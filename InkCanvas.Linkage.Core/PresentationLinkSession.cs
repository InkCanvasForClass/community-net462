using System;

namespace InkCanvasLinkage.Core
{
    /// <summary>Normalizes polling and event notifications into one ordered session stream.</summary>
    public sealed class PresentationLinkSession
    {
        public PresentationLinkState State { get; private set; } = new PresentationLinkState(false, false, 0, 0);
        public event Action<PresentationLinkState> StateChanged;
        public event Action<PresentationLinkState> PresentationOpen;
        public event Action<PresentationLinkState> PresentationClose;
        public event Action<PresentationLinkState> SlideShowBegin;
        public event Action<PresentationLinkState> SlideShowEnd;
        public event Action<PresentationLinkState> SlideShowNextSlide;
        public event Action<bool> PPTConnectionChanged;
        public event Action<bool> SlideShowStateChanged;

        public void Update(bool connected, bool running, int page, PresentationMetadata metadata)
        {
            var old = State;
            running = connected && running && metadata != null;
            if (!connected) metadata = null;
            int total = metadata?.TotalSlides ?? 0;
            page = Math.Max(0, Math.Min(page, total));
            bool sameDocument = old.Presentation != null && metadata != null
                && string.Equals(old.Presentation.FullName, metadata.FullName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(old.Presentation.Name, metadata.Name, StringComparison.OrdinalIgnoreCase);
            var documentId = metadata == null ? Guid.Empty : sameDocument ? old.PresentationSessionId : Guid.NewGuid();
            var showId = running ? (sameDocument && old.IsInSlideShow ? old.SlideShowSessionId : Guid.NewGuid()) : old.SlideShowSessionId;
            var next = new PresentationLinkState(connected, running, page, total, metadata, documentId, showId);
            if (old.IsConnected == connected && old.IsInSlideShow == running && old.CurrentSlide == page
                && old.TotalSlides == total && old.PresentationSessionId == documentId
                && old.Presentation?.HasHiddenSlides == metadata?.HasHiddenSlides
                && old.Presentation?.HasAutoPlayTimings == metadata?.HasAutoPlayTimings) return;

            State = next;
            if (old.IsInSlideShow && (!running || !sameDocument)) SlideShowEnd?.Invoke(old);
            if (old.Presentation != null && !sameDocument) PresentationClose?.Invoke(old);
            if (old.IsConnected != connected) PPTConnectionChanged?.Invoke(connected);
            if (metadata != null && !sameDocument) PresentationOpen?.Invoke(next);
            if (old.IsInSlideShow != running || (running && !sameDocument)) SlideShowStateChanged?.Invoke(running);
            if (running && (!old.IsInSlideShow || !sameDocument)) SlideShowBegin?.Invoke(next);
            else if (running && old.CurrentSlide != page && page > 0) SlideShowNextSlide?.Invoke(next);
            StateChanged?.Invoke(next);
        }
    }
}
