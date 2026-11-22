using System;
using System.Collections.Generic;
using System.Linq;

namespace InkCanvasLinkage.Core
{
    [Flags]
    public enum PresentationCapabilities
    {
        None = 0, SlideNavigation = 1, Thumbnails = 2, HiddenSlides = 4,
        AutoPlayTimings = 8, SmartRegions = 16, EnhancedMonitoring = 32
    }

    public sealed class PresentationThumbnail
    {
        private readonly byte[] _png;
        public PresentationThumbnail(int slideNumber, byte[] pngBytes)
        {
            if (slideNumber <= 0) throw new ArgumentOutOfRangeException(nameof(slideNumber));
            SlideNumber = slideNumber;
            _png = (byte[])(pngBytes ?? throw new ArgumentNullException(nameof(pngBytes))).Clone();
        }
        public int SlideNumber { get; }
        public byte[] PngBytes => (byte[])_png.Clone();
    }

    public sealed class PresentationSmartRegion
    {
        public PresentationSmartRegion(double x, double y, double width, double height)
        { X = x; Y = y; Width = width; Height = height; }
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
    }

    public sealed class PresentationSmartRegions
    {
        public PresentationSmartRegions(int slideIndex = 0, float slideWidth = 0, float slideHeight = 0,
            IntPtr windowHandle = default, IEnumerable<PresentationSmartRegion> regions = null)
        {
            SlideIndex = slideIndex; SlideWidth = slideWidth; SlideHeight = slideHeight;
            WindowHandle = windowHandle;
            Regions = Array.AsReadOnly((regions ?? Enumerable.Empty<PresentationSmartRegion>()).ToArray());
        }
        public int SlideIndex { get; }
        public float SlideWidth { get; }
        public float SlideHeight { get; }
        public IntPtr WindowHandle { get; }
        public IReadOnlyList<PresentationSmartRegion> Regions { get; }
    }
}
