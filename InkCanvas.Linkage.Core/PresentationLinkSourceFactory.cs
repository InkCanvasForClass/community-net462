using InkCanvasLinkage.Core.Sources;
using System;

namespace InkCanvasLinkage.Core
{
    public static class PresentationLinkSourceFactory
    {
        public static IPresentationLinkSource Create(PresentationLinkMode mode, PresentationLinkOptions options = null)
        {
            if (!Enum.IsDefined(typeof(PresentationLinkMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            return new PresentationLinkSource(mode, options ?? new PresentationLinkOptions());
        }
    }
}
