using System;

namespace InkCanvasLinkage.Core
{
    public sealed class PresentationLinkState
    {
        public PresentationLinkState(bool isConnected, bool isInSlideShow, int currentSlide, int totalSlides,
            PresentationMetadata presentation = null, Guid presentationSessionId = default, Guid slideShowSessionId = default)
        {
            if (currentSlide < 0) throw new ArgumentOutOfRangeException(nameof(currentSlide));
            if (totalSlides < 0) throw new ArgumentOutOfRangeException(nameof(totalSlides));
            if (totalSlides > 0 && currentSlide > totalSlides) throw new ArgumentOutOfRangeException(nameof(currentSlide));

            IsConnected = isConnected;
            IsInSlideShow = isInSlideShow;
            CurrentSlide = currentSlide;
            TotalSlides = totalSlides;
            Presentation = presentation;
            PresentationSessionId = presentationSessionId;
            SlideShowSessionId = slideShowSessionId;
        }

        public bool IsConnected { get; }
        public bool IsInSlideShow { get; }
        /// <summary>One-based slide number; zero means unknown.</summary>
        public int CurrentSlide { get; }
        public int TotalSlides { get; }
        public PresentationMetadata Presentation { get; }
        public Guid PresentationSessionId { get; }
        public Guid SlideShowSessionId { get; }
    }
}
