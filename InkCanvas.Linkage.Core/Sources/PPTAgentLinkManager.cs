using InkCanvasLinkage.Core.Sources;
using InkCanvasLinkage.Core;
using InkCanvasPPTAgent.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InkCanvasLinkage.Core.Sources
{
    internal sealed class PPTAgentLinkManager : IPPTLinkManager
    {
        private readonly object _stateLock = new object();
        private PPTAgentPipeClient _client;
        private PPTState _lastState = new PPTState();
        private bool _lastConnected;
        private bool _lastIsRunning;
        private int _lastSlideIndex;
        private string _lastPresentationName;
        private bool _disposed;

        public event Action<object> SlideShowBegin;
        public event Action<PresentationLinkState> SlideShowNextSlide;
        public event Action<object> SlideShowEnd;
        public event Action<object> PresentationOpen;
        public event Action<object> PresentationClose;
        public event Action<bool> PPTConnectionChanged;
        public event Action<bool> SlideShowStateChanged;
        internal event Action StateUpdated;

        public bool IsConnected => _client?.IsConnected == true;
        public bool IsInSlideShow => GetStateSnapshot().IsRunning;
        public bool IsSupportWPS { get; set; }
        public bool SkipAnimationsWhenNavigating { get; set; }
        public int SlidesCount => GetStateSnapshot().TotalSlides;
        public object PPTApplication => null;

        private readonly LogHelper LogHelper;
        public PPTAgentLinkManager(PresentationLinkOptions options)
        {
            LogHelper = new LogHelper(options.Log);
        }

        public PPTState CurrentState => GetStateSnapshot();

        public void StartMonitoring()
        {
            if (_disposed) return;
            if (_client != null) return;

            var client = new PPTAgentPipeClient(LogHelper);
            lock (_stateLock) _client = client;
            client.ConnectionChanged += connected =>
            {
                lock (_stateLock) { if (ReferenceEquals(_client, client) && !_disposed) OnConnectionChanged(connected); }
            };
            client.StateReceived += state =>
            {
                lock (_stateLock) { if (ReferenceEquals(_client, client) && !_disposed) HandleState(state, null); }
            };
            client.EventReceived += (eventName, state) =>
            {
                lock (_stateLock) { if (ReferenceEquals(_client, client) && !_disposed) HandleState(state, eventName); }
            };
            client.Start();

            LogHelper.WriteLogToFile("PPT Agent 联动监控已启动", LogHelper.LogType.Event);
        }

        public void StopMonitoring(bool isShutdown = false)
        {
            PPTAgentPipeClient client;
            lock (_stateLock) { client = _client; _client = null; }
            client?.Dispose();
            OnConnectionChanged(false);
            LogHelper.WriteLogToFile("PPT Agent 联动监控已停止", LogHelper.LogType.Event);
        }

        public void ReloadConnection()
        {
            StopMonitoring();
            StartMonitoring();
        }

        public bool TryStartSlideShow() => Send(PPTCommands.StartSlideShow);
        public bool TryEndSlideShow() => Send(PPTCommands.EndSlideShow);
        public bool TryNavigateNext() => Send(PPTCommands.Next);
        public bool TryNavigatePrevious() => Send(PPTCommands.Previous);

        public bool TryNavigateToSlide(int slideNumber)
        {
            if (slideNumber <= 0) return false;
            return Send(PPTCommands.GotoSlide, new GotoSlideRequest { SlideNumber = slideNumber });
        }

        public int GetCurrentSlideNumber() => GetStateSnapshot().SlideIndex;

        public string GetPresentationName() => GetStateSnapshot().PresentationName ?? string.Empty;

        public PresentationMetadata GetPresentationMetadata()
        {
            var state = GetStateSnapshot();
            return string.IsNullOrEmpty(state.PresentationName) ? null
                : new PresentationMetadata(state.PresentationName, state.PresentationFullName, state.TotalSlides,
                    state.HasHiddenSlides, state.HasAutoPlayTimings);
        }

        public bool TryShowSlideNavigation() => Send(PPTCommands.ShowSlideNavigation);

        public bool TryUnhideHiddenSlides() => Send(PPTCommands.UnhideHiddenSlides);

        public bool TryDisableAutoPlayTimings() => Send(PPTCommands.DisableAutoPlayTimings);

        public object GetCurrentActivePresentation() => null;

        public List<PPTSlideThumbnail> ExportSlideThumbnails(int width, int height)
        {
            var response = _client?.SendRequest<ExportSlideThumbnailsResponse>(
                PPTCommands.ExportSlideThumbnails,
                new ExportSlideThumbnailsRequest { Width = width, Height = height });

            if (response?.Slides == null)
                return new List<PPTSlideThumbnail>();

            return response.Slides
                .Where(s => s != null && s.PngBytes != null)
                .Select(s => new PPTSlideThumbnail { SlideNumber = s.SlideNumber, PngBytes = s.PngBytes })
                .ToList();
        }

        public SmartRegionsResponse GetSmartRegions()
        {
            return _client?.SendRequest<SmartRegionsResponse>(PPTCommands.GetSmartRegions)
                   ?? new SmartRegionsResponse();
        }

        private bool Send(string command, object data = null)
        {
            var result = _client?.SendCommand(command, data) == true;
            if (!result)
                LogHelper.WriteLogToFile($"PPT Agent 命令发送失败: {command}", LogHelper.LogType.Warning);
            return result;
        }

        private void OnConnectionChanged(bool connected)
        {
            lock (_stateLock)
            {
                if (_lastConnected == connected) return;
                _lastConnected = connected;
                if (!connected)
                {
                    _lastState = new PPTState();
                    _lastIsRunning = false;
                    _lastSlideIndex = 0;
                    _lastPresentationName = null;
                }
            }

            PPTConnectionChanged?.Invoke(connected);
            if (!connected)
                SlideShowStateChanged?.Invoke(false);
        }

        private void HandleState(PPTState state, string eventName)
        {
            if (_disposed || state == null) return;
            if (state.SlideIndex < 0 || state.TotalSlides < 0 || state.TotalSlides > int.MaxValue - 2
                || state.SlideIndex > state.TotalSlides)
            {
                LogHelper.WriteLogToFile("PPT Agent 返回了无效的幻灯片页码或总数", LogHelper.LogType.Warning);
                return;
            }

            bool raiseConnection = false;
            bool raiseShowStateChanged = false;
            bool newShowState = false;
            bool raisePresentationOpen = false;
            bool raisePresentationClose = false;
            bool raiseSlideShowBegin = false;
            bool raiseSlideShowNext = false;
            bool raiseSlideShowEnd = false;

            lock (_stateLock)
            {
                var oldRunning = _lastIsRunning;
                var oldSlide = _lastSlideIndex;
                var oldPresentation = _lastPresentationName;

                _lastState = new PPTState
                {
                    SlideIndex = state.SlideIndex, TotalSlides = state.TotalSlides, IsRunning = state.IsRunning,
                    PresentationName = state.PresentationName, PresentationFullName = state.PresentationFullName,
                    HasHiddenSlides = state.HasHiddenSlides, HasAutoPlayTimings = state.HasAutoPlayTimings
                };
                _lastIsRunning = state.IsRunning;
                _lastSlideIndex = state.SlideIndex;
                _lastPresentationName = state.PresentationName;

                if (!_lastConnected)
                {
                    _lastConnected = true;
                    raiseConnection = true;
                }

                if (!string.IsNullOrEmpty(eventName))
                {
                    raisePresentationOpen = eventName == PPTEvents.PresentationOpen;
                    raisePresentationClose = eventName == PPTEvents.PresentationClose;
                    raiseSlideShowBegin = eventName == PPTEvents.SlideShowBegin;
                    raiseSlideShowNext = eventName == PPTEvents.SlideShowNextSlide;
                    raiseSlideShowEnd = eventName == PPTEvents.SlideShowEnd;
                }
                else
                {
                    var hasPresentation = !string.IsNullOrEmpty(state.PresentationName);
                    var hadPresentation = !string.IsNullOrEmpty(oldPresentation);
                    raisePresentationOpen = hasPresentation && (!hadPresentation || !string.Equals(oldPresentation, state.PresentationName, StringComparison.OrdinalIgnoreCase));
                    raisePresentationClose = !hasPresentation && hadPresentation;
                    raiseSlideShowBegin = !oldRunning && state.IsRunning;
                    raiseSlideShowNext = oldRunning && state.IsRunning && oldSlide > 0 && state.SlideIndex > 0 && oldSlide != state.SlideIndex;
                    raiseSlideShowEnd = oldRunning && !state.IsRunning;
                }

                if (oldRunning != state.IsRunning || raiseSlideShowBegin || raiseSlideShowEnd)
                {
                    raiseShowStateChanged = true;
                    newShowState = state.IsRunning;
                }
            }

            if (raiseConnection) PPTConnectionChanged?.Invoke(true);
            if (raisePresentationOpen) PresentationOpen?.Invoke(state);
            if (raisePresentationClose) PresentationClose?.Invoke(state);
            if (raiseShowStateChanged) SlideShowStateChanged?.Invoke(newShowState);
            if (raiseSlideShowBegin) SlideShowBegin?.Invoke(state);
            if (raiseSlideShowNext) SlideShowNextSlide?.Invoke(new PresentationLinkState(true, state.IsRunning, state.SlideIndex, state.TotalSlides));
            if (raiseSlideShowEnd) SlideShowEnd?.Invoke(state);
            if (eventName != PPTEvents.PresentationClose) StateUpdated?.Invoke();
        }

        private PPTState GetStateSnapshot()
        {
            lock (_stateLock)
            {
                return new PPTState
                {
                    SlideIndex = _lastState.SlideIndex,
                    TotalSlides = _lastState.TotalSlides,
                    IsRunning = _lastState.IsRunning,
                    PresentationName = _lastState.PresentationName,
                    PresentationFullName = _lastState.PresentationFullName,
                    HasHiddenSlides = _lastState.HasHiddenSlides,
                    HasAutoPlayTimings = _lastState.HasAutoPlayTimings
                };
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopMonitoring();
        }
    }
}
