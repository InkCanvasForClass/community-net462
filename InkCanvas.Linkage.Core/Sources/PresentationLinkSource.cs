using System;
using System.Collections.Generic;
using System.Linq;

namespace InkCanvasLinkage.Core.Sources
{
    internal sealed class PresentationLinkSource : IPresentationLinkSource
    {
        private readonly StaDispatcher _dispatcher;
        private readonly IPPTLinkManager _backend;
        private readonly LogHelper _log;
        private readonly PresentationLinkSession _session = new PresentationLinkSession();
        private readonly EnhancedPowerPointSession _enhanced;
        private bool _monitoring;
        private bool _disposed;
        private int _generation;
        private volatile PresentationLinkState _state = new PresentationLinkState(false, false, 0, 0);

        public PresentationLinkSource(PresentationLinkMode mode, PresentationLinkOptions options)
        {
            _log = new LogHelper(options.Log);
            _dispatcher = new StaDispatcher(options.Log);
            _backend = _dispatcher.Invoke<IPPTLinkManager>(() => mode == PresentationLinkMode.Rot
                ? new ROTPPTManager(_dispatcher, options)
                : mode == PresentationLinkMode.Agent ? new PPTAgentLinkManager(options)
                : new ComPPTLinkManager(_dispatcher, options));
            Capabilities = PresentationCapabilities.SlideNavigation | PresentationCapabilities.Thumbnails
                | PresentationCapabilities.HiddenSlides | PresentationCapabilities.AutoPlayTimings | PresentationCapabilities.SmartRegions;
            if (_backend is ComPPTLinkManager com)
            {
                Capabilities |= PresentationCapabilities.EnhancedMonitoring;
                _enhanced = new EnhancedPowerPointSession(_dispatcher, com, _log);
            }
            _backend.IsSupportWPS = options.SupportWps;
            _backend.SkipAnimationsWhenNavigating = options.SkipAnimations;
            _backend.PPTConnectionChanged += connected => Capture(connected: connected);
            _backend.SlideShowStateChanged += running => Capture(running: running);
            _backend.PresentationOpen += _ => Capture();
            _backend.PresentationClose += _ => Capture(closed: true);
            _backend.SlideShowBegin += _ => Capture(running: true);
            _backend.SlideShowEnd += _ => Capture(running: false);
            _backend.SlideShowNextSlide += state => Capture(page: state.CurrentSlide);
            if (_backend is PPTAgentLinkManager agent) agent.StateUpdated += () => Capture();
            _session.StateChanged += state => { _state = state; StateChanged?.Invoke(state); };
            _session.PresentationOpen += state => PresentationOpen?.Invoke(state);
            _session.PresentationClose += state => PresentationClose?.Invoke(state);
            _session.SlideShowBegin += state => SlideShowBegin?.Invoke(state);
            _session.SlideShowNextSlide += state => SlideShowNextSlide?.Invoke(state);
            _session.SlideShowEnd += state => SlideShowEnd?.Invoke(state);
            _session.PPTConnectionChanged += value => PPTConnectionChanged?.Invoke(value);
            _session.SlideShowStateChanged += value => SlideShowStateChanged?.Invoke(value);
        }

        public event Action<PresentationLinkState> StateChanged;
        public event Action<PresentationLinkState> SlideShowBegin;
        public event Action<PresentationLinkState> SlideShowNextSlide;
        public event Action<PresentationLinkState> SlideShowEnd;
        public event Action<PresentationLinkState> PresentationOpen;
        public event Action<PresentationLinkState> PresentationClose;
        public event Action<bool> PPTConnectionChanged;
        public event Action<bool> SlideShowStateChanged;
        public PresentationCapabilities Capabilities { get; }
        public PresentationLinkState State => _state;
        public bool IsConnected => State.IsConnected;
        public bool IsInSlideShow => State.IsInSlideShow;
        public int SlidesCount => State.TotalSlides;
        public bool IsSupportWPS { get => _dispatcher.Invoke(() => _backend.IsSupportWPS); set => _dispatcher.Invoke(() => _backend.IsSupportWPS = value); }
        public bool SkipAnimationsWhenNavigating { get => _dispatcher.Invoke(() => _backend.SkipAnimationsWhenNavigating); set => _dispatcher.Invoke(() => _backend.SkipAnimationsWhenNavigating = value); }
        public PresentationMetadata GetPresentationMetadata() => State.Presentation;
        public int GetCurrentSlideNumber() => State.CurrentSlide;
        public string GetPresentationName() => State.Presentation?.Name ?? string.Empty;

        private void Capture(bool? connected = null, bool? running = null, bool closed = false, int? page = null, bool refreshDetails = false)
        {
            // Capture Agent data before dispatching: later pipe messages must not replace this event's payload.
            var agent = _backend as PPTAgentLinkManager;
            var agentState = agent?.CurrentState;
            if (agent != null) connected = connected ?? agent.IsConnected;
            int generation = _generation;
            Action capture = () =>
            {
                if (_disposed || !_monitoring || generation != _generation) return;
                try
                {
                    bool isConnected = connected ?? _backend.IsConnected;
                    var metadata = closed || !isConnected ? null : agentState != null
                        ? (string.IsNullOrEmpty(agentState.PresentationName) ? null : new PresentationMetadata(agentState.PresentationName,
                            agentState.PresentationFullName, agentState.TotalSlides, agentState.HasHiddenSlides, agentState.HasAutoPlayTimings))
                        : _backend.GetPresentationMetadata();
                    if (agent == null && metadata != null)
                    {
                        var previous = _session.State.Presentation;
                        if (!refreshDetails && previous != null && previous.Name == metadata.Name && previous.FullName == metadata.FullName)
                            metadata = new PresentationMetadata(metadata.Name, metadata.FullName, metadata.TotalSlides,
                                previous.HasHiddenSlides, previous.HasAutoPlayTimings);
                        else
                        {
                            try
                            {
                                metadata = ComPresentationCapabilities.ReadDetails(_backend.GetCurrentActivePresentation(), metadata,
                                    ex => _log.WriteLogToFile($"读取联动状态失败: {ex}", LogHelper.LogType.Warning));
                            }
                            catch (Exception ex) { _log.WriteLogToFile($"读取联动状态失败: {ex}", LogHelper.LogType.Warning); }
                        }
                    }
                    _session.Update(isConnected, running ?? (agentState?.IsRunning ?? _backend.IsInSlideShow),
                        page ?? (agentState?.SlideIndex ?? _backend.GetCurrentSlideNumber()), metadata);
                }
                catch (Exception ex) { _log.WriteLogToFile($"读取联动状态失败: {ex}", LogHelper.LogType.Warning); }
            };
            if (_dispatcher.CheckAccess) capture();
            else _dispatcher.Post(capture);
        }

        public void StartMonitoring() => _dispatcher.Invoke(() => { if (_disposed || _monitoring) return; _generation++; _monitoring = true; _backend.StartMonitoring(); });
        public void StopMonitoring(bool isShutdown = false) => _dispatcher.Invoke(() =>
        {
            _monitoring = false;
            _generation++;
            _enhanced?.SetEnabled(false);
            _backend.StopMonitoring(isShutdown);
            _session.Update(false, false, 0, null);
        });
        public void ReloadConnection() => _dispatcher.Invoke(() =>
        {
            if (_disposed) return;
            _generation++;
            _monitoring = true;
            _session.Update(false, false, 0, null);
            _backend.ReloadConnection();
            _backend.StartMonitoring();
        });

        private bool Execute(Func<bool> action)
        {
            if (_disposed) return false;
            int generation = _generation;
            var state = State;
            return _dispatcher.Invoke(() =>
            {
                if (_disposed || generation != _generation || state.PresentationSessionId != State.PresentationSessionId
                    || state.SlideShowSessionId != State.SlideShowSessionId) return false;
                try { return action(); }
                catch (Exception ex) { _log.WriteLogToFile(ex.ToString(), LogHelper.LogType.Warning); return false; }
            });
        }
        public bool TryStartSlideShow() => IsConnected && Execute(_backend.TryStartSlideShow);
        public bool TryEndSlideShow() => IsInSlideShow && Execute(_backend.TryEndSlideShow);
        public bool TryNavigateNext() => IsInSlideShow && Execute(_backend.TryNavigateNext);
        public bool TryNavigatePrevious() => IsInSlideShow && Execute(_backend.TryNavigatePrevious);
        public bool TryNavigateToSlide(int slideNumber) => slideNumber > 0 && slideNumber <= SlidesCount && Execute(() => _backend.TryNavigateToSlide(slideNumber));
        public bool TryShowSlideNavigation() => Execute(_backend.TryShowSlideNavigation);
        public bool TryUnhideHiddenSlides() => Execute(() =>
        {
            bool changed = _backend is PPTAgentLinkManager agent
                ? agent.TryUnhideHiddenSlides() : ComPresentationCapabilities.Unhide(_backend.GetCurrentActivePresentation());
            if (changed) Capture(refreshDetails: true);
            return changed;
        });
        public bool TryDisableAutoPlayTimings() => Execute(() =>
        {
            bool changed = _backend is PPTAgentLinkManager agent
                ? agent.TryDisableAutoPlayTimings() : ComPresentationCapabilities.DisableTimings(_backend.GetCurrentActivePresentation());
            if (changed) Capture(refreshDetails: true);
            return changed;
        });
        public bool SetEnhancedMonitoring(bool enabled, bool isShutdown = false) => Execute(() => _enhanced?.SetEnabled(enabled) == true);

        public IReadOnlyList<PresentationThumbnail> ExportSlideThumbnails(int width, int height)
        {
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) throw new ArgumentOutOfRangeException(nameof(width));
            if (_disposed) return Array.Empty<PresentationThumbnail>();
            return _dispatcher.Invoke(() => (IReadOnlyList<PresentationThumbnail>)_backend.ExportSlideThumbnails(width, height)
                .Where(item => item.SlideNumber > 0 && item.PngBytes != null)
                .Select(item => new PresentationThumbnail(item.SlideNumber, item.PngBytes)).ToList().AsReadOnly());
        }
        public PresentationSmartRegions GetSmartRegions()
        {
            if (_disposed) return new PresentationSmartRegions();
            return _dispatcher.Invoke(() =>
            {
                try
                {
                    if (_backend is PPTAgentLinkManager agent)
                    {
                        var result = agent.GetSmartRegions();
                        return new PresentationSmartRegions(result.SlideIndex, result.SlideWidth, result.SlideHeight,
                            new IntPtr(result.SlideShowWindowHandle), result.Regions?.Select(region =>
                                new PresentationSmartRegion(region.X, region.Y, region.Width, region.Height)));
                    }
                    return ComPresentationCapabilities.ReadSmartRegions(_backend.PPTApplication);
                }
                catch (Exception ex) { _log.WriteLogToFile(ex.ToString(), LogHelper.LogType.Warning); return new PresentationSmartRegions(); }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                _dispatcher.Invoke(() =>
                {
                    if (_disposed) return;
                    _disposed = true;
                    _monitoring = false;
                    _generation++;
                    try { _enhanced?.Dispose(); }
                    finally
                    {
                        try { _backend.Dispose(); }
                        finally { _session.Update(false, false, 0, null); }
                    }
                });
            }
            finally { _dispatcher.Dispose(); }
        }
    }
}
