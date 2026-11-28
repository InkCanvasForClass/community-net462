using System;
using System.Collections.Generic;
using System.IO;
using InkUInk.Model;
using InkUInk.MsgPack;

namespace InkUInk
{
    /// <summary>导出目标等级（兼容导出时用来选择丢掉哪些高等级能力）。</summary>
    public enum UInkTargetLevel
    {
        Core = 0,
        Basic = 1,
        Standard = 2,
        Full = 3,
        Inkeys = 4
    }

    /// <summary>写入选项。默认按「完整级内容原样写出」，不会静默丢弃内容。</summary>
    public sealed class UInkWriteOptions
    {
        /// <summary>只写最后一个 Clear 之后的内容（CLEAR-7 的推荐回退，导出给不支持 Clear 的软件时用）。</summary>
        public bool DropContentBeforeLastClear { get; set; }

        /// <summary>跳过 Media 块（导出给核心级 / 基础级时可用）。</summary>
        public bool SkipMedia { get; set; }

        /// <summary>完整保存是否允许替换目标文件。导出副本时可禁止覆盖。</summary>
        public bool Overwrite { get; set; } = true;

        /// <summary>
        /// 目标等级。低于 Full 时合并图层、只保留首个 Device；低于 Standard 时丢弃 Media；
        /// 低于 Inkeys 时丢弃 Clear 及其之前的内容（COMPAT-5）。
        /// </summary>
        public UInkTargetLevel TargetLevel { get; set; } = UInkTargetLevel.Inkeys;

        /// <summary>写入 version（VER-2：1.1 写入器必须写 11）。</summary>
        public ushort Version { get; set; } = 11;

        /// <summary>规范化过程中的提示，用于诊断被修正或丢弃的内容。</summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// UInk 1.1 写入器。只做完整保存（紧凑文件）：
    /// 每个 Canvas 唯一键只出现一次、contentId / undoId 重新编号、Header 统计重新计算（SAVE-2）。
    /// 写出前的字段校正全部由 <see cref="UInkNormalizer"/> 完成，本类只负责编码。
    /// </summary>
    public static class UInkWriter
    {
        public static void WriteFile(UInkDocument document, string path, UInkWriteOptions options = null)
        {
            // SAVE-1：先写临时文件，再整体替换。
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            string temp = Path.Combine(directory, Path.GetFileName(fullPath) + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    Write(document, stream, options);
                    stream.Flush(true);
                }

                if (options?.Overwrite != false && File.Exists(fullPath)) File.Replace(temp, fullPath, null);
                else File.Move(temp, fullPath);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        public static byte[] WriteToBytes(UInkDocument document, UInkWriteOptions options = null)
        {
            using (var stream = new MemoryStream())
            {
                Write(document, stream, options);
                return stream.ToArray();
            }
        }

        public static void Write(UInkDocument document, Stream stream, UInkWriteOptions options = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            options = options ?? new UInkWriteOptions();
            if (options.Version < 10 || options.Version > 19) options.Version = 11;

            var normalized = UInkNormalizer.Build(document, options);

            options.Warnings.Clear();
            options.Warnings.AddRange(normalized.Warnings);

            var writer = new MsgPackWriter(stream);

            WriteHeader(writer, normalized.Header);
            if (normalized.Extension != null) WriteExtension(writer, normalized.Extension);

            foreach (var layer in normalized.Layers)
            {
                WriteCanvas(writer, layer.Canvas);
                WriteBlocks(writer, layer.Blocks);
            }
        }

        // ------------------------------------------------------------------
        // Header / Extension
        // ------------------------------------------------------------------

        private static void WriteHeader(MsgPackWriter writer, UInkHeader header)
        {
            // HDR-1：固定 array(7)，字段顺序、位宽与 str8 编码都不得改变。
            writer.WriteArrayHeader(7);
            writer.WriteUInt16(UInkTypeId.Header);
            writer.WriteUInt16(header.Version);
            writer.WriteString(header.Guid);
            writer.WriteUInt32(header.DeviceNum);
            writer.WriteUInt32(header.WorkspaceNum);
            writer.WriteUInt32(header.PageNum);
            writer.WriteUInt64(header.Time);
        }

        private static void WriteExtension(MsgPackWriter writer, UInkHeaderExtension ext)
        {
            var keys = new List<string> { "type" };
            if (!string.IsNullOrEmpty(ext.Name)) keys.Add("name");
            if (!string.IsNullOrEmpty(ext.Explanation)) keys.Add("explanation");
            if (ext.Devices.Count > 0) keys.Add("devices");
            if (ext.Workspaces.Count > 0) keys.Add("workspaces");

            writer.WriteMapHeader(keys.Count);
            foreach (var key in keys)
            {
                writer.WriteKey(key);
                switch (key)
                {
                    case "type": writer.WriteUInt16(UInkTypeId.HeaderExtension); break;
                    case "name": writer.WriteString(ext.Name); break;
                    case "explanation": writer.WriteString(ext.Explanation); break;
                    case "devices":
                    {
                        writer.WriteArrayHeader(ext.Devices.Count);
                        foreach (var device in ext.Devices) WriteDevice(writer, device);
                        break;
                    }
                    case "workspaces":
                    {
                        writer.WriteArrayHeader(ext.Workspaces.Count);
                        foreach (var workspace in ext.Workspaces) WriteWorkspace(writer, workspace);
                        break;
                    }
                }
            }
        }

        private static void WriteDevice(MsgPackWriter writer, UInkDevice device)
        {
            bool isWindow = device.DeviceType == 1;
            var keys = new List<string> { "guid", "deviceType" };
            if (!string.IsNullOrEmpty(device.Name)) keys.Add("name");
            if (isWindow)
            {
                keys.Add("parentDeviceGuid");
                keys.Add("x"); keys.Add("y"); keys.Add("width"); keys.Add("height"); keys.Add("zIndex");
            }
            else
            {
                keys.Add("x"); keys.Add("y"); keys.Add("width"); keys.Add("height");
            }

            writer.WriteMapHeader(keys.Count);
            foreach (var key in keys)
            {
                writer.WriteKey(key);
                switch (key)
                {
                    case "guid": writer.WriteString(device.Guid); break;
                    case "deviceType": writer.WriteInt32(device.DeviceType); break;
                    case "name": writer.WriteString(device.Name); break;
                    case "parentDeviceGuid": writer.WriteString(device.ParentDeviceGuid); break;
                    case "x": if (isWindow) writer.WriteFloat32((float)device.WindowX); else writer.WriteInt32(device.X); break;
                    case "y": if (isWindow) writer.WriteFloat32((float)device.WindowY); else writer.WriteInt32(device.Y); break;
                    case "width": if (isWindow) writer.WriteFloat32((float)device.WindowWidth); else writer.WriteUInt32(device.Width); break;
                    case "height": if (isWindow) writer.WriteFloat32((float)device.WindowHeight); else writer.WriteUInt32(device.Height); break;
                    case "zIndex": writer.WriteUInt32(device.ZIndex); break;
                }
            }
        }

        private static void WriteWorkspace(MsgPackWriter writer, UInkWorkspace workspace)
        {
            var keys = new List<string> { "guid", "workspaceType" };
            if (!string.IsNullOrEmpty(workspace.Name)) keys.Add("name");
            if (!string.IsNullOrEmpty(workspace.ParentWorkspaceGuid)) keys.Add("parentWorkspaceGuid");
            if (!string.IsNullOrEmpty(workspace.HostId)) keys.Add("hostId");
            if (workspace.CurrentPageIndex > 0) keys.Add("currentPageIndex");

            writer.WriteMapHeader(keys.Count);
            foreach (var key in keys)
            {
                writer.WriteKey(key);
                switch (key)
                {
                    case "guid": writer.WriteString(workspace.Guid); break;
                    case "workspaceType": writer.WriteInt32(workspace.WorkspaceType); break;
                    case "name": writer.WriteString(workspace.Name); break;
                    case "parentWorkspaceGuid": writer.WriteString(workspace.ParentWorkspaceGuid); break;
                    case "hostId": writer.WriteString(workspace.HostId); break;
                    case "currentPageIndex": writer.WriteUInt32(workspace.CurrentPageIndex); break;
                }
            }
        }

        // ------------------------------------------------------------------
        // Canvas
        // ------------------------------------------------------------------

        private static void WriteCanvas(MsgPackWriter writer, UInkCanvas canvas)
        {
            // CANVAS-8：页面元数据只写在第 0 层（Normalizer 已清理，这里再防御一次）。
            bool includeMetadata = canvas.LayerIndex == 0;

            var keys = new List<string> { "type" };
            if (!string.IsNullOrEmpty(canvas.WorkspaceGuid)) keys.Add("workspaceGuid");
            if (!string.IsNullOrEmpty(canvas.DeviceGuid)) keys.Add("deviceGuid");
            keys.Add("pageGuid");
            keys.Add("pageIndex");
            keys.Add("pageNumber");
            keys.Add("layerIndex");
            keys.Add("layerNumber");

            if (includeMetadata && canvas.Viewport != null) keys.Add("viewport");
            if (includeMetadata && canvas.Background != null) keys.Add("background");
            if (includeMetadata && canvas.PageState != 0) keys.Add("pageState");
            if (includeMetadata && canvas.PageKind != 0) keys.Add("pageKind");
            if (includeMetadata && canvas.Binding != 0) keys.Add("binding");
            if (includeMetadata && canvas.SlideId.HasValue) keys.Add("slideId");

            writer.WriteMapHeader(keys.Count);
            foreach (var key in keys)
            {
                writer.WriteKey(key);
                switch (key)
                {
                    case "type": writer.WriteUInt16(UInkTypeId.Canvas); break;
                    case "workspaceGuid": writer.WriteString(canvas.WorkspaceGuid); break;
                    case "deviceGuid": writer.WriteString(canvas.DeviceGuid); break;
                    case "pageGuid": writer.WriteString(canvas.PageGuid); break;
                    case "pageIndex": writer.WriteUInt32(canvas.PageIndex); break;
                    case "pageNumber": writer.WriteUInt32(canvas.PageNumber); break;
                    case "layerIndex": writer.WriteUInt32(canvas.LayerIndex); break;
                    case "layerNumber": writer.WriteUInt32(canvas.LayerNumber); break;
                    case "viewport":
                        // CANVAS-22：scale 不是有限正数时整个 viewport 无效，改为不写。
                        if (!IsValidViewport(canvas.Viewport))
                        {
                            WriteViewportPlaceholder(writer);
                            break;
                        }
                        writer.WriteMapHeader(3);
                        writer.WriteKey("x"); writer.WriteFloat32((float)canvas.Viewport.X);
                        writer.WriteKey("y"); writer.WriteFloat32((float)canvas.Viewport.Y);
                        writer.WriteKey("scale"); writer.WriteFloat32((float)canvas.Viewport.Scale);
                        break;
                    case "background":
                    {
                        writer.WriteMapHeader(2);
                        writer.WriteKey("color"); WriteColor(writer, canvas.Background.Color);
                        writer.WriteKey("pattern"); writer.WriteInt32(canvas.Background.Pattern);
                        break;
                    }
                    case "pageState": writer.WriteInt32(canvas.PageState); break;
                    case "pageKind": writer.WriteInt32(canvas.PageKind); break;
                    case "binding": writer.WriteInt32(canvas.Binding); break;
                    case "slideId": writer.WriteInt32(canvas.SlideId.Value); break;
                }
            }
        }

        /// <summary>viewport 无效时写默认值，等价于不写（CANVAS-22 的回退结果）。</summary>
        private static void WriteViewportPlaceholder(MsgPackWriter writer)
        {
            writer.WriteMapHeader(3);
            writer.WriteKey("x"); writer.WriteFloat32(0f);
            writer.WriteKey("y"); writer.WriteFloat32(0f);
            writer.WriteKey("scale"); writer.WriteFloat32(1f);
        }

        private static bool IsValidViewport(UInkViewport viewport)
        {
            if (viewport == null) return false;
            if (double.IsNaN(viewport.X) || double.IsInfinity(viewport.X)) return false;
            if (double.IsNaN(viewport.Y) || double.IsInfinity(viewport.Y)) return false;
            if (double.IsNaN(viewport.Scale) || double.IsInfinity(viewport.Scale) || viewport.Scale <= 0) return false;
            return true;
        }

        // ------------------------------------------------------------------
        // 内容块
        // ------------------------------------------------------------------

        private static void WriteBlocks(MsgPackWriter writer, List<UInkBlock> blocks)
        {
            foreach (var block in blocks)
            {
                if (block is UInkInk) WriteInk(writer, (UInkInk)block);
                else if (block is UInkShape) WriteShape(writer, (UInkShape)block);
                else if (block is UInkMedia) WriteMedia(writer, (UInkMedia)block);
                else if (block is UInkClear) WriteClear(writer, (UInkClear)block);
            }
        }

        private static void WriteInk(MsgPackWriter writer, UInkInk ink)
        {
            bool hasNib = ink.Nib != null;
            bool hasCap = ink.Cap.HasValue && !(hasNib && ink.Nib.IsRectangle);   // NIB-3

            var keys = new List<string>
            {
                "type", "contentId", "undoId", "inkType", "color", "opacity", "texture", "points"
            };
            if (hasNib) keys.Add("nib");
            if (hasCap) keys.Add("cap");
            if (ink.RenderOnlyWhenLatest) keys.Add("renderOnlyWhenLatest");

            writer.WriteMapHeader(keys.Count);
            foreach (var key in keys)
            {
                writer.WriteKey(key);
                switch (key)
                {
                    case "type": writer.WriteUInt16(UInkTypeId.Ink); break;
                    case "contentId": writer.WriteUInt32(ink.ContentId); break;
                    case "undoId": writer.WriteUInt32(ink.UndoId); break;
                    case "inkType": writer.WriteInt32(ink.InkType); break;
                    case "color": WriteColor(writer, ink.Color); break;
                    case "opacity": writer.WriteFloat32((float)ink.Opacity); break;
                    case "texture": writer.WriteInt32(ink.Texture); break;
                    case "points": WritePoints(writer, ink.Points); break;
                    case "nib":
                        writer.WriteMapHeader(3);
                        writer.WriteKey("shape"); writer.WriteInt32(ink.Nib.Shape);
                        writer.WriteKey("aspect"); writer.WriteFloat32((float)ink.Nib.Aspect);
                        writer.WriteKey("orientation"); writer.WriteInt32(ink.Nib.Orientation);
                        break;
                    case "cap": writer.WriteInt32(ink.Cap.Value); break;
                    case "renderOnlyWhenLatest": writer.WriteBool(true); break;
                }
            }
        }

        private static void WritePoints(MsgPackWriter writer, List<UInkPoint> points)
        {
            writer.WriteArrayHeader(points.Count);

            double previousX = 0, previousY = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                double x, y;
                if (i == 0) { x = point.X; y = point.Y; }
                else { x = point.X - previousX; y = point.Y - previousY; }

                previousX = point.X;
                previousY = point.Y;

                // INK-5：width 必须大于 0，写 0 会让整条笔迹被跳过。
                float width = (float)point.Width;
                if (!(width > 0f) || float.IsInfinity(width)) width = 1f;

                bool hasStyle = point.Color != null && point.Opacity.HasValue;
                writer.WriteMapHeader(hasStyle ? 5 : 3);
                writer.WriteKey("x"); writer.WriteFloat32((float)x);
                writer.WriteKey("y"); writer.WriteFloat32((float)y);
                writer.WriteKey("width"); writer.WriteFloat32(width);

                if (hasStyle)
                {
                    writer.WriteKey("color"); WriteColor(writer, point.Color);
                    writer.WriteKey("opacity"); writer.WriteFloat32((float)point.Opacity.Value);
                }
            }
        }

        private static void WriteShape(MsgPackWriter writer, UInkShape shape)
        {
            var keys = new List<string> { "type", "contentId", "undoId", "shapeType", "geometry" };
            if (shape.Stroke != null) keys.Add("stroke");
            if (shape.Fill != null) keys.Add("fill");
            if (shape.Object != null) keys.Add("object");
            if (shape.RenderOnlyWhenLatest) keys.Add("renderOnlyWhenLatest");

            writer.WriteMapHeader(keys.Count);
            foreach (var key in keys)
            {
                writer.WriteKey(key);
                switch (key)
                {
                    case "type": writer.WriteUInt16(UInkTypeId.Shape); break;
                    case "contentId": writer.WriteUInt32(shape.ContentId); break;
                    case "undoId": writer.WriteUInt32(shape.UndoId); break;
                    case "shapeType": writer.WriteInt32(shape.ShapeType); break;
                    case "geometry": WriteGeometry(writer, shape.ShapeType, shape.Geometry); break;
                    case "stroke": WriteStroke(writer, shape.Stroke); break;
                    case "fill":
                        writer.WriteMapHeader(3);
                        writer.WriteKey("fillType"); writer.WriteInt32(shape.Fill.FillType);
                        writer.WriteKey("color"); WriteColor(writer, shape.Fill.Color);
                        writer.WriteKey("opacity"); writer.WriteFloat32((float)shape.Fill.Opacity);
                        break;
                    case "object": WriteShapeObject(writer, shape.Object); break;
                    case "renderOnlyWhenLatest": writer.WriteBool(true); break;
                }
            }
        }

        private static void WriteGeometry(MsgPackWriter writer, int shapeType, UInkGeometry geometry)
        {
            switch (shapeType)
            {
                case UInkShapeType.Line:
                case UInkShapeType.Polyline:
                case UInkShapeType.Polygon:
                    writer.WriteMapHeader(1);
                    writer.WriteKey("points");
                    writer.WriteArrayHeader(geometry.Points.Count);
                    foreach (var point in geometry.Points)
                    {
                        writer.WriteMapHeader(2);
                        writer.WriteKey("x"); writer.WriteFloat32((float)point.X);
                        writer.WriteKey("y"); writer.WriteFloat32((float)point.Y);
                    }
                    break;

                case UInkShapeType.Rectangle:
                {
                    bool rounded = geometry.CornerRadiusX.HasValue && geometry.CornerRadiusY.HasValue
                                   && (geometry.CornerRadiusX.Value > 0 || geometry.CornerRadiusY.Value > 0);
                    bool rotated = geometry.Rotation != 0;

                    writer.WriteMapHeader(4 + (rounded ? 2 : 0) + (rotated ? 1 : 0));
                    writer.WriteKey("centerX"); writer.WriteFloat32((float)geometry.CenterX.Value);
                    writer.WriteKey("centerY"); writer.WriteFloat32((float)geometry.CenterY.Value);
                    writer.WriteKey("width"); writer.WriteFloat32((float)geometry.Width.Value);
                    writer.WriteKey("height"); writer.WriteFloat32((float)geometry.Height.Value);
                    if (rotated)
                    {
                        writer.WriteKey("rotation"); writer.WriteFloat32((float)geometry.Rotation);
                    }
                    if (rounded)
                    {
                        writer.WriteKey("cornerRadiusX"); writer.WriteFloat32((float)geometry.CornerRadiusX.Value);
                        writer.WriteKey("cornerRadiusY"); writer.WriteFloat32((float)geometry.CornerRadiusY.Value);
                    }
                    break;
                }

                case UInkShapeType.Square:
                {
                    bool rotated = geometry.Rotation != 0;
                    writer.WriteMapHeader(rotated ? 4 : 3);
                    writer.WriteKey("centerX"); writer.WriteFloat32((float)geometry.CenterX.Value);
                    writer.WriteKey("centerY"); writer.WriteFloat32((float)geometry.CenterY.Value);
                    writer.WriteKey("size"); writer.WriteFloat32((float)geometry.Size.Value);
                    if (rotated)
                    {
                        writer.WriteKey("rotation"); writer.WriteFloat32((float)geometry.Rotation);
                    }
                    break;
                }

                case UInkShapeType.Ellipse:
                {
                    bool rotated = geometry.Rotation != 0;
                    writer.WriteMapHeader(rotated ? 5 : 4);
                    writer.WriteKey("centerX"); writer.WriteFloat32((float)geometry.CenterX.Value);
                    writer.WriteKey("centerY"); writer.WriteFloat32((float)geometry.CenterY.Value);
                    writer.WriteKey("width"); writer.WriteFloat32((float)geometry.Width.Value);
                    writer.WriteKey("height"); writer.WriteFloat32((float)geometry.Height.Value);
                    if (rotated)
                    {
                        writer.WriteKey("rotation"); writer.WriteFloat32((float)geometry.Rotation);
                    }
                    break;
                }

                case UInkShapeType.Circle:
                    writer.WriteMapHeader(3);
                    writer.WriteKey("centerX"); writer.WriteFloat32((float)geometry.CenterX.Value);
                    writer.WriteKey("centerY"); writer.WriteFloat32((float)geometry.CenterY.Value);
                    writer.WriteKey("radius"); writer.WriteFloat32((float)geometry.Radius.Value);
                    break;
            }
        }

        private static void WriteStroke(MsgPackWriter writer, UInkStrokeStyle stroke)
        {
            bool hasDash = stroke.DashArray != null && stroke.DashArray.Length > 0;
            var keys = new List<string> { "color", "opacity", "width" };
            if (hasDash) { keys.Add("dashArray"); if (stroke.DashOffset != 0) keys.Add("dashOffset"); }
            if (stroke.LineCap != 0) keys.Add("lineCap");
            if (stroke.LineJoin != 0) keys.Add("lineJoin");
            if (stroke.StartMarker != 0) keys.Add("startMarker");
            if (stroke.EndMarker != 0) keys.Add("endMarker");

            writer.WriteMapHeader(keys.Count);
            foreach (var key in keys)
            {
                writer.WriteKey(key);
                switch (key)
                {
                    case "color": WriteColor(writer, stroke.Color); break;
                    case "opacity": writer.WriteFloat32((float)stroke.Opacity); break;
                    case "width": writer.WriteFloat32((float)stroke.Width); break;
                    case "dashArray":
                        writer.WriteArrayHeader(stroke.DashArray.Length);
                        foreach (double value in stroke.DashArray) writer.WriteFloat32((float)value);
                        break;
                    case "dashOffset": writer.WriteFloat32((float)stroke.DashOffset); break;
                    case "lineCap": writer.WriteInt32(stroke.LineCap); break;
                    case "lineJoin": writer.WriteInt32(stroke.LineJoin); break;
                    case "startMarker": writer.WriteInt32(stroke.StartMarker); break;
                    case "endMarker": writer.WriteInt32(stroke.EndMarker); break;
                }
            }
        }

        private static void WriteShapeObject(MsgPackWriter writer, UInkShapeObject obj)
        {
            bool isFirstPart = obj.RefId == null;

            if (!isFirstPart)
            {
                writer.WriteMapHeader(2);
                writer.WriteKey("ref"); writer.WriteString(obj.RefId);
                writer.WriteKey("partIndex"); writer.WriteUInt32(obj.PartIndex ?? 0);
                return;
            }

            var keys = new List<string> { "id", "revision", "partCount" };
            if (!string.IsNullOrEmpty(obj.Preset)) keys.Add("preset");
            if (obj.Transform != null && obj.Transform.Length == 6) keys.Add("transform");

            writer.WriteMapHeader(keys.Count);
            foreach (var key in keys)
            {
                writer.WriteKey(key);
                switch (key)
                {
                    case "id": writer.WriteString(obj.Id); break;
                    case "revision": writer.WriteUInt64(obj.Revision > 0 ? obj.Revision : 1); break;
                    case "partCount": writer.WriteUInt32(obj.PartCount); break;
                    case "preset": writer.WriteString(obj.Preset); break;
                    case "transform":
                        writer.WriteArrayHeader(6);
                        foreach (double value in obj.Transform) writer.WriteFloat32((float)value);
                        break;
                }
            }
        }

        private static void WriteMedia(MsgPackWriter writer, UInkMedia media)
        {
            bool visual = media.Width.HasValue && media.Height.HasValue;
            var keys = new List<string> { "type", "contentId", "undoId", "path", "mimeType" };
            if (visual) { keys.Add("width"); keys.Add("height"); }
            if (media.Transform != null && media.Transform.Length == 6) keys.Add("transform");
            if (media.Opacity != 1.0) keys.Add("opacity");
            if (media.Fit != 0) keys.Add("fit");
            if (media.Crop != null) keys.Add("crop");
            if (media.PageCount.HasValue) keys.Add("pageCount");
            if (media.PageIndex != 0) keys.Add("pageIndex");
            if (media.Autoplay) keys.Add("autoplay");
            if (media.Loop) keys.Add("loop");
            if (media.Volume != 1.0) keys.Add("volume");
            if (media.StartTime != 0) keys.Add("startTime");
            if (media.PlaybackRate != 1.0) keys.Add("playbackRate");

            writer.WriteMapHeader(keys.Count);
            foreach (var key in keys)
            {
                writer.WriteKey(key);
                switch (key)
                {
                    case "type": writer.WriteUInt16(UInkTypeId.Media); break;
                    case "contentId": writer.WriteUInt32(media.ContentId); break;
                    case "undoId": writer.WriteUInt32(media.UndoId); break;
                    case "path": writer.WriteString(media.Path); break;
                    case "mimeType": writer.WriteString(media.MimeType); break;
                    case "width": writer.WriteFloat32((float)media.Width.Value); break;
                    case "height": writer.WriteFloat32((float)media.Height.Value); break;
                    case "transform":
                        writer.WriteArrayHeader(6);
                        foreach (double value in media.Transform) writer.WriteFloat32((float)value);
                        break;
                    case "opacity": writer.WriteFloat32((float)media.Opacity); break;
                    case "fit": writer.WriteInt32(media.Fit); break;
                    case "crop":
                        writer.WriteMapHeader(4);
                        writer.WriteKey("x"); writer.WriteFloat32((float)media.Crop.X);
                        writer.WriteKey("y"); writer.WriteFloat32((float)media.Crop.Y);
                        writer.WriteKey("width"); writer.WriteFloat32((float)media.Crop.Width);
                        writer.WriteKey("height"); writer.WriteFloat32((float)media.Crop.Height);
                        break;
                    case "pageCount": writer.WriteUInt32(media.PageCount.Value); break;
                    case "pageIndex": writer.WriteUInt32(media.PageIndex); break;
                    case "autoplay": writer.WriteBool(true); break;
                    case "loop": writer.WriteBool(true); break;
                    case "volume": writer.WriteFloat32((float)media.Volume); break;
                    case "startTime": writer.WriteFloat64(media.StartTime); break;
                    case "playbackRate": writer.WriteFloat32((float)media.PlaybackRate); break;
                }
            }
        }

        private static void WriteClear(MsgPackWriter writer, UInkClear clear)
        {
            writer.WriteMapHeader(3);
            writer.WriteKey("type"); writer.WriteUInt16(UInkTypeId.Clear);
            writer.WriteKey("contentId"); writer.WriteUInt32(clear.ContentId);
            writer.WriteKey("undoId"); writer.WriteUInt32(clear.UndoId);
        }

        private static void WriteColor(MsgPackWriter writer, UInkColor color)
        {
            if (color == null)
            {
                writer.WriteMapHeader(1);
                writer.WriteKey("fallback"); writer.WriteUInt32(0);
                return;
            }

            bool extended = color.HasExtendedColor
                            && !string.IsNullOrEmpty(color.Space)
                            && (color.Space == "srgb" || color.Space == "scrgb");

            writer.WriteMapHeader(extended ? 3 : 1);
            // COLOR-1：fallback 不得超过 0xFFFFFF。
            writer.WriteKey("fallback"); writer.WriteUInt32(color.Fallback & 0x00FFFFFFu);

            if (extended)
            {
                writer.WriteKey("space"); writer.WriteString(color.Space);
                writer.WriteKey("components");
                writer.WriteArrayHeader(3);
                foreach (double value in color.Components)
                {
                    float component = (float)value;
                    if (float.IsNaN(component) || float.IsInfinity(component)) component = 0f;
                    writer.WriteFloat32(component);
                }
            }
        }
    }
}
