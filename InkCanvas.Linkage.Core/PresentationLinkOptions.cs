using System;

namespace InkCanvasLinkage.Core
{
    public enum PresentationLinkMode { Com, Rot, Agent }
    public enum PresentationLogLevel { Trace, Info, Event, Warning, Error }

    public sealed class PresentationLinkOptions
    {
        public bool SupportWps { get; set; }
        public bool SkipAnimations { get; set; }
        public bool DebugProbeEnabled { get; set; }
        public Action<string, PresentationLogLevel> Log { get; set; }
    }
}
