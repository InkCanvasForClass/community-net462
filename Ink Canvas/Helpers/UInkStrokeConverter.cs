using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using InkUInk;
using InkUInk.Model;

namespace Ink_Canvas.Helpers
{
    /// <summary>构建 UInk 文档时的参数。</summary>
    public sealed class UInkBuildOptions
    {
        /// <summary>Workspace 类型：0 屏幕批注、1 白板、2 PPT。</summary>
        public int WorkspaceType { get; set; }

        public string WorkspaceName { get; set; }

        /// <summary>PPT 场景的宿主标识（HEXT-8）。</summary>
        public string HostId { get; set; }

        /// <summary>当前显示页（HEXT-10）。</summary>
        public int CurrentPageIndex { get; set; }

        /// <summary>DIP → Device 像素的缩放（高 DPI 下大于 1）。</summary>
        public double DpiScale { get; set; } = 1.0;

        /// <summary>Device（显示器）的物理像素尺寸。</summary>
        public int ScreenWidth { get; set; }

        public int ScreenHeight { get; set; }

        /// <summary>复用已有逻辑文件时传入原 Header.guid（VER-7 / HDR-3）。</summary>
        public string DocumentGuid { get; set; }

        /// <summary>复用已有页面的 pageGuid，按页序对应；缺项时生成新 GUID。</summary>
        public IList<string> PageGuids { get; set; }
    }

    /// <summary>
    /// WPF 墨迹与 UInk 块之间的转换。
    ///
    /// 笔锋：ICCE 的渲染宽度是 <c>DrawingAttributes.Width × clamp(0.42 + 1.16 × pressure, 0.22, 2.1)</c>
    /// （见 MultiTouchInput.PressureToVisualScale），UInk 要求逐点宽度，因此写入时按同一公式换算成
    /// 每个点的 <c>width</c>，读取时再反算回 PressureFactor。
    ///
    /// 擦除与清空在 ICCE 中是破坏性的（笔画已被切分或删除），所以写出的是「已烘焙」的紧凑内容，
    /// 不需要 Erase Ink / Clear 块——这正好符合 COMPAT-5 的兼容导出结果。
    /// </summary>
    public static class UInkStrokeConverter
    {
        private const double PressureScaleBase = 0.42;
        private const double PressureScaleGain = 1.16;
        private const double PressureScaleMin = 0.22;
        private const double PressureScaleMax = 2.1;

        public static double PressureToVisualScale(float pressure, bool ignorePressure)
        {
            if (ignorePressure) return 1.0;
            return Math.Max(PressureScaleMin, Math.Min(PressureScaleMax, PressureScaleBase + PressureScaleGain * pressure));
        }

        private static double VisualScaleToPressure(double scale)
        {
            double pressure = (scale - PressureScaleBase) / PressureScaleGain;
            if (pressure < 0.01) return 0.01;
            if (pressure > 1.0) return 1.0;
            return pressure;
        }

        private static uint ToFallback(Color color)
        {
            return ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        }

        private static double ToOpacity(Color color)
        {
            return color.A / 255.0;
        }

        private static Color FromFallback(uint fallback, double opacity)
        {
            byte a = (byte)Math.Max(0, Math.Min(255, Math.Round(opacity * 255.0)));
            return Color.FromArgb(a,
                (byte)((fallback >> 16) & 0xFF),
                (byte)((fallback >> 8) & 0xFF),
                (byte)(fallback & 0xFF));
        }

        /// <summary>把一条 WPF 笔画转成 UInk Ink 块。</summary>
        public static UInkInk ToInk(Stroke stroke, uint contentId, uint undoId, double dpiScale = 1.0)
        {
            if (stroke == null) return null;

            var attributes = stroke.DrawingAttributes;
            bool rectangleNib = attributes.StylusTip == StylusTip.Rectangle;
            bool highlighter = attributes.IsHighlighter;

            // 矩形笔尖：width 是笔尖高度，aspect = 宽 ÷ 高（NIB 尺寸）。
            double baseWidth = rectangleNib ? attributes.Height : attributes.Width;
            if (baseWidth <= 0) baseWidth = 1;

            var ink = new UInkInk
            {
                ContentId = contentId,
                UndoId = undoId,
                InkType = highlighter ? UInkInkType.Highlighter : UInkInkType.Pen,
                Color = new UInkColor(ToFallback(attributes.Color)),
                Opacity = ToOpacity(attributes.Color),
                Texture = 0
            };

            if (rectangleNib)
            {
                ink.Nib = new UInkNib
                {
                    Shape = 1,
                    Aspect = attributes.Height > 0 ? attributes.Width / attributes.Height : 1,
                    Orientation = 0
                };
            }

            foreach (StylusPoint point in stroke.StylusPoints)
            {
                double width = rectangleNib
                    ? baseWidth
                    : baseWidth * PressureToVisualScale(point.PressureFactor, attributes.IgnorePressure);

                ink.Points.Add(new UInkPoint
                {
                    X = point.X * dpiScale,
                    Y = point.Y * dpiScale,
                    Width = Math.Max(0.01, width * dpiScale)
                });
            }

            if (ink.Points.Count == 0) return null;
            return ink;
        }

        /// <summary>把 UInk Ink 块还原成一条 WPF 笔画。</summary>
        public static Stroke ToStroke(UInkInk ink, Matrix transform = default)
        {
            if (ink == null) return null;

            var absolute = ink.Points;
            if (absolute.Count == 0) return null;

            double sum = 0;
            for (int i = 0; i < absolute.Count; i++) sum += absolute[i].Width;
            double average = Math.Max(0.01, sum / absolute.Count);
            double width = average * transform.M11;

            bool rectangleNib = ink.Nib != null && ink.Nib.IsRectangle;

            var attributes = new DrawingAttributes
            {
                Color = FromFallback(ink.Color != null ? ink.Color.Fallback : 0, ink.Opacity),
                FitToCurve = false,
                IsHighlighter = ink.InkType == UInkInkType.Highlighter,
                StylusTip = rectangleNib ? StylusTip.Rectangle : StylusTip.Ellipse,
                IgnorePressure = rectangleNib
            };

            if (rectangleNib)
            {
                attributes.Height = ClampStylusSize(width);
                attributes.Width = ClampStylusSize(width * ink.Nib.Aspect);
            }
            else
            {
                attributes.Width = ClampStylusSize(width);
                attributes.Height = attributes.Width;
            }

            var points = new StylusPointCollection();
            for (int i = 0; i < absolute.Count; i++)
            {
                var point = absolute[i];
                float pressure = rectangleNib
                    ? 0.5f
                    : (float)VisualScaleToPressure(point.Width / average);

                Point position = transform.Transform(new Point(point.X, point.Y));
                var stylusPoint = new StylusPoint(position.X, position.Y, pressure);
                points.Add(stylusPoint);
            }

            if (points.Count == 1)
            {
                // 单点笔迹：复制一个极小位移的点，保证 WPF 能画出圆点。
                points.Add(new StylusPoint(points[0].X + 0.01, points[0].Y, points[0].PressureFactor));
            }

            return new Stroke(points, attributes);
        }

        private static double ClampStylusSize(double size)
            => Math.Max(DrawingAttributes.MinWidth, Math.Min(DrawingAttributes.MaxWidth, size));

        /// <summary>把 UInk Shape 采样为轮廓笔画。单页导入不保留填充和虚线。</summary>
        public static StrokeCollection ToStrokes(UInkShape shape, Matrix transform = default)
        {
            if (shape == null || shape.Geometry == null) return null;

            var result = new StrokeCollection();
            var points = BuildShapePoints(shape);
            if (points == null || points.Count < 2) return result;

            double width = ClampStylusSize((shape.Stroke?.Width ?? 1.0) * transform.M11);

            double opacity = shape.Stroke != null ? shape.Stroke.Opacity : 1.0;
            uint fallback = shape.Stroke != null && shape.Stroke.Color != null
                ? shape.Stroke.Color.Fallback
                : (shape.Fill != null && shape.Fill.Color != null ? shape.Fill.Color.Fallback : 0u);

            var attributes = new DrawingAttributes
            {
                Color = FromFallback(fallback, opacity),
                Width = width,
                Height = width,
                FitToCurve = false,
                StylusTip = StylusTip.Ellipse,
                IgnorePressure = true
            };

            var collection = new StylusPointCollection();
            foreach (var point in points)
            {
                Point position = transform.Transform(new Point(point.X, point.Y));
                collection.Add(new StylusPoint(position.X, position.Y));
            }

            if (collection.Count >= 2) result.Add(new Stroke(collection, attributes));
            return result;
        }

        private static List<UInkGeomPoint> BuildShapePoints(UInkShape shape)
        {
            var geometry = shape.Geometry;
            var points = new List<UInkGeomPoint>();
            double cx, cy;

            switch (shape.ShapeType)
            {
                case UInkShapeType.Line:
                case UInkShapeType.Polyline:
                    foreach (var p in geometry.Points) points.Add(new UInkGeomPoint { X = p.X, Y = p.Y });
                    return points;

                case UInkShapeType.Polygon:
                    foreach (var p in geometry.Points) points.Add(new UInkGeomPoint { X = p.X, Y = p.Y });
                    if (points.Count > 0) points.Add(new UInkGeomPoint { X = points[0].X, Y = points[0].Y });
                    return points;

                case UInkShapeType.Rectangle:
                {
                    cx = geometry.CenterX ?? 0;
                    cy = geometry.CenterY ?? 0;
                    double hw = (geometry.Width ?? 0) / 2;
                    double hh = (geometry.Height ?? 0) / 2;
                    AddRotatedRect(points, cx, cy, hw, hh, geometry.Rotation);
                    return points;
                }

                case UInkShapeType.Square:
                {
                    cx = geometry.CenterX ?? 0;
                    cy = geometry.CenterY ?? 0;
                    double half = (geometry.Size ?? 0) / 2;
                    AddRotatedRect(points, cx, cy, half, half, geometry.Rotation);
                    return points;
                }

                case UInkShapeType.Ellipse:
                {
                    cx = geometry.CenterX ?? 0;
                    cy = geometry.CenterY ?? 0;
                    AddEllipse(points, cx, cy, (geometry.Width ?? 0) / 2, (geometry.Height ?? 0) / 2, geometry.Rotation);
                    return points;
                }

                case UInkShapeType.Circle:
                    AddEllipse(points, geometry.CenterX ?? 0, geometry.CenterY ?? 0,
                               geometry.Radius ?? 0, geometry.Radius ?? 0, 0);
                    return points;
            }

            return null;
        }

        private static void AddRotatedRect(List<UInkGeomPoint> points, double cx, double cy, double hw, double hh, double rotation)
        {
            var corners = new[]
            {
                new Point(-hw, -hh), new Point(hw, -hh), new Point(hw, hh), new Point(-hw, hh), new Point(-hw, -hh)
            };

            double cos = Math.Cos(rotation), sin = Math.Sin(rotation);
            foreach (var corner in corners)
            {
                points.Add(new UInkGeomPoint
                {
                    X = cx + corner.X * cos - corner.Y * sin,
                    Y = cy + corner.X * sin + corner.Y * cos
                });
            }
        }

        private static void AddEllipse(List<UInkGeomPoint> points, double cx, double cy, double rx, double ry, double rotation)
        {
            const int segments = 64;
            double cos = Math.Cos(rotation), sin = Math.Sin(rotation);

            for (int i = 0; i <= segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double x = rx * Math.Cos(angle);
                double y = ry * Math.Sin(angle);
                points.Add(new UInkGeomPoint
                {
                    X = cx + x * cos - y * sin,
                    Y = cy + x * sin + y * cos
                });
            }
        }

        /// <summary>把一组页面（每页一个 StrokeCollection）构建成 UInk 文档。</summary>
        public static UInkDocument Build(IList<StrokeCollection> pages, UInkBuildOptions options)
        {
            if (pages == null) throw new ArgumentNullException(nameof(pages));
            options = options ?? new UInkBuildOptions();

            var document = new UInkDocument
            {
                Header = new UInkHeader
                {
                    Version = 11,
                    Guid = string.IsNullOrEmpty(options.DocumentGuid) ? Guid.NewGuid().ToString("D") : options.DocumentGuid,
                    Time = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
            };

            int screenWidth = options.ScreenWidth > 0 ? options.ScreenWidth : 1920;
            int screenHeight = options.ScreenHeight > 0 ? options.ScreenHeight : 1080;

            string deviceGuid = Guid.NewGuid().ToString("D");
            string workspaceGuid = Guid.NewGuid().ToString("D");

            var extension = new UInkHeaderExtension();
            extension.Devices.Add(new UInkDevice
            {
                Guid = deviceGuid,
                DeviceType = 0,
                Name = "Display",
                X = 0,
                Y = 0,
                Width = (uint)screenWidth,
                Height = (uint)screenHeight
            });
            extension.Workspaces.Add(new UInkWorkspace
            {
                Guid = workspaceGuid,
                WorkspaceType = options.WorkspaceType,
                Name = options.WorkspaceName,
                HostId = options.HostId,
                CurrentPageIndex = (uint)Math.Max(0, options.CurrentPageIndex)
            });
            document.Extension = extension;
            document.Devices.AddRange(extension.Devices);
            document.Workspaces.AddRange(extension.Workspaces);

            // UInk 默认视口使用设备像素，导出时直接把 WPF DIP 转成像素。
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                string pageGuid = options.PageGuids != null && pageIndex < options.PageGuids.Count
                                  && UInkValues.IsUuid(options.PageGuids[pageIndex])
                    ? options.PageGuids[pageIndex]
                    : Guid.NewGuid().ToString("D");

                var canvas = new UInkCanvas
                {
                    WorkspaceGuid = workspaceGuid,
                    DeviceGuid = deviceGuid,
                    PageGuid = pageGuid,
                    PageIndex = (uint)pageIndex,
                    PageNumber = (uint)(pageIndex + 1),
                    LayerIndex = 0,
                    LayerNumber = 0,
                    // PPT 场景按页码绑定：没有 SlideID 时写 binding = 1（CANVAS-13）。
                    Binding = options.WorkspaceType == UInkWorkspaceType.Presentation ? 1 : 0
                };

                var layer = new UInkLayer { Canvas = canvas };
                var strokes = pages[pageIndex] ?? new StrokeCollection();

                uint contentId = 0;
                foreach (Stroke stroke in strokes)
                {
                    var ink = ToInk(stroke, contentId, contentId, options.DpiScale);
                    if (ink == null) continue;
                    layer.Blocks.Add(ink);
                    contentId++;
                }

                var page = new UInkPage
                {
                    WorkspaceGuid = workspaceGuid,
                    DeviceGuid = deviceGuid,
                    PageGuid = pageGuid,
                    PageIndex = (uint)pageIndex,
                    PageNumber = (uint)(pageIndex + 1)
                };
                page.Layers.Add(layer);
                document.Pages.Add(page);
            }

            return document;
        }

        /// <summary>把 UInk 文档的一页转换为 StrokeCollection。</summary>
        public static StrokeCollection ToStrokeCollection(UInkPage page, out string warning, double dpiScale = 1.0)
        {
            warning = null;
            var result = new StrokeCollection();
            if (page == null) return result;

            // CANVAS-8：其余图层继承第 0 层的视口；WPF 再把设备像素换算成 DIP。
            var viewport = page.PrimaryCanvas?.Viewport;
            double scale = (viewport?.Scale ?? 1.0) / dpiScale;
            var transform = new Matrix(scale, 0, 0, scale, -(viewport?.X ?? 0) * scale, -(viewport?.Y ?? 0) * scale);

            bool skippedErase = false;

            foreach (var layer in page.Layers)
            {
                int lastClear = -1;
                for (int i = 0; i < layer.Blocks.Count; i++)
                    if (layer.Blocks[i] is UInkClear) lastClear = i;

                // CLEAR-7：未实现 Inkeys 附加级的读取器只显示最后一个 Clear 之后的内容。
                for (int i = lastClear + 1; i < layer.Blocks.Count; i++)
                {
                    var block = layer.Blocks[i];

                    if (block is UInkInk)
                    {
                        var ink = (UInkInk)block;
                        if (ink.InkType == UInkInkType.Erase)
                        {
                            // INK-11：不支持 Inkeys 附加级时必须跳过 Erase Ink。
                            skippedErase = true;
                            continue;
                        }

                        var stroke = ToStroke(ink, transform);
                        if (stroke != null) result.Add(stroke);
                    }
                    else if (block is UInkShape)
                    {
                        var strokes = ToStrokes((UInkShape)block, transform);
                        if (strokes != null) result.Add(strokes);
                    }
                }
            }

            if (skippedErase) warning = "文件使用了 UInk 的橡皮轨迹（Erase Ink），当前版本按规范跳过，显示结果可能与原软件不同";
            return result;
        }
    }
}
