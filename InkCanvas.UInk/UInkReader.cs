using System;
using System.Collections.Generic;
using System.IO;
using InkUInk.Model;
using InkUInk.MsgPack;

namespace InkUInk
{
    /// <summary>文件不是 UInk 1.x 时抛出（VER-3、HDR-2）。</summary>
    public sealed class UInkFormatException : Exception
    {
        public UInkFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// UInk 1.1 读取器。按规范「读取流程」实现：
    /// 验证 Header → 读可选 Header Extension → 逐对象解码并归属 Canvas →
    /// 结尾不完整丢弃尾块、中间损坏停止读取 → 用实际对象重建页面模型。
    /// </summary>
    public static class UInkReader
    {
        private sealed class ParsedObject
        {
            public object Value;
            public int Start;
            public int End;
            public bool Invalid;
            public bool IsCanvas;
        }

        public static UInkDocument ReadFile(string path)
        {
            return Read(File.ReadAllBytes(path));
        }

        public static UInkDocument Read(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            var doc = new UInkDocument();
            List<ParsedObject> objects = DecodeObjects(bytes, doc);

            if (objects.Count == 0)
                throw new UInkFormatException("文件中没有可解码的 MessagePack 对象");

            doc.Header = ParseHeader(objects[0].Value);
            doc.SafeAppendPosition = doc.Status == UInkReadStatus.TruncatedTail ? objects[objects.Count - 1].End : -1;

            int index = 1;
            if (index < objects.Count)
            {
                var ext = ParseExtension(objects[index].Value);
                if (ext != null)
                {
                    doc.Extension = ext;
                    foreach (var device in ext.Devices) doc.Devices.Add(device);
                    foreach (var workspace in ext.Workspaces) doc.Workspaces.Add(workspace);
                    index++;
                }
            }

            var layers = new List<UInkLayer>();
            var layerMap = new Dictionary<string, UInkLayer>(StringComparer.Ordinal);
            UInkLayer current = null;

            for (; index < objects.Count; index++)
            {
                var parsed = objects[index];
                if (parsed.Invalid)
                {
                    // FILE-3: 即使 Canvas 无效，它也结束前一个 Canvas 的作用域。
                    if (parsed.IsCanvas) current = null;
                    continue;
                }

                var map = parsed.Value as IDictionary<string, object>;
                if (map == null)
                {
                    doc.Warnings.Add($"对象 #{index} 不是 Map，已跳过");
                    doc.HasUnknownContent = true;
                    continue;
                }

                int? typeId = UInkValues.Int32(map, "type");
                if (!typeId.HasValue)
                {
                    doc.Warnings.Add($"对象 #{index} 缺少 type，已跳过");
                    doc.HasUnknownContent = true;
                    continue;
                }

                switch (typeId.Value)
                {
                    case UInkTypeId.Canvas:
                    {
                        current = null;
                        var canvas = ParseCanvas(map, index, doc);
                        if (canvas == null) continue;

                        var layer = new UInkLayer { Canvas = canvas };
                        string key = canvas.UniqueKey;

                        UInkLayer existing;
                        if (layerMap.TryGetValue(key, out existing))
                        {
                            // FILE-8：同一唯一键后出现的整体替代先出现的，旧版本不显示也不参与编号。
                            for (int i = 0; i < layers.Count; i++)
                            {
                                if (ReferenceEquals(layers[i], existing)) { layers.RemoveAt(i); break; }
                            }
                        }

                        layerMap[key] = layer;
                        layers.Add(layer);
                        current = layer;
                        break;
                    }

                    case UInkTypeId.Ink:
                    case UInkTypeId.Shape:
                    case UInkTypeId.Media:
                    case UInkTypeId.Clear:
                    {
                        if (current == null)
                        {
                            // FILE-4：第一个 Canvas 之前不得出现内容块。
                            doc.Warnings.Add($"对象 #{index}（type {typeId.Value}）没有有效的 Canvas，已跳过");
                            doc.HasUnknownContent = true;
                            continue;
                        }

                        UInkBlock block = null;
                        if (typeId.Value == UInkTypeId.Ink) block = ParseInk(map, doc);
                        else if (typeId.Value == UInkTypeId.Shape) block = ParseShape(map, doc);
                        else if (typeId.Value == UInkTypeId.Media) block = ParseMedia(map, doc);
                        else block = ParseClear(map, doc);

                        if (block != null) current.Blocks.Add(block);
                        break;
                    }

                    default:
                        // TYPE-9：不认识的 Type ID，跳过整个对象继续读取。
                        doc.Warnings.Add($"跳过不认识的块 type = {typeId.Value}");
                        doc.HasUnknownContent = true;
                        break;
                }
            }

            UInkPageBuilder.Build(doc, layers);
            return doc;
        }

        private static List<ParsedObject> DecodeObjects(byte[] bytes, UInkDocument doc)
        {
            var objects = new List<ParsedObject>();
            var reader = new MsgPackReader(bytes);

            while (!reader.EndOfStream)
            {
                int start = reader.Position;
                try
                {
                    object value = reader.ReadValue();
                    objects.Add(new ParsedObject { Value = value, Start = start, End = reader.Position });
                }
                catch (MsgPackTruncatedException)
                {
                    // FILE-16：结尾不完整，丢弃该对象。
                    doc.Status = UInkReadStatus.TruncatedTail;
                    doc.Warnings.Add("文件结尾不完整，已丢弃最后一个未写完的对象");
                    break;
                }
                catch (MsgPackCorruptException ex)
                {
                    // FILE-17：中间损坏，保留之前的对象并停止读取。
                    doc.Status = UInkReadStatus.CorruptMiddle;
                    doc.Warnings.Add("文件中间存在无法解码的字节，已停止读取：" + ex.Message);
                    break;
                }
                catch (MsgPackDuplicateKeyException ex)
                {
                    var map = ex.Value as IDictionary<string, object>;
                    bool isCanvas = map != null && UInkValues.Int32(map, "type") == UInkTypeId.Canvas;
                    if (!isCanvas && ex.DuplicateRootTypes != null)
                    {
                        foreach (var value in ex.DuplicateRootTypes)
                        {
                            if (!UInkValues.TryGetLong(value, out long type) || type != UInkTypeId.Canvas) continue;
                            isCanvas = true;
                            break;
                        }
                    }

                    doc.Warnings.Add($"对象 @{start} 重复出现键，已跳过该块");
                    doc.HasUnknownContent = true;
                    objects.Add(new ParsedObject { Invalid = true, IsCanvas = isCanvas, Start = start, End = reader.Position });
                }
            }

            return objects;
        }

        private static UInkHeader ParseHeader(object value)
        {
            var array = value as List<object>;
            if (array == null || array.Count != 7)
                throw new UInkFormatException("不是 UInk 1.x 文件：首块不是 array(7) 的 Header");

            long type;
            if (!UInkValues.TryGetLong(array[0], out type) || type != UInkTypeId.Header)
                throw new UInkFormatException("不是 UInk 1.x 文件：Header.type 不是 0");

            long version;
            if (!UInkValues.TryGetLong(array[1], out version) || version < 10 || version > 19)
                throw new UInkFormatException($"不支持的 UInk 版本：{version}（只接受 10–19）");

            string guid = array[2] as string;
            if (!UInkValues.IsUuid(guid))
                throw new UInkFormatException("Header.guid 不是合法的 UUID");

            uint deviceNum, workspaceNum, pageNum;
            ulong time;
            if (!UInkValues.TryGetLong(array[3], out var d) || d < 0 || d > uint.MaxValue)
                throw new UInkFormatException("Header.deviceNum 无效");
            if (!UInkValues.TryGetLong(array[4], out var w) || w < 0 || w > uint.MaxValue)
                throw new UInkFormatException("Header.workspaceNum 无效");
            if (!UInkValues.TryGetLong(array[5], out var p) || p < 0 || p > uint.MaxValue)
                throw new UInkFormatException("Header.pageNum 无效");
            if (!UInkValues.TryGetULong(array[6], out time))
                throw new UInkFormatException("Header.time 无效");

            deviceNum = (uint)d;
            workspaceNum = (uint)w;
            pageNum = (uint)p;

            return new UInkHeader
            {
                Version = (ushort)version,
                Guid = guid,
                DeviceNum = deviceNum,
                WorkspaceNum = workspaceNum,
                PageNum = pageNum,
                Time = time
            };
        }

        private static UInkHeaderExtension ParseExtension(object value)
        {
            var map = value as IDictionary<string, object>;
            if (map == null) return null;

            int? type = UInkValues.Int32(map, "type");
            if (type != UInkTypeId.HeaderExtension) return null;

            var ext = new UInkHeaderExtension
            {
                Name = UInkValues.String(map, "name"),
                Explanation = UInkValues.String(map, "explanation")
            };

            var devices = UInkValues.Array(map, "devices");
            if (devices != null)
            {
                foreach (var item in devices)
                {
                    var device = ParseDevice(item as IDictionary<string, object>);
                    if (device != null) ext.Devices.Add(device);
                }
            }

            var workspaces = UInkValues.Array(map, "workspaces");
            if (workspaces != null)
            {
                foreach (var item in workspaces)
                {
                    var workspace = ParseWorkspace(item as IDictionary<string, object>);
                    if (workspace != null) ext.Workspaces.Add(workspace);
                }
            }

            return ext;
        }

        private static UInkDevice ParseDevice(IDictionary<string, object> map)
        {
            if (map == null) return null;

            string guid = UInkValues.String(map, "guid");
            if (!UInkValues.IsUuid(guid)) return null;

            int? deviceType = UInkValues.Int32(map, "deviceType");
            if (!deviceType.HasValue) return null;

            var device = new UInkDevice
            {
                Guid = guid,
                DeviceType = deviceType.Value,
                Name = UInkValues.String(map, "name")
            };

            if (deviceType.Value == 0)
            {
                int? x = UInkValues.Int32(map, "x");
                int? y = UInkValues.Int32(map, "y");
                uint? width = UInkValues.UInt32(map, "width");
                uint? height = UInkValues.UInt32(map, "height");
                if (!x.HasValue || !y.HasValue || !width.HasValue || !height.HasValue) return null;
                if (width.Value == 0 || height.Value == 0) return null;

                device.X = x.Value;
                device.Y = y.Value;
                device.Width = width.Value;
                device.Height = height.Value;

                var hardware = UInkValues.Map(map, "hardware");
                if (hardware != null)
                {
                    device.Hardware = new UInkHardware
                    {
                        Name = UInkValues.String(hardware, "name"),
                        Id = UInkValues.String(hardware, "id"),
                        PhysicalWidth = UInkValues.UInt32(hardware, "physicalWidth"),
                        PhysicalHeight = UInkValues.UInt32(hardware, "physicalHeight"),
                        ScaleFactor = UInkValues.FiniteDouble(hardware, "scaleFactor")
                    };
                }
            }
            else if (deviceType.Value == 1)
            {
                string parent = UInkValues.String(map, "parentDeviceGuid");
                double? x = UInkValues.FiniteDouble(map, "x");
                double? y = UInkValues.FiniteDouble(map, "y");
                double? width = UInkValues.FiniteDouble(map, "width");
                double? height = UInkValues.FiniteDouble(map, "height");
                uint? zIndex = UInkValues.UInt32(map, "zIndex");
                if (!UInkValues.IsUuid(parent) || !x.HasValue || !y.HasValue ||
                    !width.HasValue || !height.HasValue || !zIndex.HasValue) return null;
                if (width.Value <= 0 || height.Value <= 0) return null;

                device.ParentDeviceGuid = parent;
                device.WindowX = x.Value;
                device.WindowY = y.Value;
                device.WindowWidth = width.Value;
                device.WindowHeight = height.Value;
                device.ZIndex = zIndex.Value;
            }
            else
            {
                // DEV-8：不认识的 deviceType 当作临时根显示区域。
                device.DeviceType = 0;
                device.Width = 1;
                device.Height = 1;
            }

            return device;
        }

        private static UInkWorkspace ParseWorkspace(IDictionary<string, object> map)
        {
            if (map == null) return null;

            string guid = UInkValues.String(map, "guid");
            if (!UInkValues.IsUuid(guid)) return null;

            return new UInkWorkspace
            {
                Guid = guid,
                WorkspaceType = UInkValues.Int32(map, "workspaceType") ?? UInkWorkspaceType.ScreenAnnotation,
                Name = UInkValues.String(map, "name"),
                ParentWorkspaceGuid = UInkValues.String(map, "parentWorkspaceGuid"),
                HostId = UInkValues.String(map, "hostId"),
                CurrentPageIndex = UInkValues.UInt32(map, "currentPageIndex") ?? 0
            };
        }

        private static UInkCanvas ParseCanvas(IDictionary<string, object> map, int index, UInkDocument doc)
        {
            string pageGuid = UInkValues.String(map, "pageGuid");
            if (!UInkValues.IsUuid(pageGuid))
            {
                // CANVAS-25：生成临时页面，不写回文件。
                pageGuid = "00000000-0000-4000-8000-" + index.ToString("D12");
                doc.Warnings.Add($"Canvas @{index} 的 pageGuid 无效，已生成临时页面");
                doc.HasUnknownContent = true;
            }

            var canvas = new UInkCanvas
            {
                WorkspaceGuid = UInkValues.String(map, "workspaceGuid"),
                DeviceGuid = UInkValues.String(map, "deviceGuid"),
                PageGuid = pageGuid,
                PageIndex = UInkValues.UInt32(map, "pageIndex") ?? 0,
                PageNumber = UInkValues.UInt32(map, "pageNumber") ?? 0,
                LayerIndex = UInkValues.UInt32(map, "layerIndex") ?? 0,
                LayerNumber = UInkValues.UInt32(map, "layerNumber") ?? 0,
                PageState = UInkValues.Int32(map, "pageState") ?? 0,
                PageKind = UInkValues.Int32(map, "pageKind") ?? 0,
                Binding = UInkValues.Int32(map, "binding") ?? 0,
                SlideId = UInkValues.Int32(map, "slideId")
            };

            // CANVAS-8：页面元数据只写在第 0 层，其他图层上的这些字段忽略。
            if (canvas.LayerIndex != 0)
            {
                return canvas;
            }

            var viewport = UInkValues.Map(map, "viewport");
            if (viewport != null)
            {
                double? x = UInkValues.FiniteDouble(viewport, "x");
                double? y = UInkValues.FiniteDouble(viewport, "y");
                double? scale = UInkValues.FiniteDouble(viewport, "scale");
                if (x.HasValue && y.HasValue && scale.HasValue && scale.Value > 0)
                    canvas.Viewport = new UInkViewport { X = x.Value, Y = y.Value, Scale = scale.Value };
                else
                    doc.Warnings.Add($"Canvas @{index} 的 viewport 无效，使用缺省值"); // CANVAS-22
            }

            var background = UInkValues.Map(map, "background");
            if (background != null)
            {
                var colorMap = UInkValues.Map(background, "color");
                var color = ParseColor(colorMap);
                if (color != null)
                {
                    canvas.Background = new UInkBackground
                    {
                        Color = color,
                        Pattern = UInkValues.Int32(background, "pattern") ?? 0
                    };
                }
            }

            return canvas;
        }

        private static UInkBlock ParseInk(IDictionary<string, object> map, UInkDocument doc)
        {
            uint? contentId = UInkValues.UInt32(map, "contentId");
            uint? undoId = UInkValues.UInt32(map, "undoId");
            var pointsArray = UInkValues.Array(map, "points");
            if (!contentId.HasValue || !undoId.HasValue || pointsArray == null || pointsArray.Count == 0)
            {
                doc.Warnings.Add("跳过字段不完整或没有点的 Ink（INK-19）");
                doc.HasUnknownContent = true;
                return null;
            }

            var ink = new UInkInk
            {
                ContentId = contentId.Value,
                UndoId = undoId.Value,
                InkType = UInkValues.Int32(map, "inkType") ?? UInkInkType.Pen,
                Color = ParseColor(UInkValues.Map(map, "color")),
                Opacity = UInkValues.ClampOpacity(UInkValues.FiniteDouble(map, "opacity")) ?? 1.0,
                Texture = UInkValues.Int32(map, "texture") ?? 0,
                Cap = UInkValues.Int32(map, "cap"),
                RenderOnlyWhenLatest = UInkValues.Bool(map, "renderOnlyWhenLatest") ?? false
            };

            var nibMap = UInkValues.Map(map, "nib");
            if (nibMap != null)
            {
                ink.Nib = new UInkNib
                {
                    Shape = UInkValues.Int32(nibMap, "shape") ?? 0,
                    Aspect = UInkValues.FiniteDouble(nibMap, "aspect") ?? 0,
                    Orientation = UInkValues.Int32(nibMap, "orientation") ?? 0
                };
            }

            if (ink.Color == null)
            {
                doc.Warnings.Add("跳过缺少颜色的 Ink（INK-19）");
                doc.HasUnknownContent = true;
                return null;
            }

            for (int i = 0; i < pointsArray.Count; i++)
            {
                var pointMap = pointsArray[i] as IDictionary<string, object>;
                if (pointMap == null) { ink.Points.Clear(); break; }

                double? x = UInkValues.FiniteDouble(pointMap, "x");
                double? y = UInkValues.FiniteDouble(pointMap, "y");
                double? width = UInkValues.FiniteDouble(pointMap, "width");
                if (!x.HasValue || !y.HasValue || !width.HasValue || width.Value <= 0)
                {
                    ink.Points.Clear();
                    break;
                }

                ink.Points.Add(new UInkPoint
                {
                    X = x.Value,
                    Y = y.Value,
                    Width = width.Value,
                    Color = ParseColor(UInkValues.Map(pointMap, "color")),
                    Opacity = UInkValues.ClampOpacity(UInkValues.FiniteDouble(pointMap, "opacity"))
                });
            }

            if (ink.Points.Count == 0)
            {
                doc.Warnings.Add("跳过点结构损坏的 Ink（INK-19）");
                doc.HasUnknownContent = true;
                return null;
            }

            // INK-4：文件中首点是绝对坐标、之后是相对前一点的位移。模型统一保存绝对坐标，
            // 这里用 float64 累加（规范明确要求），既避免长笔迹的累积误差，
            // 也保证「读 → 写 → 读」时位移差分与累加互为逆运算，字节稳定。
            double absoluteX = 0, absoluteY = 0;
            for (int i = 0; i < ink.Points.Count; i++)
            {
                var point = ink.Points[i];
                if (i == 0) { absoluteX = point.X; absoluteY = point.Y; }
                else { absoluteX += point.X; absoluteY += point.Y; }

                point.X = absoluteX;
                point.Y = absoluteY;
            }

            return ink;
        }

        private static UInkBlock ParseShape(IDictionary<string, object> map, UInkDocument doc)
        {
            uint? contentId = UInkValues.UInt32(map, "contentId");
            uint? undoId = UInkValues.UInt32(map, "undoId");
            int? shapeType = UInkValues.Int32(map, "shapeType");
            var geometryMap = UInkValues.Map(map, "geometry");

            if (!contentId.HasValue || !undoId.HasValue || !shapeType.HasValue || geometryMap == null)
            {
                doc.Warnings.Add("跳过字段不完整的 Shape（SHAPE-19）");
                doc.HasUnknownContent = true;
                return null;
            }

            if (shapeType.Value < 0 || shapeType.Value > 6)
            {
                // SHAPE-20：不认识的 shapeType 整个跳过，不猜测。
                doc.Warnings.Add($"跳过不认识的 shapeType = {shapeType.Value}");
                doc.HasUnknownContent = true;
                return null;
            }

            var shape = new UInkShape
            {
                ContentId = contentId.Value,
                UndoId = undoId.Value,
                ShapeType = shapeType.Value,
                RenderOnlyWhenLatest = UInkValues.Bool(map, "renderOnlyWhenLatest") ?? false
            };

            shape.Geometry = ParseGeometry(shapeType.Value, geometryMap);
            if (shape.Geometry == null)
            {
                doc.Warnings.Add("跳过几何无效的 Shape（SHAPE-22）");
                doc.HasUnknownContent = true;
                return null;
            }

            var strokeMap = UInkValues.Map(map, "stroke");
            if (strokeMap != null)
            {
                shape.Stroke = new UInkStrokeStyle
                {
                    Color = ParseColor(UInkValues.Map(strokeMap, "color")),
                    Opacity = UInkValues.ClampOpacity(UInkValues.FiniteDouble(strokeMap, "opacity")) ?? 1.0,
                    Width = UInkValues.FiniteDouble(strokeMap, "width") ?? 0,
                    DashArray = UInkValues.FloatArray(strokeMap, "dashArray", 0),
                    DashOffset = UInkValues.FiniteDouble(strokeMap, "dashOffset") ?? 0,
                    LineCap = UInkValues.Int32(strokeMap, "lineCap") ?? 0,
                    LineJoin = UInkValues.Int32(strokeMap, "lineJoin") ?? 0,
                    StartMarker = UInkValues.Int32(strokeMap, "startMarker") ?? 0,
                    EndMarker = UInkValues.Int32(strokeMap, "endMarker") ?? 0
                };

                if (shape.Stroke.Color == null || shape.Stroke.Width <= 0) shape.Stroke = null;
            }

            var fillMap = UInkValues.Map(map, "fill");
            if (fillMap != null)
            {
                var fillColor = ParseColor(UInkValues.Map(fillMap, "color"));
                if (fillColor != null)
                {
                    shape.Fill = new UInkFill
                    {
                        FillType = UInkValues.Int32(fillMap, "fillType") ?? 0,
                        Color = fillColor,
                        Opacity = UInkValues.ClampOpacity(UInkValues.FiniteDouble(fillMap, "opacity")) ?? 1.0
                    };
                }
            }

            // SHAPE-4：至少有描边或填充；Line / Polyline 必须有描边。
            bool isOpen = shapeType.Value == UInkShapeType.Line || shapeType.Value == UInkShapeType.Polyline;
            if (isOpen && shape.Stroke == null)
            {
                doc.Warnings.Add("跳过没有有效描边的 Line / Polyline（SHAPE-22）");
                doc.HasUnknownContent = true;
                return null;
            }
            if (!isOpen && shape.Stroke == null && shape.Fill == null)
            {
                doc.Warnings.Add("跳过既没有描边也没有填充的 Shape（SHAPE-22）");
                doc.HasUnknownContent = true;
                return null;
            }

            var objectMap = UInkValues.Map(map, "object");
            if (objectMap != null)
            {
                var obj = new UInkShapeObject
                {
                    Id = UInkValues.String(objectMap, "id"),
                    Revision = UInkValues.UInt64(objectMap, "revision") ?? 0,
                    Preset = UInkValues.String(objectMap, "preset"),
                    PartCount = UInkValues.UInt32(objectMap, "partCount") ?? 0,
                    Transform = UInkValues.FloatArray(objectMap, "transform", 6),
                    RefId = UInkValues.String(objectMap, "ref"),
                    PartIndex = UInkValues.UInt32(objectMap, "partIndex")
                };

                if (obj.RefId != null || UInkValues.IsUuid(obj.Id)) shape.Object = obj;
            }

            return shape;
        }

        private static UInkGeometry ParseGeometry(int shapeType, IDictionary<string, object> map)
        {
            var geometry = new UInkGeometry();

            switch (shapeType)
            {
                case UInkShapeType.Line:
                case UInkShapeType.Polyline:
                case UInkShapeType.Polygon:
                {
                    var points = UInkValues.Array(map, "points");
                    if (points == null) return null;
                    foreach (var item in points)
                    {
                        var pointMap = item as IDictionary<string, object>;
                        if (pointMap == null) return null;
                        double? x = UInkValues.FiniteDouble(pointMap, "x");
                        double? y = UInkValues.FiniteDouble(pointMap, "y");
                        if (!x.HasValue || !y.HasValue) return null;
                        geometry.Points.Add(new UInkGeomPoint { X = x.Value, Y = y.Value });
                    }

                    int min = shapeType == UInkShapeType.Line ? 2 : (shapeType == UInkShapeType.Polyline ? 2 : 3);
                    if (shapeType == UInkShapeType.Line && geometry.Points.Count != 2) return null;
                    if (geometry.Points.Count < min) return null;
                    return geometry;
                }

                case UInkShapeType.Rectangle:
                {
                    double? cx = UInkValues.FiniteDouble(map, "centerX");
                    double? cy = UInkValues.FiniteDouble(map, "centerY");
                    double? w = UInkValues.FiniteDouble(map, "width");
                    double? h = UInkValues.FiniteDouble(map, "height");
                    if (!cx.HasValue || !cy.HasValue || !w.HasValue || !h.HasValue || w.Value <= 0 || h.Value <= 0) return null;

                    geometry.CenterX = cx;
                    geometry.CenterY = cy;
                    geometry.Width = w;
                    geometry.Height = h;
                    geometry.Rotation = UInkValues.FiniteDouble(map, "rotation") ?? 0;
                    geometry.CornerRadiusX = UInkValues.FiniteDouble(map, "cornerRadiusX");
                    geometry.CornerRadiusY = UInkValues.FiniteDouble(map, "cornerRadiusY");
                    return geometry;
                }

                case UInkShapeType.Square:
                {
                    double? cx = UInkValues.FiniteDouble(map, "centerX");
                    double? cy = UInkValues.FiniteDouble(map, "centerY");
                    double? size = UInkValues.FiniteDouble(map, "size");
                    if (!cx.HasValue || !cy.HasValue || !size.HasValue || size.Value <= 0) return null;

                    geometry.CenterX = cx;
                    geometry.CenterY = cy;
                    geometry.Size = size;
                    geometry.Rotation = UInkValues.FiniteDouble(map, "rotation") ?? 0;
                    return geometry;
                }

                case UInkShapeType.Ellipse:
                {
                    double? cx = UInkValues.FiniteDouble(map, "centerX");
                    double? cy = UInkValues.FiniteDouble(map, "centerY");
                    double? w = UInkValues.FiniteDouble(map, "width");
                    double? h = UInkValues.FiniteDouble(map, "height");
                    if (!cx.HasValue || !cy.HasValue || !w.HasValue || !h.HasValue || w.Value <= 0 || h.Value <= 0) return null;

                    geometry.CenterX = cx;
                    geometry.CenterY = cy;
                    geometry.Width = w;
                    geometry.Height = h;
                    geometry.Rotation = UInkValues.FiniteDouble(map, "rotation") ?? 0;
                    return geometry;
                }

                case UInkShapeType.Circle:
                {
                    double? cx = UInkValues.FiniteDouble(map, "centerX");
                    double? cy = UInkValues.FiniteDouble(map, "centerY");
                    double? radius = UInkValues.FiniteDouble(map, "radius");
                    if (!cx.HasValue || !cy.HasValue || !radius.HasValue || radius.Value <= 0) return null;

                    geometry.CenterX = cx;
                    geometry.CenterY = cy;
                    geometry.Radius = radius;
                    return geometry;
                }
            }

            return null;
        }

        private static UInkBlock ParseMedia(IDictionary<string, object> map, UInkDocument doc)
        {
            uint? contentId = UInkValues.UInt32(map, "contentId");
            uint? undoId = UInkValues.UInt32(map, "undoId");
            string path = UInkValues.String(map, "path");
            string mimeType = UInkValues.String(map, "mimeType");

            if (!contentId.HasValue || !undoId.HasValue || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(mimeType))
            {
                doc.Warnings.Add("跳过字段不完整的 Media（MEDIA-13）");
                doc.HasUnknownContent = true;
                return null;
            }

            var media = new UInkMedia
            {
                ContentId = contentId.Value,
                UndoId = undoId.Value,
                Path = path,
                MimeType = mimeType,
                Width = UInkValues.FiniteDouble(map, "width"),
                Height = UInkValues.FiniteDouble(map, "height"),
                Transform = UInkValues.FloatArray(map, "transform", 6),
                Opacity = UInkValues.ClampOpacity(UInkValues.FiniteDouble(map, "opacity")) ?? 1.0,
                Fit = UInkValues.Int32(map, "fit") ?? 0,
                PageCount = UInkValues.UInt32(map, "pageCount"),
                PageIndex = UInkValues.UInt32(map, "pageIndex") ?? 0,
                Autoplay = UInkValues.Bool(map, "autoplay") ?? false,
                Loop = UInkValues.Bool(map, "loop") ?? false,
                Volume = UInkValues.ClampOpacity(UInkValues.FiniteDouble(map, "volume")) ?? 1.0,
                StartTime = UInkValues.FiniteDouble(map, "startTime") ?? 0,
                PlaybackRate = UInkValues.FiniteDouble(map, "playbackRate") ?? 1.0
            };

            var cropMap = UInkValues.Map(map, "crop");
            if (cropMap != null)
            {
                double? x = UInkValues.FiniteDouble(cropMap, "x");
                double? y = UInkValues.FiniteDouble(cropMap, "y");
                double? w = UInkValues.FiniteDouble(cropMap, "width");
                double? h = UInkValues.FiniteDouble(cropMap, "height");
                if (x.HasValue && y.HasValue && w.HasValue && h.HasValue && w.Value > 0 && h.Value > 0)
                    media.Crop = new UInkCrop { X = x.Value, Y = y.Value, Width = w.Value, Height = h.Value };
            }

            bool visual = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                          || mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(mimeType, "application/pdf", StringComparison.OrdinalIgnoreCase);

            if (visual && (!media.Width.HasValue || !media.Height.HasValue ||
                            media.Width.Value <= 0 || media.Height.Value <= 0))
            {
                doc.Warnings.Add("跳过缺少 width / height 的视觉 Media（MEDIA-13）");
                doc.HasUnknownContent = true;
                return null;
            }

            return media;
        }

        private static UInkBlock ParseClear(IDictionary<string, object> map, UInkDocument doc)
        {
            uint? contentId = UInkValues.UInt32(map, "contentId");
            uint? undoId = UInkValues.UInt32(map, "undoId");
            if (!contentId.HasValue || !undoId.HasValue)
            {
                doc.Warnings.Add("跳过字段不完整的 Clear（CLEAR-12）");
                doc.HasUnknownContent = true;
                return null;
            }

            return new UInkClear { ContentId = contentId.Value, UndoId = undoId.Value };
        }

        private static UInkColor ParseColor(IDictionary<string, object> map)
        {
            if (map == null) return null;

            uint? fallback = UInkValues.UInt32(map, "fallback");
            if (!fallback.HasValue) return null;

            var color = new UInkColor(fallback.Value & 0x00FFFFFFu); // COLOR-3：只取低 24 位
            string space = UInkValues.String(map, "space");
            var components = UInkValues.FloatArray(map, "components", 3);

            // COLOR-2：只有 space 与 components 都有效时才采用扩展颜色。
            if (space != null && components != null)
            {
                bool valid = space == "srgb" || space == "scrgb";
                if (valid && space == "srgb")
                {
                    foreach (double c in components)
                        if (c < 0 || c > 1) valid = false;
                }
                if (valid)
                {
                    color.Space = space;
                    color.Components = components;
                }
            }

            return color;
        }
    }
}
