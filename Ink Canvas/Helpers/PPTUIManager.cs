using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// PPT UI管理器 - 统一管理PPT相关的UI更新和样式设置
    /// </summary>
    public class PPTUIManager
    {
        #region Properties
        public int PPTSButtonsOption { get; set; } = 221;
        public int PPTBButtonsOption { get; set; } = 121;
        public int PPTLSButtonPosition { get; set; } = 0;
        public int PPTRSButtonPosition { get; set; } = 0;
        public int PPTLBButtonPosition { get; set; } = 0;
        public int PPTRBButtonPosition { get; set; } = 0;
        public double PPTLSButtonOpacity { get; set; } = 0.5;
        public double PPTRSButtonOpacity { get; set; } = 0.5;
        public double PPTLBButtonOpacity { get; set; } = 0.5;
        public double PPTRBButtonOpacity { get; set; } = 0.5;
        public double PPTNavBarScale { get; set; } = 1.0;
        #endregion

        #region Private Fields
        private readonly MainWindow _mainWindow;
        private readonly Dispatcher _dispatcher;
        #endregion

        #region Constructor
        public PPTUIManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _dispatcher = _mainWindow.Dispatcher;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// 更新PPT连接状态UI
        /// </summary>
        public void UpdateConnectionStatus(bool isConnected)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (isConnected)
                    {
                        _mainWindow.ArePPTControlsVisible = true;
                        // Old UI removed:                         _mainWindow.BtnPPTSlideShow.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _mainWindow.ArePPTControlsVisible = false;
                        // Old UI removed:                         _mainWindow.BtnPPTSlideShow.Visibility = Visibility.Collapsed;
                        _mainWindow.IsInPPTPresentationMode = false;
                        _mainWindow.UpdateToolbarComponentVisibility();
                        RefreshNavigationPanelsVisibility();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新PPT连接状态UI失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 更新幻灯片放映状态UI
        /// </summary>
        public void UpdateSlideShowStatus(bool isInSlideShow, int currentSlide = 0, int totalSlides = 0)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (isInSlideShow)
                    {
                        bool wasInSlideShow = _mainWindow.IsInPPTPresentationMode;

                        // Old UI removed:                         _mainWindow.BtnPPTSlideShow.Visibility = Visibility.Collapsed;
                        _mainWindow.IsInPPTPresentationMode = true;
                        _mainWindow.UpdateToolbarComponentVisibility();

                        UpdateNavigationPanelsVisibility();
                        UpdateNavigationButtonStyles();
                        _mainWindow.UpdatePPTTimeCapsuleVisibility();
                        _mainWindow.UpdatePPTQuickPanelVisibility();
                        if (!wasInSlideShow)
                        {
                            _mainWindow.ShowPPTModePromptNotification();
                        }
                        if (MainWindow.Settings.Advanced.IsEnableAvoidFullScreenHelper)
                        {
                            // 设置为画板模式，允许全屏操作
                            AvoidFullScreenHelper.SetBoardMode(true);
                            _dispatcher.BeginInvoke(new Action(() =>
                            {
                                MainWindow.MoveWindow(new WindowInteropHelper(_mainWindow).Handle, 0, 0,
                                    System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width,
                                    System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height, true);

                                // MoveWindow 触发的 WM_WINDOWPOSCHANGING + 重绘会打断面板的 ShowWithFadeIn 动画，
                                // 在窗口尺寸最终确定后重新评估一次翻页面板的可见性。
                                UpdateNavigationPanelsVisibility();
                                UpdateNavigationButtonStyles();
                            }), DispatcherPriority.ApplicationIdle);

                            _mainWindow.isFullScreenApplied = true; // 标记已应用全屏处理
                        }
                    }
                    else
                    {
                        // Old UI removed:                         _mainWindow.BtnPPTSlideShow.Visibility = Visibility.Visible;
                        _mainWindow.IsInPPTPresentationMode = false;
                        _mainWindow.UpdateToolbarComponentVisibility();
                        RefreshNavigationPanelsVisibility();
                        _mainWindow.UpdatePPTTimeCapsuleVisibility();
                        _mainWindow.UpdatePPTQuickPanelVisibility();
                        if (MainWindow.Settings.Advanced.IsEnableAvoidFullScreenHelper && _mainWindow.currentMode == 0)
                        {
                            // 恢复为非画板模式，重新启用全屏限制
                            AvoidFullScreenHelper.SetBoardMode(false);

                            _dispatcher.BeginInvoke(new Action(() =>
                            {
                                // 退出PPT放映模式，恢复到工作区域大小
                                var workingArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                                MainWindow.MoveWindow(new WindowInteropHelper(_mainWindow).Handle,
                                    workingArea.X, workingArea.Y,
                                    workingArea.Width, workingArea.Height, true);
                            }), DispatcherPriority.ApplicationIdle);

                            _mainWindow.isFullScreenApplied = false; // 标记全屏处理已还原
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新幻灯片放映状态UI失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 处理PPT放映状态变化
        /// </summary>
        public void OnSlideShowStateChanged(bool isInSlideShow)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!isInSlideShow)
                    {
                        // 如果不在放映模式，隐藏所有导航面板
                        RefreshNavigationPanelsVisibility();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"处理PPT放映状态变化失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 更新导航面板显示状态
        /// </summary>
        public void UpdateNavigationPanelsVisibility()
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _mainWindow.ApplyPresentationLinkSettings();

                    // 设置侧边按钮位置
                    _mainWindow.LeftSidePanelForPPTNavigation.Margin = new Thickness(0, 0, 0, PPTLSButtonPosition * 2);
                    _mainWindow.RightSidePanelForPPTNavigation.Margin = new Thickness(0, 0, 0, PPTRSButtonPosition * 2);

                    // 设置底部按钮水平位置
                    _mainWindow.LeftBottomPanelForPPTNavigation.Margin = new Thickness(6 + PPTLBButtonPosition, 0, 0, 6);
                    _mainWindow.RightBottomPanelForPPTNavigation.Margin = new Thickness(0, 0, 6 + PPTRBButtonPosition, 6);

                    // Reapply animation after native window resizing; visibility comes from bindings.
                    RefreshNavigationPanelsVisibility();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新导航面板显示状态失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 更新导航按钮样式
        /// </summary>
        public void UpdateNavigationButtonStyles()
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    UpdateSideButtonStyles();
                    UpdateBottomButtonStyles();
                    ApplyNavBarScale();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新导航按钮样式失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 根据 ViewModel 绑定的状态刷新导航面板
        /// </summary>
        private void RefreshNavigationPanelsVisibility()
        {
            _mainWindow.LeftBottomPanelForPPTNavigation.RefreshNavigationVisibility();
            _mainWindow.RightBottomPanelForPPTNavigation.RefreshNavigationVisibility();
            _mainWindow.LeftSidePanelForPPTNavigation.RefreshNavigationVisibility();
            _mainWindow.RightSidePanelForPPTNavigation.RefreshNavigationVisibility();
        }

        /// <summary>
        /// 显示/隐藏侧边栏退出按钮
        /// </summary>
        public void UpdateSidebarExitButtons(bool show)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var visibility = show ? Visibility.Visible : Visibility.Collapsed;

                    if (_mainWindow.BtnExitPPTFromSidebarLeft != null)
                        _mainWindow.BtnExitPPTFromSidebarLeft.Visibility = visibility;

                    if (_mainWindow.BtnExitPPTFromSidebarRight != null)
                        _mainWindow.BtnExitPPTFromSidebarRight.Visibility = visibility;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新侧边栏退出按钮失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 设置浮动栏透明度
        /// </summary>
        public void SetFloatingBarOpacity(double opacity)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _mainWindow.ViewboxFloatingBar.Opacity = opacity;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"设置浮动栏透明度失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 设置主面板边距
        /// </summary>
        public void SetMainPanelMargin(Thickness margin)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    // Old UI removed:                     _mainWindow.ViewBoxStackPanelMain.Margin = margin;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"设置主面板边距失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }
        #endregion

        #region Private Methods
        private void UpdateSideButtonStyles()
        {
            try
            {
                var sideOption = PPTSButtonsOption.ToString();
                if (sideOption.Length < 3) return;

                var options = sideOption.ToCharArray();

                // 页码按钮显示
                var pageButtonVisibility = options[0] == '2' ? Visibility.Visible : Visibility.Collapsed;
                _mainWindow.LeftSidePanelForPPTNavigation.SetPageButtonVisibility(pageButtonVisibility);
                _mainWindow.RightSidePanelForPPTNavigation.SetPageButtonVisibility(pageButtonVisibility);

                // 透明度
                _mainWindow.LeftSidePanelForPPTNavigation.SetBarOpacity(PPTLSButtonOpacity);
                _mainWindow.RightSidePanelForPPTNavigation.SetBarOpacity(PPTRSButtonOpacity);

                // 颜色主题
                bool isDarkTheme = options[2] == '2';
                _mainWindow.LeftSidePanelForPPTNavigation.ApplyTheme(isDarkTheme);
                _mainWindow.RightSidePanelForPPTNavigation.ApplyTheme(isDarkTheme);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"更新侧边按钮样式失败: {ex}", LogHelper.LogType.Error);
            }
        }

        private void UpdateBottomButtonStyles()
        {
            try
            {
                var bottomOption = PPTBButtonsOption.ToString();
                if (bottomOption.Length < 3) return;

                var options = bottomOption.ToCharArray();

                // 页码按钮显示
                var pageButtonVisibility = options[0] == '2' ? Visibility.Visible : Visibility.Collapsed;
                _mainWindow.LeftBottomPanelForPPTNavigation.SetPageButtonVisibility(pageButtonVisibility);
                _mainWindow.RightBottomPanelForPPTNavigation.SetPageButtonVisibility(pageButtonVisibility);

                // 透明度
                _mainWindow.LeftBottomPanelForPPTNavigation.SetBarOpacity(PPTLBButtonOpacity);
                _mainWindow.RightBottomPanelForPPTNavigation.SetBarOpacity(PPTRBButtonOpacity);

                // 颜色主题
                bool isDarkTheme = options[2] == '2';
                _mainWindow.LeftBottomPanelForPPTNavigation.ApplyTheme(isDarkTheme);
                _mainWindow.RightBottomPanelForPPTNavigation.ApplyTheme(isDarkTheme);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"更新底部按钮样式失败: {ex}", LogHelper.LogType.Error);
            }
        }

        private void ApplyNavBarScale()
        {
            try
            {
                _mainWindow.LeftBottomPanelForPPTNavigation.SetBarScale(PPTNavBarScale);
                _mainWindow.RightBottomPanelForPPTNavigation.SetBarScale(PPTNavBarScale);
                _mainWindow.LeftSidePanelForPPTNavigation.SetBarScale(PPTNavBarScale);
                _mainWindow.RightSidePanelForPPTNavigation.SetBarScale(PPTNavBarScale);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用翻页按钮缩放失败: {ex}", LogHelper.LogType.Error);
            }
        }
        #endregion
    }
}
