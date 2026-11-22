using InkCanvasLinkage.Core;
using System;
using System.Collections.Generic;

namespace InkCanvasLinkage.Core.Sources
{
    internal sealed class PPTSlideThumbnail
    {
        public int SlideNumber { get; set; }
        public byte[] PngBytes { get; set; }
    }

    // Raw backend contract is private to the core; the public source never exposes these objects.
    internal interface IPPTLinkManager : IDisposable
    {
        event Action<PresentationLinkState> SlideShowNextSlide;
        bool IsConnected { get; }
        bool IsInSlideShow { get; }
        int SlidesCount { get; }
        PresentationMetadata GetPresentationMetadata();
        void StartMonitoring();
        void StopMonitoring(bool isShutdown = false);
        void ReloadConnection();
        bool TryStartSlideShow();
        bool TryEndSlideShow();
        bool TryNavigateToSlide(int slideNumber);
        bool TryNavigateNext();
        bool TryNavigatePrevious();
        event Action<object> SlideShowBegin;
        event Action<object> SlideShowEnd;
        event Action<object> PresentationOpen;
        event Action<object> PresentationClose;
        event Action<bool> PPTConnectionChanged;
        event Action<bool> SlideShowStateChanged;

        bool IsSupportWPS { get; set; }
        bool SkipAnimationsWhenNavigating { get; set; }

        object PPTApplication { get; }

        // 查询
        int GetCurrentSlideNumber();
        string GetPresentationName();
        bool TryShowSlideNavigation();
        object GetCurrentActivePresentation();
        List<PPTSlideThumbnail> ExportSlideThumbnails(int width, int height);
    }
}

