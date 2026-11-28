using System;
using System.Collections.Generic;
using InkUInk.Model;

namespace InkUInk
{
    /// <summary>
    /// 写出前的规范化：把任意内存中的文档整理成一份「严格符合 UInk 1.1 写入方规则」的快照。
    /// 目的是让 UInkWriter 只负责编码，不必在每个字段上重复判断。
    /// 覆盖的强制条款：TYPE-1/3、HDR-1/3/4、HEXT-2/5/6/9、CANVAS-1/3/4/5/8/13、
    /// INK-2/3/4/5/18/19、SHAPE-1/2/3/4/7/9/11/14/22/23、MEDIA-12/13/14、
    /// CLEAR-4/11、SAVE-2/3、COMPAT-5/6。
    /// </summary>
    public static class UInkNormalizer
    {
        private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

        public static UInkNormalizedDocument Build(UInkDocument document, UInkWriteOptions options)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            options = options ?? new UInkWriteOptions();

            var result = new UInkNormalizedDocument();

            // ---- 1. 复制图层（内容块在规范化前再复制）----
            var entries = new List<Entry>();
            int sequence = 0;

            foreach (var page in document.Pages)
            {
                if (page == null) continue;
                foreach (var layer in page.Layers)
                {
                    if (layer == null || layer.Canvas == null) continue;
                    entries.Add(new Entry
                    {
                        Canvas = CloneCanvas(layer.Canvas),
                        Blocks = new List<UInkBlock>(layer.Blocks),
                        Sequence = sequence++
                    });
                }
            }

            // ---- 2. 注册表：GUID 合法化与去重 ----
            var workspaces = CopyWorkspaces(document);
            var devices = CopyDevices(document);

            var wsRemap = NormalizeRegistry(workspaces, w => w.Guid, (w, g) => w.Guid = g, result.Warnings, "Workspace");
            var devRemap = NormalizeRegistry(devices, d => d.Guid, (d, g) => d.Guid = g, result.Warnings, "Device");

            foreach (var entry in entries)
            {
                entry.Canvas.WorkspaceGuid = Remap(entry.Canvas.WorkspaceGuid, wsRemap);
                entry.Canvas.DeviceGuid = Remap(entry.Canvas.DeviceGuid, devRemap);
            }

            // ---- 3. 等级导出：多 Device / 多图层 ----
            if (options.TargetLevel < UInkTargetLevel.Full)
            {
                if (devices.Count > 0)
                {
                    string keep = devices[0].Guid;
                    int removed = entries.RemoveAll(e => !GuidEquals(e.Canvas.DeviceGuid, keep));
                    if (removed > 0)
                        result.Warnings.Add($"导出到 {options.TargetLevel} 级：已丢弃 {removed} 个非首个 Device 的 Canvas（COMPAT 兼容导出）");
                }

                entries = MergeLayers(entries);

                // COMPAT-5：注册表也一并收窄，避免留下没有任何 Canvas 的 Device。
                if (devices.Count > 1)
                {
                    var usedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in entries)
                    {
                        if (entry.Canvas.DeviceGuid != null) usedDevices.Add(entry.Canvas.DeviceGuid);
                    }

                    int removedDevices = usedDevices.Count > 0 ? devices.RemoveAll(d => !usedDevices.Contains(d.Guid)) : 0;
                    if (removedDevices > 0)
                        result.Warnings.Add($"导出到 {options.TargetLevel} 级：已收窄 Device 注册表（-{removedDevices}）");
                }
            }

            // ---- 4. 隐式单例与显式注册表的一致性（HEXT-5）----
            EnsureRegistryConsistency(entries, workspaces, devices, result.Warnings);

            // ---- 5. 内容块规范化（含等级过滤）----
            foreach (var entry in entries)
            {
                entry.Blocks = NormalizeBlocks(entry.Blocks, options, result.Warnings);
            }

            // ---- 6. 紧凑化：Canvas 唯一键去重，后者整体替代前者（CANVAS-5 / SAVE-2）----
            var byKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var keyOrder = new List<string>();

            foreach (var entry in entries)
            {
                string key = UniqueKey(entry.Canvas);
                if (!byKey.ContainsKey(key)) keyOrder.Add(key);
                byKey[key] = entry;   // 后出现的赢
            }

            var compact = new List<Entry>(keyOrder.Count);
            foreach (var key in keyOrder) compact.Add(byKey[key]);

            // ---- 7. 页面身份：pageGuid 唯一、pageIndex / layerIndex 连续（CANVAS-1/3/4）----
            NormalizePageIdentity(compact, result.Warnings);

            // ---- 8. 排序：Workspace → pageIndex → Device → layerIndex（SAVE-3）----
            var wsOrder = BuildOrder(workspaces);
            var devOrder = BuildOrder(devices);

            foreach (var entry in compact)
            {
                entry.WorkspaceOrder = IndexOf(wsOrder, entry.Canvas.WorkspaceGuid);
                entry.DeviceOrder = IndexOf(devOrder, entry.Canvas.DeviceGuid);
            }

            compact.Sort(CompareEntries);

            // ---- 9. Header 与 Extension ----
            result.Header = BuildHeader(document, compact, workspaces, devices, options);
            result.Extension = BuildExtension(document, workspaces, devices);

            foreach (var entry in compact)
            {
                var layer = new UInkLayer { Canvas = entry.Canvas };
                layer.Blocks.AddRange(entry.Blocks);
                result.Layers.Add(layer);
            }

            return result;
        }

        // ------------------------------------------------------------------
        // 注册表
        // ------------------------------------------------------------------

        private sealed class Entry
        {
            public UInkCanvas Canvas;
            public List<UInkBlock> Blocks;
            public int Sequence;
            public int WorkspaceOrder;
            public int DeviceOrder;
        }

        private static List<UInkWorkspace> CopyWorkspaces(UInkDocument document)
        {
            var source = document.Extension != null && document.Extension.Workspaces.Count > 0
                ? document.Extension.Workspaces
                : document.Workspaces;

            var list = new List<UInkWorkspace>(source.Count);
            foreach (var item in source)
            {
                if (item == null) continue;
                list.Add(new UInkWorkspace
                {
                    Guid = item.Guid,
                    WorkspaceType = item.WorkspaceType,
                    Name = item.Name,
                    ParentWorkspaceGuid = item.ParentWorkspaceGuid,
                    HostId = item.HostId,
                    CurrentPageIndex = item.CurrentPageIndex
                });
            }
            return list;
        }

        private static List<UInkDevice> CopyDevices(UInkDocument document)
        {
            var source = document.Extension != null && document.Extension.Devices.Count > 0
                ? document.Extension.Devices
                : document.Devices;

            var list = new List<UInkDevice>(source.Count);
            foreach (var item in source)
            {
                if (item == null) continue;
                list.Add(new UInkDevice
                {
                    Guid = item.Guid,
                    DeviceType = item.DeviceType,
                    Name = item.Name,
                    Hardware = item.Hardware,
                    X = item.X,
                    Y = item.Y,
                    Width = item.Width,
                    Height = item.Height,
                    ParentDeviceGuid = item.ParentDeviceGuid,
                    WindowX = item.WindowX,
                    WindowY = item.WindowY,
                    WindowWidth = item.WindowWidth,
                    WindowHeight = item.WindowHeight,
                    ZIndex = item.ZIndex
                });
            }
            return list;
        }

        /// <summary>GUID 必须是 36 字符 UUID；重复或非法时重新生成，并返回旧值到新值的映射。</summary>
        private static Dictionary<string, string> NormalizeRegistry<T>(
            List<T> items,
            Func<T, string> getter,
            Action<T, string> setter,
            List<string> warnings,
            string label)
        {
            var remap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int repaired = 0;

            foreach (var item in items)
            {
                string original = getter(item);
                string guid = original;

                if (!UInkValues.IsUuid(guid) || !seen.Add(guid))
                {
                    guid = NewGuid();
                    seen.Add(guid);
                    setter(item, guid);
                    repaired++;
                }

                if (!string.IsNullOrEmpty(original))
                {
                    remap[original] = guid;   // 重复时映射到最后一条
                }
            }

            if (repaired > 0)
                warnings.Add($"{label} 注册表有 {repaired} 个 GUID 非法或重复，已重新生成（HEXT-2）");

            return remap;
        }

        private static string Remap(string guid, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string mapped;
            return map.TryGetValue(guid, out mapped) ? mapped : guid;
        }

        private static void EnsureRegistryConsistency(
            List<Entry> entries,
            List<UInkWorkspace> workspaces,
            List<UInkDevice> devices,
            List<string> warnings)
        {
            // Workspace
            if (workspaces.Count == 0)
            {
                // HEXT-3 / HEXT-4：隐式单例没有 GUID，Canvas 不得写。
                foreach (var entry in entries) entry.Canvas.WorkspaceGuid = null;
            }
            else
            {
                var index = BuildOrder(workspaces);
                int fallbackCount = 0;

                foreach (var entry in entries)
                {
                    if (entry.Canvas.WorkspaceGuid != null && index.ContainsKey(entry.Canvas.WorkspaceGuid)) continue;
                    entry.Canvas.WorkspaceGuid = workspaces[0].Guid;
                    fallbackCount++;
                }

                if (fallbackCount > 0)
                    warnings.Add($"{fallbackCount} 个 Canvas 的 workspaceGuid 缺失或未注册，已归入第一个 Workspace（HEXT-5）");

                // HEXT-9：每个显式 Workspace 至少一个 Canvas。
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries) used.Add(entry.Canvas.WorkspaceGuid);

                foreach (var workspace in workspaces)
                {
                    if (used.Contains(workspace.Guid)) continue;
                    entries.Add(new Entry
                    {
                        Canvas = new UInkCanvas
                        {
                            WorkspaceGuid = workspace.Guid,
                            PageGuid = NewGuid(),
                            PageIndex = 0,
                            PageNumber = 1,
                            LayerIndex = 0,
                            LayerNumber = 0
                        },
                        Blocks = new List<UInkBlock>(),
                        Sequence = int.MaxValue
                    });
                    used.Add(workspace.Guid);
                    warnings.Add($"Workspace {workspace.Guid} 没有页面，已补一个空白页（HEXT-9）");
                }
            }

            // Device
            if (devices.Count == 0)
            {
                foreach (var entry in entries) entry.Canvas.DeviceGuid = null;
            }
            else
            {
                var index = BuildOrder(devices);
                int fallbackCount = 0;

                foreach (var entry in entries)
                {
                    if (entry.Canvas.DeviceGuid != null && index.ContainsKey(entry.Canvas.DeviceGuid)) continue;
                    entry.Canvas.DeviceGuid = devices[0].Guid;
                    fallbackCount++;
                }

                if (fallbackCount > 0)
                    warnings.Add($"{fallbackCount} 个 Canvas 的 deviceGuid 缺失或未注册，已归入第一个 Device（HEXT-5）");
            }

            // CANVAS-13：PPT 页按 slideId 绑定（binding = 0）时必须写 slideId。
            var workspaceType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var workspace in workspaces) workspaceType[workspace.Guid] = workspace.WorkspaceType;

            int rebound = 0;
            foreach (var entry in entries)
            {
                var canvas = entry.Canvas;
                if (canvas.LayerIndex != 0) continue;         // 元数据只在第 0 层
                if (canvas.PageKind == 1) continue;           // 结束页可以没有 slideId

                int type;
                if (canvas.WorkspaceGuid == null || !workspaceType.TryGetValue(canvas.WorkspaceGuid, out type)) continue;
                if (type != UInkWorkspaceType.Presentation) continue;
                if (canvas.Binding != 0 || canvas.SlideId.HasValue) continue;

                // 没有 slideId 就无法按 slideId 绑定，改为按页码绑定。
                canvas.Binding = 1;
                rebound++;
            }

            if (rebound > 0)
                warnings.Add($"{rebound} 个 PPT 页缺少 slideId，已改为按页码绑定（CANVAS-13）");
        }

        // ------------------------------------------------------------------
        // 图层合并
        // ------------------------------------------------------------------

        /// <summary>多图层合并到第 0 层：按 layerIndex 顺序依次拼接内容块（COMPAT 兼容导出）。</summary>
        private static List<Entry> MergeLayers(List<Entry> entries)
        {
            var order = new List<string>();
            var map = new Dictionary<string, Entry>(StringComparer.Ordinal);

            var sorted = new List<Entry>(entries);
            sorted.Sort(delegate (Entry a, Entry b)
            {
                int cmp = a.Canvas.LayerIndex.CompareTo(b.Canvas.LayerIndex);
                return cmp != 0 ? cmp : a.Sequence.CompareTo(b.Sequence);
            });

            foreach (var entry in sorted)
            {
                string key = (entry.Canvas.WorkspaceGuid ?? "") + "|" + (entry.Canvas.DeviceGuid ?? "") + "|" + (entry.Canvas.PageGuid ?? "");

                Entry target;
                if (!map.TryGetValue(key, out target))
                {
                    target = new Entry
                    {
                        Canvas = entry.Canvas,
                        Blocks = new List<UInkBlock>(),
                        Sequence = entry.Sequence
                    };
                    map[key] = target;
                    order.Add(key);
                }

                target.Blocks.AddRange(entry.Blocks);
            }

            var result = new List<Entry>(order.Count);
            foreach (var key in order)
            {
                var entry = map[key];
                entry.Canvas.LayerIndex = 0;
                entry.Canvas.LayerNumber = 0;
                result.Add(entry);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // 内容块
        // ------------------------------------------------------------------

        private static List<UInkBlock> NormalizeBlocks(List<UInkBlock> source, UInkWriteOptions options, List<string> warnings)
        {
            bool dropBeforeClear = options.DropContentBeforeLastClear || options.TargetLevel < UInkTargetLevel.Inkeys;
            bool skipMedia = options.SkipMedia || options.TargetLevel < UInkTargetLevel.Standard;

            int start = 0;
            if (dropBeforeClear)
            {
                for (int i = source.Count - 1; i >= 0; i--)
                {
                    if (source[i] is UInkClear) { start = i + 1; break; }
                }
            }

            var blocks = new List<UInkBlock>(source.Count);
            for (int i = start; i < source.Count; i++)
            {
                if (source[i] != null) blocks.Add(source[i].CopyForNormalization());
            }

            NormalizeShapeObjects(blocks, warnings);

            var output = new List<UInkBlock>(blocks.Count);

            uint contentId = 0;
            uint currentUndo = 0;
            uint previousSourceUndo = 0;
            bool hasPrevious = false;
            bool previousWasClear = false;
            var objectUndo = new Dictionary<string, uint>(StringComparer.Ordinal);

            foreach (var block in blocks)
            {
                if (skipMedia && block is UInkMedia) continue;

                uint sourceUndo = block.UndoId;
                if (!NormalizeBlock(block, options, warnings)) continue;

                // CLEAR-11：空 Canvas 上的 Clear、紧跟 Clear 的 Clear 都不产生效果。
                if (block is UInkClear && (output.Count == 0 || previousWasClear)) continue;

                if (!hasPrevious)
                {
                    currentUndo = 0;
                }
                else if (block is UInkClear)
                {
                    currentUndo++;                       // CLEAR-4：Clear 独占一组
                }
                else if (previousWasClear)
                {
                    currentUndo++;                       // CLEAR-4：Clear 之后的块必须更大
                }
                else
                {
                    string groupId = ObjectGroupId(block);
                    uint groupUndo;
                    if (groupId != null && objectUndo.TryGetValue(groupId, out groupUndo))
                    {
                        currentUndo = groupUndo;         // SHAPE-11：同一对象的部件共享 undoId
                    }
                    else if (sourceUndo != previousSourceUndo)
                    {
                        currentUndo++;
                    }
                }

                block.ContentId = contentId;
                block.UndoId = currentUndo;

                string id = ObjectGroupId(block);
                if (id != null && !objectUndo.ContainsKey(id)) objectUndo[id] = currentUndo;

                output.Add(block);

                contentId++;
                previousSourceUndo = sourceUndo;
                previousWasClear = block is UInkClear;
                hasPrevious = true;
            }

            return output;
        }

        private static string ObjectGroupId(UInkBlock block)
        {
            var shape = block as UInkShape;
            if (shape == null || shape.Object == null) return null;
            return shape.Object.RefId ?? shape.Object.Id;
        }

        /// <summary>返回 false 表示该块无法合法写出，必须跳过。</summary>
        private static bool NormalizeBlock(UInkBlock block, UInkWriteOptions options, List<string> warnings)
        {
            var ink = block as UInkInk;
            if (ink != null) return NormalizeInk(ink, options, warnings);

            var shape = block as UInkShape;
            if (shape != null) return NormalizeShape(shape, options, warnings);

            var media = block as UInkMedia;
            if (media != null) return NormalizeMedia(media, warnings);

            return block is UInkClear;
        }

        private static bool NormalizeInk(UInkInk ink, UInkWriteOptions options, List<string> warnings)
        {
            // INK-19：没有点的 Ink 跳过。
            if (ink.Points.Count == 0) return false;

            // INK-11：Erase Ink 属于 Inkeys 附加级；本库不做几何烘焙，导出到更低等级时保留并提示。
            if (ink.InkType == UInkInkType.Erase && options.TargetLevel < UInkTargetLevel.Inkeys)
            {
                warnings.Add("导出目标不含 Inkeys 附加级，但文档中有 Erase Ink；本库不做几何烘焙，需由调用方传入已擦除的笔迹（COMPAT-5）");
            }

            ink.Opacity = Clamp01(ink.Opacity);

            if (ink.Texture < 0) ink.Texture = 0;

            // INK-5：每点宽度必须大于 0。
            double width = ink.Points[0].Width;
            if (!IsPositiveFinite(width)) width = 1.0;

            foreach (var point in ink.Points)
            {
                if (!IsFinite(point.X)) point.X = 0;
                if (!IsFinite(point.Y)) point.Y = 0;
                if (!IsPositiveFinite(point.Width)) point.Width = width;

                // INK-12：点级颜色与透明度必须成对。
                bool hasColor = point.Color != null;
                bool hasOpacity = point.Opacity.HasValue;
                if (hasColor != hasOpacity)
                {
                    point.Color = null;
                    point.Opacity = null;
                }
                else if (hasOpacity)
                {
                    point.Opacity = Clamp01(point.Opacity.Value);
                }
            }

            if (ink.Nib != null)
            {
                if (ink.Nib.Shape != 0 && ink.Nib.Shape != 1) ink.Nib = null;
                else if (!IsPositiveFinite(ink.Nib.Aspect)) ink.Nib = null;
                else if (ink.Nib.Orientation != 0 && ink.Nib.Orientation != 1) ink.Nib.Orientation = 0;
            }

            // NIB-3：矩形笔尖的两端由笔尖自身决定，cap 会被忽略；写入器可以不写。
            if (ink.Nib != null && ink.Nib.IsRectangle) ink.Cap = null;

            // 端帽枚举：0 Round / 1 Square / 2 Butt / 3-127 保留 / 128+ 私有。
            if (ink.Cap.HasValue && (ink.Cap.Value < 0 || (ink.Cap.Value > 2 && ink.Cap.Value < 128))) ink.Cap = null;

            // 核心级只说 Pen / Highlighter；未知 inkType 会被读取器按 Pen 画，保留原值即可。
            return true;
        }

        private static bool NormalizeShape(UInkShape shape, UInkWriteOptions options, List<string> warnings)
        {
            var geometry = shape.Geometry;
            if (geometry == null) return false;

            switch (shape.ShapeType)
            {
                case UInkShapeType.Line:
                    if (geometry.Points.Count < 2) return false;
                    if (geometry.Points.Count > 2) geometry.Points.RemoveRange(2, geometry.Points.Count - 2);
                    break;

                case UInkShapeType.Polyline:
                    if (geometry.Points.Count < 2) return false;
                    break;

                case UInkShapeType.Polygon:
                    // SHAPE-2：不得重复写入首点。
                    if (geometry.Points.Count >= 2 && SamePoint(geometry.Points[0], geometry.Points[geometry.Points.Count - 1]))
                        geometry.Points.RemoveAt(geometry.Points.Count - 1);
                    if (geometry.Points.Count < 3) return false;
                    break;

                case UInkShapeType.Rectangle:
                    if (!HasBox(geometry)) return false;
                    NormalizeCornerRadius(geometry);
                    break;

                case UInkShapeType.Ellipse:
                    if (!HasBox(geometry)) return false;
                    geometry.CornerRadiusX = null;
                    geometry.CornerRadiusY = null;
                    break;

                case UInkShapeType.Square:
                    if (!IsPositiveFinite(geometry.Size)) return false;
                    if (!IsFinite(geometry.CenterX) || !IsFinite(geometry.CenterY)) return false;
                    break;

                case UInkShapeType.Circle:
                    if (!IsPositiveFinite(geometry.Radius)) return false;
                    if (!IsFinite(geometry.CenterX) || !IsFinite(geometry.CenterY)) return false;
                    break;

                default:
                    return false;   // SHAPE-20：不认识的 shapeType 不得写出
            }

            if (!IsFinite(geometry.Rotation)) geometry.Rotation = 0;

            foreach (var point in geometry.Points)
            {
                if (!IsFinite(point.X) || !IsFinite(point.Y)) return false;
            }

            // SHAPE-4：Line、Polyline 必须有描边且不得有填充。
            if (shape.ShapeType == UInkShapeType.Line || shape.ShapeType == UInkShapeType.Polyline)
                shape.Fill = null;

            if (shape.Stroke != null) NormalizeStroke(shape.Stroke, shape.ShapeType, warnings);
            else if (shape.ShapeType == UInkShapeType.Line || shape.ShapeType == UInkShapeType.Polyline) return false;

            if (shape.Fill != null)
            {
                if (shape.Fill.FillType != 0) shape.Fill.FillType = 0;
                shape.Fill.Opacity = Clamp01(shape.Fill.Opacity);
            }

            // SHAPE-4：至少要有描边或填充。
            if (shape.Stroke == null && shape.Fill == null) return false;

            return true;
        }

        private static void NormalizeStroke(UInkStrokeStyle stroke, int shapeType, List<string> warnings)
        {
            if (!IsPositiveFinite(stroke.Width)) stroke.Width = 1.0;
            stroke.Opacity = Clamp01(stroke.Opacity);
            if (!IsFinite(stroke.DashOffset)) stroke.DashOffset = 0;

            // SHAPE-7：非空 dashArray 必须偶数个非负有限值，且总和大于 0。
            if (stroke.DashArray != null)
            {
                var cleaned = new List<double>(stroke.DashArray.Length);
                foreach (double value in stroke.DashArray)
                {
                    if (!IsFinite(value) || value < 0) { cleaned.Clear(); break; }
                    cleaned.Add(value);
                }

                if (cleaned.Count == 0 || cleaned.Count % 2 != 0)
                {
                    if (cleaned.Count > 0)
                    {
                        cleaned.Clear();
                        warnings.Add("dashArray 长度不是偶数，已按实线写出（SHAPE-7）");
                    }
                }
                else
                {
                    double total = 0;
                    foreach (double value in cleaned) total += value;
                    if (total <= 0) cleaned.Clear();
                }

                stroke.DashArray = cleaned.Count > 0 ? cleaned.ToArray() : null;
            }

            if (stroke.DashArray == null) stroke.DashOffset = 0;

            // SHAPE-9：Marker 只用于 Line、Polyline。
            bool open = shapeType == UInkShapeType.Line || shapeType == UInkShapeType.Polyline;
            if (!open)
            {
                stroke.StartMarker = 0;
                stroke.EndMarker = 0;
            }
            else
            {
                if (stroke.StartMarker != 0 && stroke.StartMarker != 1) stroke.StartMarker = 0;
                if (stroke.EndMarker != 0 && stroke.EndMarker != 1) stroke.EndMarker = 0;
            }

            if (stroke.LineCap < 0 || (stroke.LineCap > 2 && stroke.LineCap < 128)) stroke.LineCap = 0;
            if (stroke.LineJoin < 0 || (stroke.LineJoin > 2 && stroke.LineJoin < 128)) stroke.LineJoin = 0;
        }

        private static bool NormalizeMedia(UInkMedia media, List<string> warnings)
        {
            // MEDIA-13：缺少 path / mimeType 的 Media 跳过。
            if (string.IsNullOrEmpty(media.Path) || string.IsNullOrEmpty(media.MimeType)) return false;

            if (IsVisualMime(media.MimeType))
            {
                if (!media.Width.HasValue || !IsPositiveFinite(media.Width.Value)) return false;
                if (!media.Height.HasValue || !IsPositiveFinite(media.Height.Value)) return false;
                if (media.Transform != null && (media.Transform.Length != 6 || !AllFinite(media.Transform))) media.Transform = null;
                if (media.Crop != null && (!IsPositiveFinite(media.Crop.Width) || !IsPositiveFinite(media.Crop.Height))) media.Crop = null;
                if (media.Fit < 0 || media.Fit > 3) media.Fit = 0;
                media.Opacity = Clamp01(media.Opacity);
            }
            else
            {
                if (media.Width.HasValue || media.Height.HasValue)
                {
                    if (!IsPositiveFinite(media.Width.GetValueOrDefault()) || !IsPositiveFinite(media.Height.GetValueOrDefault()))
                    {
                        media.Width = null;
                        media.Height = null;
                    }
                }
            }

            if (media.PageCount.HasValue && media.PageCount.Value == 0) media.PageCount = null;
            if (media.PageCount.HasValue && media.PageIndex >= media.PageCount.Value) media.PageIndex = 0;

            media.Volume = Clamp01(media.Volume);
            if (!IsFinite(media.StartTime) || media.StartTime < 0) media.StartTime = 0;
            if (!IsPositiveFinite(media.PlaybackRate)) media.PlaybackRate = 1.0;

            return true;
        }

        /// <summary>SHAPE-11 / SHAPE-14：整理图形对象层，无法组成完整组的部件退化为独立 Shape。</summary>
        private static void NormalizeShapeObjects(List<UInkBlock> blocks, List<string> warnings)
        {
            var ownerIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var claimed = new HashSet<int>();

            int i = 0;
            while (i < blocks.Count)
            {
                var shape = blocks[i] as UInkShape;
                if (shape == null || shape.Object == null || shape.Object.RefId != null) { i++; continue; }

                var obj = shape.Object;

                // SHAPE-11：id 必须是 UUID 且在文件内唯一。
                if (!UInkValues.IsUuid(obj.Id) || ownerIndex.ContainsKey(obj.Id))
                {
                    obj.Id = NewGuid();
                }

                int start = i;
                int end = i + 1;
                while (end < blocks.Count)
                {
                    var next = blocks[end] as UInkShape;
                    if (next == null || next.Object == null || !GuidEquals(next.Object.RefId, obj.Id)) break;
                    end++;
                }

                int partCount = end - start;

                if (partCount <= 1)
                {
                    // 只有一个部件：对象层没有意义，但仍然是合法的 partCount = 1。
                    obj.PartCount = 1;
                }
                else
                {
                    obj.PartCount = (uint)partCount;
                }

                if (obj.Revision == 0) obj.Revision = 1;
                if (obj.Transform != null && (obj.Transform.Length != 6 || !AllFinite(obj.Transform))) obj.Transform = null;

                ownerIndex[obj.Id] = start;
                claimed.Add(start);

                for (int k = start + 1; k < end; k++)
                {
                    var part = (UInkShape)blocks[k];
                    part.Object.PartIndex = (uint)(k - start);
                    claimed.Add(k);
                }

                i = end;
            }

            // 找不到首部件、或不在连续段内的部件：去掉 object，按独立 Shape 显示（SHAPE-14）。
            int detached = 0;
            for (int k = 0; k < blocks.Count; k++)
            {
                var shape = blocks[k] as UInkShape;
                if (shape == null || shape.Object == null) continue;
                if (shape.Object.RefId != null && !claimed.Contains(k))
                {
                    shape.Object = null;
                    detached++;
                }
            }

            if (detached > 0)
                warnings.Add($"{detached} 个图形部件找不到所属对象，已按独立 Shape 写出（SHAPE-14）");
        }

        private static bool HasBox(UInkGeometry geometry)
        {
            return IsFinite(geometry.CenterX) && IsFinite(geometry.CenterY)
                   && IsPositiveFinite(geometry.Width) && IsPositiveFinite(geometry.Height);
        }

        /// <summary>SHAPE-3：圆角必须成对，且分别不超过宽高的一半。</summary>
        private static void NormalizeCornerRadius(UInkGeometry geometry)
        {
            bool hasX = geometry.CornerRadiusX.HasValue, hasY = geometry.CornerRadiusY.HasValue;
            if (!hasX || !hasY)
            {
                geometry.CornerRadiusX = 0;
                geometry.CornerRadiusY = 0;
                return;
            }

            double x = geometry.CornerRadiusX.Value, y = geometry.CornerRadiusY.Value;
            if (!IsFinite(x) || !IsFinite(y) || x <= 0 || y <= 0)
            {
                geometry.CornerRadiusX = 0;
                geometry.CornerRadiusY = 0;
                return;
            }

            double maxX = geometry.Width.Value / 2.0, maxY = geometry.Height.Value / 2.0;
            geometry.CornerRadiusX = Math.Min(x, maxX);
            geometry.CornerRadiusY = Math.Min(y, maxY);
        }

        // ------------------------------------------------------------------
        // 页面身份与排序
        // ------------------------------------------------------------------

        private static void NormalizePageIdentity(List<Entry> entries, List<string> warnings)
        {
            // 每个 Workspace 内：pageGuid 与 pageIndex 一一对应，pageIndex 从 0 连续。
            var byWorkspace = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
            var workspaceOrder = new List<string>();

            foreach (var entry in entries)
            {
                string key = entry.Canvas.WorkspaceGuid ?? "";
                List<Entry> list;
                if (!byWorkspace.TryGetValue(key, out list))
                {
                    list = new List<Entry>();
                    byWorkspace[key] = list;
                    workspaceOrder.Add(key);
                }
                list.Add(entry);
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int repaired = 0, renumbered = 0;

            foreach (var key in workspaceOrder)
            {
                var list = byWorkspace[key];

                // CANVAS-2：同一页在多个 Device、多个图层上的 Canvas 共享 pageGuid，
                // 所以这里按 GUID 字符串分组，一组只判定一次，避免把同一页拆成多页。
                var groups = new List<List<Entry>>();
                var groupMap = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in list)
                {
                    string raw = entry.Canvas.PageGuid;
                    string groupKey = string.IsNullOrEmpty(raw) ? "\u0000null" : raw;

                    List<Entry> group;
                    if (!groupMap.TryGetValue(groupKey, out group))
                    {
                        group = new List<Entry>();
                        groupMap[groupKey] = group;
                        groups.Add(group);
                    }
                    group.Add(entry);
                }

                foreach (var group in groups)
                {
                    string raw = group[0].Canvas.PageGuid;
                    string guid = raw;

                    // CANVAS-1：必须是 UUID，且在文件内唯一（不同 Workspace 也不能撞）。
                    if (!UInkValues.IsUuid(raw) || !used.Add(raw))
                    {
                        guid = NewGuid();
                        used.Add(guid);
                        repaired++;
                    }

                    foreach (var entry in group) entry.Canvas.PageGuid = guid;
                }

                if (repaired > 0)
                    warnings.Add($"{repaired} 个页面的 pageGuid 非法或与其他页面重复，已重新生成（CANVAS-1）");

                var pageOrder = new List<string>();
                var pageIndex = new Dictionary<string, uint>(StringComparer.Ordinal);

                var sorted = new List<Entry>(list);
                sorted.Sort(delegate (Entry a, Entry b)
                {
                    int cmp = a.Canvas.PageIndex.CompareTo(b.Canvas.PageIndex);
                    return cmp != 0 ? cmp : a.Sequence.CompareTo(b.Sequence);
                });

                foreach (var entry in sorted)
                {
                    if (!pageIndex.ContainsKey(entry.Canvas.PageGuid))
                    {
                        pageIndex[entry.Canvas.PageGuid] = (uint)pageOrder.Count;
                        pageOrder.Add(entry.Canvas.PageGuid);
                    }
                }

                foreach (var entry in list)
                {
                    uint index = pageIndex[entry.Canvas.PageGuid];
                    if (entry.Canvas.PageIndex != index) renumbered++;
                    entry.Canvas.PageIndex = index;
                    if (entry.Canvas.PageNumber == 0) entry.Canvas.PageNumber = index + 1;
                }

                // 每个 (Device, pageGuid) 下 layerIndex 从 0 连续（CANVAS-4）。
                var layerSorted = new List<Entry>(list);
                layerSorted.Sort(delegate (Entry a, Entry b)
                {
                    int cmp = a.Canvas.LayerIndex.CompareTo(b.Canvas.LayerIndex);
                    return cmp != 0 ? cmp : a.Sequence.CompareTo(b.Sequence);
                });

                var perPage = new Dictionary<string, uint>(StringComparer.Ordinal);
                foreach (var entry in layerSorted)
                {
                    string layerKey = (entry.Canvas.DeviceGuid ?? "") + "|" + entry.Canvas.PageGuid;
                    uint next;
                    if (!perPage.TryGetValue(layerKey, out next)) next = 0;
                    entry.Canvas.LayerIndex = next;
                    entry.Canvas.LayerNumber = next;
                    perPage[layerKey] = next + 1;
                }
            }

            if (renumbered > 0)
                warnings.Add($"{renumbered} 个 Canvas 的 pageIndex 已重新连续编号（CANVAS-3）");

            // CANVAS-8：页面元数据只保留在第 0 层。
            foreach (var entry in entries)
            {
                if (entry.Canvas.LayerIndex == 0) continue;
                entry.Canvas.Viewport = null;
                entry.Canvas.Background = null;
                entry.Canvas.PageState = 0;
                entry.Canvas.PageKind = 0;
                entry.Canvas.Binding = 0;
                entry.Canvas.SlideId = null;
            }
        }

        private static int CompareEntries(Entry a, Entry b)
        {
            int cmp = a.WorkspaceOrder.CompareTo(b.WorkspaceOrder);
            if (cmp != 0) return cmp;

            cmp = a.Canvas.PageIndex.CompareTo(b.Canvas.PageIndex);
            if (cmp != 0) return cmp;

            cmp = a.DeviceOrder.CompareTo(b.DeviceOrder);
            if (cmp != 0) return cmp;

            cmp = a.Canvas.LayerIndex.CompareTo(b.Canvas.LayerIndex);
            if (cmp != 0) return cmp;

            return a.Sequence.CompareTo(b.Sequence);
        }

        // ------------------------------------------------------------------
        // Header / Extension
        // ------------------------------------------------------------------

        private static UInkHeader BuildHeader(
            UInkDocument document,
            List<Entry> entries,
            List<UInkWorkspace> workspaces,
            List<UInkDevice> devices,
            UInkWriteOptions options)
        {
            var source = document.Header ?? new UInkHeader();

            var header = new UInkHeader
            {
                // HDR-3：另存为新的逻辑文件时生成新 GUID；非法值必须换掉，否则整份文件会被拒绝。
                Guid = UInkValues.IsUuid(source.Guid) ? source.Guid : NewGuid(),
                Version = options.Version,
                DeviceNum = (uint)Math.Max(1, devices.Count),
                WorkspaceNum = (uint)Math.Max(1, workspaces.Count),
                Time = source.Time > 0 ? source.Time : (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries) pages.Add(entry.Canvas.PageGuid);
            header.PageNum = (uint)pages.Count;

            return header;
        }

        private static UInkHeaderExtension BuildExtension(
            UInkDocument document,
            List<UInkWorkspace> workspaces,
            List<UInkDevice> devices)
        {
            var source = document.Extension;

            if (workspaces.Count == 0 && devices.Count == 0
                && (source == null || (string.IsNullOrEmpty(source.Name) && string.IsNullOrEmpty(source.Explanation))))
            {
                return null;
            }

            var extension = new UInkHeaderExtension
            {
                Name = source != null ? source.Name : null,
                Explanation = source != null ? source.Explanation : null
            };

            extension.Devices.AddRange(devices);
            extension.Workspaces.AddRange(workspaces);
            return extension;
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        private static UInkCanvas CloneCanvas(UInkCanvas source)
        {
            return new UInkCanvas
            {
                WorkspaceGuid = source.WorkspaceGuid,
                DeviceGuid = source.DeviceGuid,
                PageGuid = source.PageGuid,
                PageIndex = source.PageIndex,
                PageNumber = source.PageNumber,
                LayerIndex = source.LayerIndex,
                LayerNumber = source.LayerNumber,
                Viewport = source.Viewport,
                Background = source.Background,
                PageState = source.PageState,
                PageKind = source.PageKind,
                Binding = source.Binding,
                SlideId = source.SlideId
            };
        }

        private static string UniqueKey(UInkCanvas canvas)
        {
            return (canvas.WorkspaceGuid ?? "") + "|" + (canvas.DeviceGuid ?? "") + "|" + (canvas.PageGuid ?? "") + "|" + canvas.LayerIndex;
        }

        private static Dictionary<string, int> BuildOrder<T>(List<T> items) where T : class
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Count; i++)
            {
                string guid = null;

                var workspace = items[i] as UInkWorkspace;
                if (workspace != null) guid = workspace.Guid;

                var device = items[i] as UInkDevice;
                if (device != null) guid = device.Guid;

                if (!string.IsNullOrEmpty(guid) && !map.ContainsKey(guid)) map[guid] = i;
            }
            return map;
        }

        private static int IndexOf(Dictionary<string, int> map, string guid)
        {
            int value;
            if (guid != null && map.TryGetValue(guid, out value)) return value;
            return int.MaxValue;
        }

        private static bool GuidEquals(string a, string b)
        {
            return string.Equals(a, b, OIC);
        }

        private static string NewGuid()
        {
            return Guid.NewGuid().ToString("D");
        }

        private static bool IsFinite(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
        }

        private static bool IsPositiveFinite(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) && value.Value > 0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsPositiveFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        private static bool AllFinite(double[] values)
        {
            foreach (double value in values) if (!IsFinite(value)) return false;
            return true;
        }

        private static bool SamePoint(UInkGeomPoint a, UInkGeomPoint b)
        {
            return Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value)) return 0;
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }

        private static bool IsVisualMime(string mime)
        {
            if (string.IsNullOrEmpty(mime)) return false;
            string lower = mime.ToLowerInvariant();
            return lower.StartsWith("image/", StringComparison.Ordinal)
                   || lower.StartsWith("video/", StringComparison.Ordinal)
                   || lower == "application/pdf";
        }
    }

    /// <summary>规范化后的输出快照。</summary>
    public sealed class UInkNormalizedDocument
    {
        public UInkHeader Header { get; set; }
        public UInkHeaderExtension Extension { get; set; }
        public List<UInkLayer> Layers { get; } = new List<UInkLayer>();
        public List<string> Warnings { get; } = new List<string>();
    }
}
