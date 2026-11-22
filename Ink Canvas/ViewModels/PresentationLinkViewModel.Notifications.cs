using System;
using System.Threading.Tasks;
using InkCanvasLinkage.Core;

namespace Ink_Canvas.ViewModels
{
    public sealed partial class PresentationLinkViewModel
    {
        public bool AlwaysGoToFirstPageOnReenter { get; set; }
        public bool NotifyPreviousPage { get; set; }
        public bool NotifyHiddenSlides { get; set; }
        public bool NotifyAutoPlayTimings { get; set; }

        public async Task NavigateOnSlideShowBeginAsync(PresentationLinkState state)
        {
            if (AlwaysGoToFirstPageOnReenter && IsCurrentSession(state))
            {
                bool navigated = await NavigateToSlideAsync(1);
                if (IsCurrentSession(state)) _host?.NavigationCompleted(navigated, SkipAnimationsWhenNavigating);
            }
        }

        public async Task NotifyPresentationOpenedAsync(PresentationLinkState state)
        {
            if (_host == null || !IsCurrentSession(state)) return;
            try
            {
                if (AlwaysGoToFirstPageOnReenter) await NavigateOnSlideShowBeginAsync(state);
                else if (NotifyPreviousPage)
                {
                    int page = 0;
                    try { page = await _host.ReadPlaybackPositionAsync(state.Presentation); }
                    catch (Exception ex) { _error?.Invoke(ex); }
                    if (!IsCurrentSession(state)) return;
                    if (page > 0 && page <= state.TotalSlides
                        && await _host.ConfirmAsync(string.Format(Properties.PPTStrings.PPT_RememberLastPage_Prompt, page))
                        && IsCurrentSession(state))
                    {
                        bool navigated = await NavigateToSlideAsync(page);
                        if (IsCurrentSession(state)) _host.NavigationCompleted(navigated, SkipAnimationsWhenNavigating);
                    }
                }
                if (!IsCurrentSession(state)) return;
                if (NotifyHiddenSlides && state.Presentation?.HasHiddenSlides == true
                    && Capabilities.HasFlag(PresentationCapabilities.HiddenSlides)
                    && await _host.ConfirmAsync(Properties.PPTStrings.PPT_HiddenSlides_Detected)
                    && IsCurrentSession(state))
                    await UnhideHiddenSlidesAsync();
                if (!IsCurrentSession(state)) return;
                if (NotifyAutoPlayTimings && !IsInSlideShow && state.Presentation?.HasAutoPlayTimings == true
                    && Capabilities.HasFlag(PresentationCapabilities.AutoPlayTimings)
                    && await _host.ConfirmAsync(Properties.PPTStrings.PPT_AutoPlay_Detected)
                    && IsCurrentSession(state))
                    await DisableAutoPlayTimingsAsync();
            }
            catch (Exception ex) { _error?.Invoke(ex); }
        }
    }
}
