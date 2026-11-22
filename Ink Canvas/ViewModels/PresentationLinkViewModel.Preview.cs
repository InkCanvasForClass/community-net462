using InkCanvasLinkage.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Ink_Canvas.ViewModels
{
    public sealed partial class PresentationLinkViewModel
    {
        private CancellationTokenSource _previewCts;
        private Task<IList<PresentationPreviewItem>> _previewTask;
        private Guid _previewPresentationId;
        private int _previewTotalSlides;
        private int? _expandedPreviewPosition;
        private bool _pageNavigationEnabled = true;
        private bool _enhancedPreviewEnabled;
        private bool _showNavigationButtons = true;
        private bool _autoFoldInSlideShow;
        private bool _navigationSuppressed;
        private int _navigationDisplayOption = 2222;

        public bool PageNavigationEnabled
        {
            get => _pageNavigationEnabled;
            set { if (SetProperty(ref _pageNavigationEnabled, value)) ShowNavigationCommand.NotifyCanExecuteChanged(); }
        }
        public bool EnhancedPreviewEnabled
        {
            get => _enhancedPreviewEnabled;
            set { if (SetProperty(ref _enhancedPreviewEnabled, value)) ResetPreviewCache(); }
        }
        public bool ShowNavigationButtons
        {
            get => _showNavigationButtons;
            set { if (SetProperty(ref _showNavigationButtons, value)) NotifyNavigationVisibility(); }
        }
        public bool AutoFoldInSlideShow
        {
            get => _autoFoldInSlideShow;
            set { if (SetProperty(ref _autoFoldInSlideShow, value)) NotifyNavigationVisibility(); }
        }
        public int NavigationDisplayOption
        {
            get => _navigationDisplayOption;
            set { if (SetProperty(ref _navigationDisplayOption, value)) NotifyNavigationVisibility(); }
        }
        public bool NavigationSuppressed
        {
            get => _navigationSuppressed;
            set { if (SetProperty(ref _navigationSuppressed, value)) NotifyNavigationVisibility(); }
        }
        public bool AreNavigationControlsVisible => ShowNavigationButtons && !AutoFoldInSlideShow && !NavigationSuppressed
            && IsConnected && IsInSlideShow && TotalSlides > 0;
        public bool IsLeftBottomNavigationVisible => IsNavigationPositionVisible(0);
        public bool IsRightBottomNavigationVisible => IsNavigationPositionVisible(1);
        public bool IsLeftSideNavigationVisible => IsNavigationPositionVisible(2) && !IsLeftBottomPreviewExpanded;
        public bool IsRightSideNavigationVisible => IsNavigationPositionVisible(3) && !IsRightBottomPreviewExpanded;
        private bool IsNavigationPositionVisible(int position)
        {
            string option = NavigationDisplayOption.ToString();
            return AreNavigationControlsVisible && option.Length >= 4 && option[position] == '2';
        }
        private void NotifyNavigationVisibility()
        {
            OnPropertyChanged(nameof(AreNavigationControlsVisible));
            OnPropertyChanged(nameof(IsLeftBottomNavigationVisible)); OnPropertyChanged(nameof(IsRightBottomNavigationVisible));
            OnPropertyChanged(nameof(IsLeftSideNavigationVisible)); OnPropertyChanged(nameof(IsRightSideNavigationVisible));
        }
        public int? ExpandedPreviewPosition
        {
            get => _expandedPreviewPosition;
            private set
            {
                if (!SetProperty(ref _expandedPreviewPosition, value)) return;
                OnPropertyChanged(nameof(IsLeftBottomPreviewExpanded)); OnPropertyChanged(nameof(IsRightBottomPreviewExpanded));
                OnPropertyChanged(nameof(IsLeftSidePreviewExpanded)); OnPropertyChanged(nameof(IsRightSidePreviewExpanded));
                NotifyNavigationVisibility();
            }
        }
        public bool IsLeftBottomPreviewExpanded { get => ExpandedPreviewPosition == 0; set => SetPreviewExpanded(0, value); }
        public bool IsRightBottomPreviewExpanded { get => ExpandedPreviewPosition == 1; set => SetPreviewExpanded(1, value); }
        public bool IsLeftSidePreviewExpanded { get => ExpandedPreviewPosition == 2; set => SetPreviewExpanded(2, value); }
        public bool IsRightSidePreviewExpanded { get => ExpandedPreviewPosition == 3; set => SetPreviewExpanded(3, value); }
        private void SetPreviewExpanded(int position, bool value)
        {
            if (value) ExpandedPreviewPosition = position;
            else if (ExpandedPreviewPosition == position) ExpandedPreviewPosition = null;
        }
        public void CollapsePreview() { ExpandedPreviewPosition = null; PreviewItems = null; }

        public void ResetPreviewCache()
        {
            CancellationTokenSource old;
            lock (_gate) { old = _previewCts; _previewCts = null; _previewTask = null; }
            old?.Cancel();
            old?.Dispose();
            CollapsePreview();
        }

        public async Task PreloadPreviewAsync()
        {
            if (!EnhancedPreviewEnabled || !IsConnected) return;
            try
            {
                var token = PreviewToken();
                await Task.Delay(100, token);
                await LoadPreviewItemsAsync(token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _error?.Invoke(ex); }
        }

        private CancellationToken PreviewToken()
        {
            lock (_gate)
            {
                var state = _source?.State;
                // Source can switch documents before its queued UI notification is applied.
                if (_previewCts != null && (_previewPresentationId != state?.PresentationSessionId || _previewTotalSlides != state?.TotalSlides))
                {
                    _previewCts.Cancel(); _previewCts.Dispose(); _previewCts = null; _previewTask = null;
                }
                if (_previewCts == null) _previewCts = new CancellationTokenSource();
                _previewPresentationId = state?.PresentationSessionId ?? Guid.Empty;
                _previewTotalSlides = state?.TotalSlides ?? 0;
                return _previewCts.Token;
            }
        }

        private Task<IList<PresentationPreviewItem>> LoadPreviewItemsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_previewTask != null && !_previewTask.IsCanceled && !_previewTask.IsFaulted
                    && (!_previewTask.IsCompleted || _previewTask.Result.Count > 0)) return _previewTask;
                var source = _source;
                _previewTask = Task.Run<IList<PresentationPreviewItem>>(() =>
                {
                    var result = new List<PresentationPreviewItem>();
                    token.ThrowIfCancellationRequested();
                    var thumbnails = source?.Capabilities.HasFlag(PresentationCapabilities.Thumbnails) == true
                        ? source.ExportSlideThumbnails(480, 270) : null;
                    if (thumbnails == null) return result;
                    foreach (var thumbnail in thumbnails)
                    {
                        token.ThrowIfCancellationRequested();
                        var bytes = thumbnail?.PngBytes;
                        if (bytes == null || bytes.Length == 0) continue;
                        try
                        {
                            using (var stream = new MemoryStream(bytes, false))
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                                result.Add(new PresentationPreviewItem { SlideNumber = thumbnail.SlideNumber, Thumbnail = bitmap });
                            }
                        }
                        catch (Exception ex) { _error?.Invoke(ex); }
                    }
                    token.ThrowIfCancellationRequested();
                    return result;
                }, token);
                return _previewTask;
            }
        }

        private async Task ShowNavigationAsync(int position)
        {
            if (!PageNavigationEnabled || !IsConnected || !IsInSlideShow) return;
            var state = _source.State;
            int generation = Generation;
            try
            {
                _host?.PrepareSlideNavigation();
                int? target = ResolvePreviewPosition(position);
                bool activate = false;
                if (EnhancedPreviewEnabled && target.HasValue)
                {
                    if (ExpandedPreviewPosition == target) ExpandedPreviewPosition = null;
                    else
                    {
                        var token = PreviewToken();
                        IList<PresentationPreviewItem> items;
                        try { items = await LoadPreviewItemsAsync(token); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { _error?.Invoke(ex); items = Array.Empty<PresentationPreviewItem>(); }
                        if (token.IsCancellationRequested || !IsCurrentSession(state, generation, true)) return;
                        if (items.Count > 0) { PreviewItems = items; ExpandedPreviewPosition = target; }
                        else activate = await RunSourceAsync(source => source.TryShowSlideNavigation(), true);
                    }
                }
                else activate = await RunSourceAsync(source => source.TryShowSlideNavigation(), true);
                if (IsCurrentSession(state, generation, true) && _host != null)
                    await _host.SlideNavigationCompletedAsync(activate && SkipAnimationsWhenNavigating);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _error?.Invoke(ex); }
        }

        private int? ResolvePreviewPosition(int position)
        {
            if (position < 0 || position > 3) return null;
            if (position == 2)
                return IsLeftBottomNavigationVisible ? 0 : IsRightBottomNavigationVisible ? 1 : position;
            if (position == 3)
                return IsRightBottomNavigationVisible ? 1 : IsLeftBottomNavigationVisible ? 0 : position;
            return position;
        }
    }
}
