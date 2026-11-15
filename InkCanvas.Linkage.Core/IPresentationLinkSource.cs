using System;

namespace InkCanvasLinkage.Core
{
    public interface IPresentationLinkSource : IDisposable
    {
        event Action<PresentationLinkState> StateChanged;
        event Action<PresentationLinkState> SlideShowBegin;
        event Action<PresentationLinkState> SlideShowNextSlide;
        event Action<PresentationLinkState> SlideShowEnd;
        event Action<PresentationLinkState> PresentationOpen;
        event Action<PresentationLinkState> PresentationClose;
        event Action<bool> PPTConnectionChanged;
        event Action<bool> SlideShowStateChanged;

        PresentationLinkState State { get; }
        PresentationCapabilities Capabilities { get; }
        bool IsSupportWPS { get; set; }
        bool SkipAnimationsWhenNavigating { get; set; }

        bool IsConnected { get; }
        bool IsInSlideShow { get; }
        int SlidesCount { get; }

        /// <summary>Returns null when no presentation is available; never returns a COM or protocol object.</summary>
        PresentationMetadata GetPresentationMetadata();
        int GetCurrentSlideNumber();
        string GetPresentationName();
        bool TryShowSlideNavigation();
        System.Collections.Generic.IReadOnlyList<PresentationThumbnail> ExportSlideThumbnails(int width, int height);
        PresentationSmartRegions GetSmartRegions();
        bool TryUnhideHiddenSlides();
        bool TryDisableAutoPlayTimings();
        bool SetEnhancedMonitoring(bool enabled, bool isShutdown = false);
        void StartMonitoring();
        void StopMonitoring(bool isShutdown = false);
        void ReloadConnection();
        bool TryStartSlideShow();
        bool TryEndSlideShow();
        bool TryNavigateToSlide(int slideNumber);
        bool TryNavigateNext();
        bool TryNavigatePrevious();
    }
}
