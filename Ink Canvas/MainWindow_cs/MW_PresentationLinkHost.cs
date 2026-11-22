using Ink_Canvas.ViewModels;
using InkCanvasLinkage.Core;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Ink_Canvas
{
    public partial class MainWindow
    {
        internal void ApplyPresentationLinkSettings()
        {
            if (PresentationLink == null) return;
            var settings = Settings.PowerPointSettings;
            PresentationLink.PageNavigationEnabled = settings.EnablePPTButtonPageClickable;
            PresentationLink.EnhancedPreviewEnabled = settings.EnablePPTButtonEnhancedPreview;
            PresentationLink.ShowNavigationButtons = settings.ShowPPTButton;
            PresentationLink.NavigationDisplayOption = settings.PPTButtonsDisplayOption;
            PresentationLink.AutoFoldInSlideShow = Settings.Automation.IsAutoFoldInPPTSlideShow;
            PresentationLink.NavigationSuppressed = currentMode != 0 || isFloatingBarFolded;
            PresentationLink.AlwaysGoToFirstPageOnReenter = settings.IsAlwaysGoToFirstPageOnReenter;
            PresentationLink.NotifyPreviousPage = settings.IsNotifyPreviousPage;
            PresentationLink.NotifyHiddenSlides = settings.IsNotifyHiddenPage;
            PresentationLink.NotifyAutoPlayTimings = settings.IsNotifyAutoPlayPresentation;
        }

        private sealed class PresentationLinkHost : IPresentationLinkHost
        {
            private readonly MainWindow _window;
            public PresentationLinkHost(MainWindow window) => _window = window;

            public void BeforeNavigate(PresentationLinkState state)
            {
                if (_window._currentSlideShowPosition == state.CurrentSlide
                    && (_window.inkCanvas?.Strokes?.Count ?? 0) > Settings.Automation.MinimumAutomationStrokeNumber
                    && Settings.PowerPointSettings.IsAutoSaveScreenShotInPowerPoint && state.CurrentSlide > 0)
                    _window.CaptureAndEnqueueScreenshotSave(true, $"{state.Presentation?.Name}/{state.CurrentSlide}");
            }

            public void NavigationCompleted(bool succeeded, bool skipAnimations)
            {
                if (succeeded && skipAnimations)
                    Helpers.ExceptionHandler.TryExecute(() => _window.Activate(), "激活主窗口失败（PPT 翻页时）");
                if (!succeeded) _window._pptUIManager?.UpdateConnectionStatus(_window.PresentationLink.IsConnected);
            }

            public async Task BeforeEndSlideShowAsync()
            {
                _window.CursorIcon_Click(null, null);
                await Task.Delay(100);
            }

            public void PrepareSlideNavigation()
            {
                _window.GridTransparencyFakeBackground.Opacity = 1;
                _window.GridTransparencyFakeBackground.Background = new SolidColorBrush(_window.StringToColor("#01FFFFFF"));
                _window.SetTransparentNotHitThrough();
                _window.CursorIcon_Click(null, null);
            }

            public async Task SlideNavigationCompletedAsync(bool activate)
            {
                if (activate) Helpers.ExceptionHandler.TryExecute(() => _window.Activate(), "激活主窗口失败（PPT 导航时）");
                int generation = _window.PresentationLink.Generation;
                var state = _window.PresentationLink.State;
                if (!_window.isFloatingBarFolded)
                {
                    await Task.Delay(100);
                    if (_window.PresentationLink.IsCurrentSession(state, generation, true)) _window.ViewboxFloatingBarMarginAnimation(60);
                }
            }

            public Task<int> ReadPlaybackPositionAsync(PresentationMetadata presentation)
            {
                var path = Path.Combine(_window.GetPresentationStrokeFolderPath(presentation), "Position");
                return Task.Run(() => File.Exists(path) && int.TryParse(File.ReadAllText(path), out var page) ? page : 0);
            }

            public Task<bool> ConfirmAsync(string content) => _window.ShowInlineYesNoDialog("Ink Canvas For Class CE", content);

            public void ConnectionCheckCompleted(bool isConnected)
            {
                if (!isConnected) MessageBox.Show(Properties.PPTStrings.PPT_SlidesNotFound);
                Helpers.LogHelper.WriteLogToFile(isConnected ? "手动PPT连接检查成功" : "手动PPT连接检查失败",
                    isConnected ? Helpers.LogHelper.LogType.Event : Helpers.LogHelper.LogType.Warning);
            }
        }
    }
}
