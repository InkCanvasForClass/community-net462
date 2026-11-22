using System;

namespace InkCanvasLinkage.Core
{
    public sealed class PresentationMetadata
    {
        public PresentationMetadata(string name, string fullName, int totalSlides,
            bool hasHiddenSlides = false, bool hasAutoPlayTimings = false)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A presentation name is required.", nameof(name));
            if (totalSlides < 0 || totalSlides > int.MaxValue - 2) throw new ArgumentOutOfRangeException(nameof(totalSlides));

            Name = name;
            FullName = fullName;
            TotalSlides = totalSlides;
            HasHiddenSlides = hasHiddenSlides;
            HasAutoPlayTimings = hasAutoPlayTimings;
        }

        public string Name { get; }
        public string FullName { get; }
        public int TotalSlides { get; }
        public bool HasHiddenSlides { get; }
        public bool HasAutoPlayTimings { get; }
    }
}
