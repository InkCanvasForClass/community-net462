using System;
using System.Collections.Generic;

namespace InkUInk.Model
{
    /// <summary>UInk 1.1 的块 Type ID（规范「块类型与编码」）。</summary>
    public static class UInkTypeId
    {
        public const int Header = 0;
        public const int HeaderExtension = 1;
        public const int Canvas = 2;
        public const int Ink = 3;
        public const int Media = 4;
        public const int Shape = 5;
        public const int Clear = 6;
    }

    public static class UInkInkType
    {
        public const int Erase = 0;
        public const int Pen = 1;
        public const int Highlighter = 2;
        public const int AdvancedHighlighter = 3;
    }

    public static class UInkShapeType
    {
        public const int Line = 0;
        public const int Polyline = 1;
        public const int Rectangle = 2;
        public const int Square = 3;
        public const int Ellipse = 4;
        public const int Circle = 5;
        public const int Polygon = 6;
    }

    public static class UInkWorkspaceType
    {
        public const int ScreenAnnotation = 0;
        public const int Whiteboard = 1;
        public const int Presentation = 2;
    }

    /// <summary>Color Map：sRGB 回退色 + 可选 HDR / 精确色彩。</summary>
    public sealed class UInkColor
    {
        public uint Fallback { get; set; }
        public string Space { get; set; }
        public double[] Components { get; set; }

        public UInkColor() { }

        public UInkColor(uint fallback)
        {
            Fallback = fallback;
        }

        public bool HasExtendedColor
        {
            get { return Space != null && Components != null && Components.Length == 3; }
        }

        /// <summary>COLOR-2：按等级选择颜色，这里只返回 SDR 回退色。</summary>
        public uint EffectiveFallback
        {
            get { return Fallback & 0x00FFFFFFu; }
        }
    }

    /// <summary>笔尖描述（NIB-1 … NIB-8）。</summary>
    public sealed class UInkNib
    {
        internal UInkNib Copy() => (UInkNib)MemberwiseClone();

        public int Shape { get; set; }          // 0 Circle, 1 Rectangle
        public double Aspect { get; set; }      // 矩形笔尖宽 ÷ 高
        public int Orientation { get; set; }    // 0 Fixed, 1 FollowDirection

        public bool IsRectangle
        {
            get { return Shape == 1 && Aspect > 0 && !double.IsNaN(Aspect) && !double.IsInfinity(Aspect); }
        }
    }

    /// <summary>
    /// 轨迹点。注意：<see cref="UInkInk.Points"/> 中保存的是 Canvas 绝对坐标；
    /// 文件里的「首点绝对、后续点相对位移」由 UInkWriter / UInkReader 负责差分与累加（INK-4）。
    /// </summary>
    public sealed class UInkPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public UInkColor Color { get; set; }
        public double? Opacity { get; set; }
    }

    public abstract class UInkBlock
    {
        internal virtual UInkBlock CopyForNormalization() => (UInkBlock)MemberwiseClone();

        public int TypeId { get; protected set; }
        public uint ContentId { get; set; }
        public uint UndoId { get; set; }
    }

    public sealed class UInkInk : UInkBlock
    {
        public UInkInk() { TypeId = UInkTypeId.Ink; }

        public int InkType { get; set; } = UInkInkType.Pen;
        public UInkColor Color { get; set; }
        public double Opacity { get; set; } = 1.0;
        public int Texture { get; set; }
        public List<UInkPoint> Points { get; private set; } = new List<UInkPoint>();
        public UInkNib Nib { get; set; }
        public int? Cap { get; set; }
        public bool RenderOnlyWhenLatest { get; set; }

        internal override UInkBlock CopyForNormalization()
        {
            var copy = (UInkInk)base.CopyForNormalization();
            copy.Points = GetAbsolutePoints();
            copy.Nib = Nib?.Copy();
            return copy;
        }

        /// <summary>
        /// 返回绝对坐标副本。<see cref="Points"/> 本身已经是绝对坐标（读取时已按 INK-4 累加），
        /// 这里只做复制，避免调用方修改集合影响原对象。
        /// </summary>
        public List<UInkPoint> GetAbsolutePoints()
        {
            var result = new List<UInkPoint>(Points.Count);
            foreach (var p in Points)
            {
                result.Add(new UInkPoint { X = p.X, Y = p.Y, Width = p.Width, Color = p.Color, Opacity = p.Opacity });
            }
            return result;
        }
    }

    /// <summary>Shape 的几何：按 shapeType 决定使用哪些字段。</summary>
    public sealed class UInkGeometry
    {
        public List<UInkGeomPoint> Points { get; private set; } = new List<UInkGeomPoint>();
        public double? CenterX { get; set; }
        public double? CenterY { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public double? Size { get; set; }
        public double? Radius { get; set; }
        public double Rotation { get; set; }
        public double? CornerRadiusX { get; set; }
        public double? CornerRadiusY { get; set; }

        internal UInkGeometry Copy()
        {
            var copy = (UInkGeometry)MemberwiseClone();
            copy.Points = new List<UInkGeomPoint>(Points);
            return copy;
        }
    }

    public sealed class UInkGeomPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class UInkStrokeStyle
    {
        internal UInkStrokeStyle Copy() => (UInkStrokeStyle)MemberwiseClone();

        public UInkColor Color { get; set; }
        public double Opacity { get; set; } = 1.0;
        public double Width { get; set; } = 1.0;
        public double[] DashArray { get; set; }
        public double DashOffset { get; set; }
        public int LineCap { get; set; }
        public int LineJoin { get; set; }
        public int StartMarker { get; set; }
        public int EndMarker { get; set; }
    }

    public sealed class UInkFill
    {
        internal UInkFill Copy() => (UInkFill)MemberwiseClone();

        public int FillType { get; set; }
        public UInkColor Color { get; set; }
        public double Opacity { get; set; } = 1.0;
    }

    /// <summary>图形对象层（SHAPE-11 … SHAPE-15）。</summary>
    public sealed class UInkShapeObject
    {
        internal UInkShapeObject Copy() => (UInkShapeObject)MemberwiseClone();

        public string Id { get; set; }
        public ulong Revision { get; set; }
        public string Preset { get; set; }
        public uint PartCount { get; set; }
        public double[] Transform { get; set; }
        public string RefId { get; set; }
        public uint? PartIndex { get; set; }
    }

    public sealed class UInkShape : UInkBlock
    {
        public UInkShape() { TypeId = UInkTypeId.Shape; }

        public int ShapeType { get; set; }
        public UInkGeometry Geometry { get; set; }
        public UInkStrokeStyle Stroke { get; set; }
        public UInkFill Fill { get; set; }
        public UInkShapeObject Object { get; set; }
        public bool RenderOnlyWhenLatest { get; set; }

        internal override UInkBlock CopyForNormalization()
        {
            var copy = (UInkShape)base.CopyForNormalization();
            copy.Geometry = Geometry?.Copy();
            copy.Stroke = Stroke?.Copy();
            copy.Fill = Fill?.Copy();
            copy.Object = Object?.Copy();
            return copy;
        }
    }

    public sealed class UInkCrop
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public sealed class UInkMedia : UInkBlock
    {
        public UInkMedia() { TypeId = UInkTypeId.Media; }

        public string Path { get; set; }
        public string MimeType { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public double[] Transform { get; set; }
        public double Opacity { get; set; } = 1.0;
        public int Fit { get; set; }
        public UInkCrop Crop { get; set; }
        public uint? PageCount { get; set; }
        public uint PageIndex { get; set; }
        public bool Autoplay { get; set; }
        public bool Loop { get; set; }
        public double Volume { get; set; } = 1.0;
        public double StartTime { get; set; }
        public double PlaybackRate { get; set; } = 1.0;
    }

    public sealed class UInkClear : UInkBlock
    {
        public UInkClear() { TypeId = UInkTypeId.Clear; }
    }

    public sealed class UInkViewport
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Scale { get; set; } = 1.0;

        public static UInkViewport Default { get { return new UInkViewport(); } }
    }

    public sealed class UInkBackground
    {
        public UInkColor Color { get; set; }
        public int Pattern { get; set; }
    }

    public sealed class UInkCanvas
    {
        public string WorkspaceGuid { get; set; }
        public string DeviceGuid { get; set; }
        public string PageGuid { get; set; }
        public uint PageIndex { get; set; }
        public uint PageNumber { get; set; }
        public uint LayerIndex { get; set; }
        public uint LayerNumber { get; set; }
        public UInkViewport Viewport { get; set; }
        public UInkBackground Background { get; set; }
        public int PageState { get; set; }
        public int PageKind { get; set; }
        public int Binding { get; set; }
        public int? SlideId { get; set; }

        public bool IsRetained { get { return PageState == 1; } }
        public bool IsEndScreen { get { return PageKind == 1; } }

        /// <summary>Canvas 唯一键 (Workspace, Device, pageGuid, layerIndex)（CANVAS-5）。</summary>
        public string UniqueKey
        {
            get
            {
                return (WorkspaceGuid ?? "") + "|" + (DeviceGuid ?? "") + "|" + (PageGuid ?? "") + "|" + LayerIndex;
            }
        }
    }

    public sealed class UInkHardware
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public Dictionary<string, string> Identifiers { get; set; }
        public uint? PhysicalWidth { get; set; }
        public uint? PhysicalHeight { get; set; }
        public double? ScaleFactor { get; set; }
    }

    public sealed class UInkDevice
    {
        public string Guid { get; set; }
        public int DeviceType { get; set; }     // 0 Display, 1 Window
        public string Name { get; set; }
        public UInkHardware Hardware { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public string ParentDeviceGuid { get; set; }
        public double WindowX { get; set; }
        public double WindowY { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public uint ZIndex { get; set; }
    }

    public sealed class UInkWorkspace
    {
        public string Guid { get; set; }
        public int WorkspaceType { get; set; }
        public string Name { get; set; }
        public string ParentWorkspaceGuid { get; set; }
        public string HostId { get; set; }
        public uint CurrentPageIndex { get; set; }
    }

    public sealed class UInkHeader
    {
        public ushort Version { get; set; } = 11;
        public string Guid { get; set; }
        public uint DeviceNum { get; set; } = 1;
        public uint WorkspaceNum { get; set; } = 1;
        public uint PageNum { get; set; }
        public ulong Time { get; set; }
    }

    public sealed class UInkHeaderExtension
    {
        public string Name { get; set; }
        public string Explanation { get; set; }
        public List<UInkDevice> Devices { get; } = new List<UInkDevice>();
        public List<UInkWorkspace> Workspaces { get; } = new List<UInkWorkspace>();
    }

    /// <summary>Canvas + 它作用域内的内容块（FILE-3）。</summary>
    public sealed class UInkLayer
    {
        public UInkCanvas Canvas { get; set; }
        public List<UInkBlock> Blocks { get; } = new List<UInkBlock>();
    }

    /// <summary>组织后的页面：同一 pageGuid 在同一 Device 上的所有图层（FILE-9）。</summary>
    public sealed class UInkPage
    {
        public string WorkspaceGuid { get; set; }
        public string DeviceGuid { get; set; }
        public string PageGuid { get; set; }
        public uint PageIndex { get; set; }
        public uint PageNumber { get; set; }
        public List<UInkLayer> Layers { get; } = new List<UInkLayer>();

        /// <summary>第 0 层：viewport、背景、绑定等页面元数据所在层（CANVAS-8）。</summary>
        public UInkCanvas PrimaryCanvas
        {
            get { return Layers.Count > 0 ? Layers[0].Canvas : null; }
        }

        public bool IsRetained
        {
            get { return PrimaryCanvas != null && PrimaryCanvas.IsRetained; }
        }
    }

    public enum UInkReadStatus
    {
        Ok = 0,
        /// <summary>文件结尾落在最后一个对象中间（FILE-16）。</summary>
        TruncatedTail = 1,
        /// <summary>中间对象损坏（FILE-17），只保留之前的内容。</summary>
        CorruptMiddle = 2
    }

    public sealed class UInkDocument
    {
        public UInkHeader Header { get; set; }
        public UInkHeaderExtension Extension { get; set; }
        public List<UInkDevice> Devices { get; } = new List<UInkDevice>();
        public List<UInkWorkspace> Workspaces { get; } = new List<UInkWorkspace>();
        public List<UInkPage> Pages { get; } = new List<UInkPage>();
        public List<string> Warnings { get; } = new List<string>();
        public UInkReadStatus Status { get; set; }

        /// <summary>SAVE-22：可安全追加的位置；-1 表示不可追加（需完整保存）。</summary>
        public long SafeAppendPosition { get; set; } = -1;

        public bool HasUnknownContent { get; set; }

        public UInkDevice GetDevice(string guid)
        {
            if (guid == null) return null;
            for (int i = 0; i < Devices.Count; i++)
                if (string.Equals(Devices[i].Guid, guid, StringComparison.OrdinalIgnoreCase)) return Devices[i];
            return null;
        }

        public UInkWorkspace GetWorkspace(string guid)
        {
            if (guid == null) return null;
            for (int i = 0; i < Workspaces.Count; i++)
                if (string.Equals(Workspaces[i].Guid, guid, StringComparison.OrdinalIgnoreCase)) return Workspaces[i];
            return null;
        }
    }
}
