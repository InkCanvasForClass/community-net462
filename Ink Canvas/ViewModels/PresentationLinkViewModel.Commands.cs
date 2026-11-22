using InkCanvasLinkage.Core;
using System;
using System.Threading.Tasks;

namespace Ink_Canvas.ViewModels
{
    public sealed partial class PresentationLinkViewModel
    {
        public void InitializeSource(PresentationLinkMode mode, PresentationLinkOptions options = null, IPresentationLinkSource source = null)
        {
            if (source != null && ReferenceEquals(source, _source)) return;
            ReleaseSource();
            var next = source ?? PresentationLinkSourceFactory.Create(mode, options);
            try
            {
                if (options != null)
                {
                    next.IsSupportWPS = options.SupportWps;
                    next.SkipAnimationsWhenNavigating = options.SkipAnimations;
                }
                Attach(next);
                _ownsSource = true;
            }
            catch
            {
                try { if (ReferenceEquals(next, _source)) Attach(null); }
                finally { next.Dispose(); }
                throw;
            }
        }

        public void ReleaseSource(bool isShutdown = false)
        {
            var old = _source;
            _ownsSource = false;
            try { Attach(null); }
            finally
            {
                // A failed stop must not skip disposal of COM/IPC resources.
                try { old?.StopMonitoring(isShutdown); }
                catch (Exception ex) { _error?.Invoke(ex); }
                finally
                {
                    try { old?.Dispose(); }
                    catch (Exception ex) { _error?.Invoke(ex); }
                }
            }
        }

        public bool IsSupportWPS
        {
            get => _source?.IsSupportWPS == true;
            set { if (_source != null) _source.IsSupportWPS = value; }
        }
        private bool _skipAnimationsWhenNavigating;
        public bool SkipAnimationsWhenNavigating
        {
            get => _skipAnimationsWhenNavigating;
            set
            {
                if (_source != null) _source.SkipAnimationsWhenNavigating = value;
                _skipAnimationsWhenNavigating = value;
            }
        }

        public void StartMonitoring() => _source?.StartMonitoring();
        public void StopMonitoring(bool isShutdown = false) => _source?.StopMonitoring(isShutdown);
        public bool SetEnhancedMonitoring(bool enabled, bool isShutdown = false) =>
            _source?.SetEnhancedMonitoring(enabled, isShutdown) == true;
        public PresentationSmartRegions GetSmartRegions() =>
            Capabilities.HasFlag(PresentationCapabilities.SmartRegions) ? _source.GetSmartRegions() : new PresentationSmartRegions();
        public Task<bool> UnhideHiddenSlidesAsync() => Capabilities.HasFlag(PresentationCapabilities.HiddenSlides)
            ? RunSourceAsync(source => source.TryUnhideHiddenSlides()) : Task.FromResult(false);
        public Task<bool> DisableAutoPlayTimingsAsync() => Capabilities.HasFlag(PresentationCapabilities.AutoPlayTimings)
            ? RunSourceAsync(source => source.TryDisableAutoPlayTimings()) : Task.FromResult(false);

        public bool IsCurrentSession(PresentationLinkState state, int? generation = null, bool includeSlideShow = false) =>
            state != null && IsCurrentOperation && (!generation.HasValue || generation == Generation)
            && state.PresentationSessionId != Guid.Empty && _source?.State.PresentationSessionId == state.PresentationSessionId
            && (!includeSlideShow || _source.State.SlideShowSessionId == state.SlideShowSessionId);

        public Task<bool> NavigateToSlideAsync(int page) => page > 0 && page <= (_source?.State.TotalSlides ?? 0)
            ? RunSourceAsync(source => source.TryNavigateToSlide(page)) : Task.FromResult(false);

        private async Task NavigateRelativeAsync(bool next)
        {
            await RunSourceAsync(source => next ? source.TryNavigateNext() : source.TryNavigatePrevious(), true,
                state => { _host?.BeforeNavigate(state); return Task.CompletedTask; },
                succeeded => _host?.NavigationCompleted(succeeded, SkipAnimationsWhenNavigating));
        }

        private async Task ReloadAsync()
        {
            var source = _source;
            int generation = Generation;
            if (source == null) return;
            try
            {
                await _commandGate.WaitAsync();
                try
                {
                    await Task.Run(() =>
                    {
                        if (generation != Generation || !ReferenceEquals(source, _source)) return;
                        source.ReloadConnection();
                        source.StartMonitoring();
                    });
                }
                finally { _commandGate.Release(); }
                if (_host != null)
                {
                    await Task.Delay(800);
                    if (generation == Generation && ReferenceEquals(source, _source))
                        _host.ConnectionCheckCompleted(IsConnected);
                }
            }
            catch (Exception ex) { _error?.Invoke(ex); }
        }

        private async Task<bool> RunSourceAsync(Func<IPresentationLinkSource, bool> operation, bool requireSlideShow = false,
            Func<PresentationLinkState, Task> before = null, Action<bool> completed = null)
        {
            var source = _source;
            var state = source?.State;
            int generation = Generation;
            if (state?.IsConnected != true || (requireSlideShow && !state.IsInSlideShow)) return false;
            try
            {
                await _commandGate.WaitAsync();
                try
                {
                    if (!IsCurrentSession(state, generation, true) || !ReferenceEquals(source, _source)
                        || (requireSlideShow && !source.State.IsInSlideShow)) return false;
                    state = source.State;
                    if (before != null) await before(state);
                    bool result = await Task.Run(() => IsCurrentSession(state, generation, true)
                        && ReferenceEquals(source, _source) && (!requireSlideShow || source.State.IsInSlideShow) && operation(source));
                    if (generation != Generation || !ReferenceEquals(source, _source)) return false;
                    if (source.State.PresentationSessionId != state.PresentationSessionId) return false;
                    if (source.State.SlideShowSessionId == state.SlideShowSessionId) completed?.Invoke(result);
                    if (!result) _error?.Invoke(new InvalidOperationException("Presentation operation failed."));
                    return result;
                }
                finally { _commandGate.Release(); }
            }
            catch (Exception ex) { _error?.Invoke(ex); return false; }
        }
    }
}
