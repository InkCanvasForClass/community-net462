using Ink_Canvas.Helpers;
using Ink_Canvas.WorkflowAutomation;
using InkCanvasLinkage.Core;
using Ink_Canvas.ViewModels;
using iNKORE.UI.WPF.Modern;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using File = System.IO.File;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        #region Win32 API Declarations
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out ForegroundWindowInfo.RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private const int GWL_STYLE = -16;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_MINIMIZE = 0x20000000;
        private const uint GW_HWNDNEXT = 2;
        private const uint GW_HWNDPREV = 3;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        #endregion

        #region PPT State Management
        /// <summary>
        /// 幻灯片放映结束事件重入保护标志，防止重复处理放映结束事件。
        /// </summary>
        private bool isEnteredSlideShowEndEvent;

        /// <summary>
        /// 演示文稿是否有黑边的指示标志。
        /// </summary>
        private bool isPresentationHaveBlackSpace;

        // 长按翻页相关字段
        /// <summary>
        /// 用于处理长按翻页功能的定时器。
        /// </summary>
        private DispatcherTimer _longPressTimer;

        /// <summary>
        /// 长按翻页方向标志，true表示下一页，false表示上一页。
        /// </summary>
        private bool _isLongPressNext = true; // true为下一页，false为上一页

        /// <summary>
        /// 长按延迟时间（毫秒），即用户需要按住按钮多长时间才开始连续翻页。
        /// </summary>
        private const int LongPressDelay = 500; // 长按延迟时间（毫秒）

        /// <summary>
        /// 长按翻页间隔（毫秒），即连续翻页的时间间隔。
        /// </summary>
        private const int LongPressInterval = 50; // 长按翻页间隔（毫秒）

        // 当前播放页码跟踪
        /// <summary>
        /// 当前幻灯片放映的位置（页码）。
        /// </summary>
        private int _currentSlideShowPosition = 0;

        /// <summary>
        /// 当前幻灯片放映位置的公开访问器（0-based）。
        /// 用于小白板等组件获取当前PPT页码。
        /// </summary>
        internal int CurrentPPTSlideIndex => _currentSlideShowPosition > 0 ? _currentSlideShowPosition - 1 : 0;

        private int _previousSlideID = 0;

        /// <summary>
        /// 用于在PowerPoint连接断开后延迟退出PPT模式的定时器。
        /// </summary>
        private DispatcherTimer _exitPPTModeAfterDisconnectTimer;

        /// <summary>
        /// 断开连接后退出PPT模式的延迟时间（毫秒），即连接断开后多长时间才退出PPT模式。
        /// </summary>
        private const int ExitPPTModeAfterDisconnectDelayMs = 1200;

        /// <summary>
        /// 仅PPT模式下周期性探测放映界面（COM 失效时依赖 Win32），间隔不宜过小以免多余开销。
        /// </summary>
        private DispatcherTimer _pptOnlyVisibilityProbeTimer;

        private const int PPTOnlyVisibilityProbeIntervalMs = 800;

        /// <summary>
        /// PowerPoint 全屏放映顶层窗口类名（与编辑态 PPTFrameClass 区分）。
        /// </summary>
        private const string PowerPointSlideShowWindowClassName = "screenClass";

        // 智慧模式：视频控件穿透区域
        /// <summary>当前幻灯片的视频控件原始区域列表（磅值），用于鼠标进入/离开判断。</summary>
        private System.Collections.Generic.IReadOnlyList<PresentationSmartRegion> _smartModeRegions;
        /// <summary>缓存的视频区域对应的幻灯片页码，避免重复查询。</summary>
        private int _smartModeSlideIndex = -1;
        /// <summary>VSTO/COM 返回的幻灯片尺寸（磅）和放映窗口句柄，用于主应用端坐标转换。</summary>
        private float _smartModeSlideWidth, _smartModeSlideHeight;
        private IntPtr _smartModeSlideShowHwnd;

        #endregion

        #region PPT Managers
        /// <summary>
        /// PPT链接管理器，用于管理与PowerPoint的连接和事件处理。
        /// </summary>
        public PresentationLinkViewModel PresentationLink { get; private set; }
        private Guid _inkPresentationSessionId;

        /// <summary>
        /// PPT墨迹管理器，用于管理PowerPoint幻灯片上的墨迹。
        /// </summary>
        private PPTInkManager _singlePPTInkManager;

        /// <summary>
        /// PPT UI管理器，用于管理与PowerPoint相关的用户界面元素。
        /// </summary>
        private PPTUIManager _pptUIManager;

        public PPTUIManager PPTUIManager => _pptUIManager;
        #endregion

        #region PPT Manager Initialization
        private void InitializePresentationLinkViewModel()
        {
            if (PresentationLink != null) return;
            PresentationLink = new PresentationLinkViewModel(Dispatcher,
                ex => LogHelper.WriteLogToFile($"PPT 宿主联动失败: {ex}", LogHelper.LogType.Error),
                new PresentationLinkHost(this));
            PresentationLink.ConnectionChanged += OnPPTConnectionChanged;
            PresentationLink.SlideShowStarted += OnPPTSlideShowBegin;
            PresentationLink.SlideChanged += OnPPTSlideShowNextSlide;
            PresentationLink.SlideShowEnded += OnPPTSlideShowEnd;
            PresentationLink.PresentationOpened += OnPPTPresentationOpen;
            PresentationLink.PresentationClosed += OnPPTPresentationClose;
            PresentationLink.SlideShowStateChanged += OnPPTSlideShowStateChanged;
            PresentationLink.PropertyChanged += OnPPTLinkPropertyChanged;
        }

        /// <summary>
        /// 初始化并配置用于 PowerPoint 集成的管理器与相关状态。
        /// </summary>
        /// <remarks>
        /// 清理并释放现有的 PPT 管理器与 COM/Interop 状态，创建并配置新的 PPT 管理器（ROT 或 COM 实现，取决于设置）、单一的 PPT 墨迹管理器及其自动保存行为，以及 PPT UI 管理器与其显示/按钮位置选项。方法内部会订阅必要的 PPT 事件并记录初始化过程中的错误或警告。同时初始化长按页翻页定时器以支持长按翻页功能。
        /// </remarks>
        /// <param name="source">可选的联动来源（包括 Fake）；ViewModel 负责停止和释放。</param>
        public void InitializePPTManagers(IPresentationLinkSource source = null)
        {
            if (source != null && ReferenceEquals(source, PresentationLink?.Source)) return;
            try
            {
                // 初始化长按定时器
                _longPressTimer?.Stop();
                InitializeLongPressTimer();
                InitializePresentationLinkViewModel();
                WirePPTNavBars();

                // 完全清理旧模式
                try
                {
                    SaveCurrentPPTInk();
                    try
                    {
                        // Detaching drops the old source's end events, so restore its UI explicitly.
                        if (IsInPPTPresentationMode || PresentationLink.IsInSlideShow)
                        {
                            IsInPPTPresentationMode = false;
                            _smartModeRegions = null;
                            _smartModeSlideIndex = -1;
                            _mediaPassthroughRects?.Clear();
                            StopMediaPassthroughTimer();
                            _isMediaRegionMouseMode = false;
                            CollapseAllPPTNavBarPreviews();
                            ResetPPTEnhancedPreviewCache();
                            _pptUIManager?.UpdateSlideShowStatus(false);
                            _pptUIManager?.UpdateSidebarExitButtons(false);
                            _pptUIManager?.SetFloatingBarOpacity(Settings.Appearance.ViewboxFloatingBarOpacityValue);
                            ApplyFloatingBarMenuOpacity();
                            if (currentMode == 0)
                            {
                                ClearStrokes(true);
                                TimeMachineHistories[0] = null;
                                if (GridTransparencyFakeBackground.Background != Brushes.Transparent)
                                    BtnHideInkCanvas_Click(null, null);
                                SetCurrentToolMode(InkCanvasEditingMode.None);
                                UpdateCurrentToolMode("cursor");
                                SetFloatingBarHighlightPosition("cursor");
                            }
                            UpdateToolbarComponentVisibility();
                            CheckMainWindowVisibility();
                        }
                        _pptUIManager?.UpdateConnectionStatus(false);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"恢复旧 PPT 界面失败: {ex}", LogHelper.LogType.Warning);
                    }
                    _inkPresentationSessionId = Guid.Empty;
                    try { CancelPresentationNotifications(); }
                    finally { PresentationLink.ReleaseSource(); }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"清理旧 PPT 管理器异常: {ex}", LogHelper.LogType.Warning);
                }

                try
                {
                    ResetPPTStateVariables();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"清理 Interop 状态异常: {ex}", LogHelper.LogType.Warning);
                }

                if (source == null && Settings.PowerPointSettings.PPTLinkMode == PPTLinkMode.Agent)
                    VstoRegistrationHelper.EnsureRegistered();
                PresentationLink.InitializeSource(
                    (PresentationLinkMode)Settings.PowerPointSettings.PPTLinkMode,
                    new PresentationLinkOptions
                    {
                        SupportWps = Settings.PowerPointSettings.IsSupportWPS,
                        SkipAnimations = Settings.PowerPointSettings.SkipAnimationsWhenGoNext,
                        DebugProbeEnabled = Settings.Advanced.IsPPTComDebugProbeEnabled,
                        Log = (message, level) => LogHelper.WriteLogToFile(message,
                            level == PresentationLogLevel.Error ? LogHelper.LogType.Error :
                            level == PresentationLogLevel.Warning ? LogHelper.LogType.Warning : LogHelper.LogType.Trace)
                    }, source);
                ApplyPresentationLinkSettings();

                _singlePPTInkManager?.Dispose();
                _singlePPTInkManager = new PPTInkManager();
                _singlePPTInkManager.IsAutoSaveEnabled = Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint;
                _singlePPTInkManager.AutoSaveLocation = Settings.Automation.AutoSavedStrokesLocation;

                // 初始化UI管理器
                _pptUIManager = new PPTUIManager(this);
                _pptUIManager.PPTSButtonsOption = Settings.PowerPointSettings.PPTSButtonsOption;
                _pptUIManager.PPTBButtonsOption = Settings.PowerPointSettings.PPTBButtonsOption;
                _pptUIManager.PPTLSButtonPosition = Settings.PowerPointSettings.PPTLSButtonPosition;
                _pptUIManager.PPTRSButtonPosition = Settings.PowerPointSettings.PPTRSButtonPosition;
                _pptUIManager.PPTLBButtonPosition = Settings.PowerPointSettings.PPTLBButtonPosition;
                _pptUIManager.PPTRBButtonPosition = Settings.PowerPointSettings.PPTRBButtonPosition;

                LogHelper.WriteLogToFile("PPT管理器初始化完成", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PPT管理器初始化失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 启动PPT监控：当PowerPoint支持功能启用时，启动PPT管理器的监控功能。
        /// </summary>
        /// <remarks>
        /// 只有当Settings.PowerPointSettings.PowerPointSupport为true时才会启动监控，并记录启动事件日志。
        /// </remarks>
        public void StartPPTMonitoring()
        {
            if (Settings.PowerPointSettings.PowerPointSupport)
            {
                PresentationLink?.StartMonitoring();
                LogHelper.WriteLogToFile("PPT监控已启动", LogHelper.LogType.Event);
            }
        }

        /// <summary>
        /// 停止 PowerPoint 相关的监控：停止并清除用于延迟退出 PPT 模式的定时器，并停止 PPT 管理器的监控，同时记录事件日志。
        /// </summary>
        public void StopPPTMonitoring()
        {
            SaveCurrentPPTInk();
            try
            {
                _exitPPTModeAfterDisconnectTimer?.Stop();
                _exitPPTModeAfterDisconnectTimer = null;
                ResetPPTEnhancedPreviewCache();
            }
            catch
            {
            }

            PresentationLink?.StopMonitoring();
            LogHelper.WriteLogToFile("PPT监控已停止", LogHelper.LogType.Event);
        }

        #region PowerPoint Application Management
        /// <summary>
        /// 启动PowerPoint应用程序守护
        /// </summary>
        /// <remarks>
        /// 启动对本地 PowerPoint 应用实例的守护监控并在需要时创建应用程序实例。
        /// 仅在 PowerPoint 增强功能已启用且未使用 ROT 链接时生效；方法将创建 PowerPoint 应用（若不存在）并启动用于定期检查应用状态的定时器。
        /// </remarks>
        public void StartPowerPointProcessMonitoring()
        {
            if (Settings.PowerPointSettings.EnablePowerPointEnhancement)
                PresentationLink?.SetEnhancedMonitoring(true);
        }

        /// <summary>
        /// 停止PowerPoint应用程序守护
        /// </summary>
        public void StopPowerPointProcessMonitoring(bool isShutdown = false)
        {
            PresentationLink?.SetEnhancedMonitoring(false, isShutdown);
        }
        #endregion

        /// <summary>
        /// 释放并停止所有与 PowerPoint 集成相关的管理器与资源，恢复和清理应用的 PPT 相关运行状态。
        /// </summary>
        /// <remarks>
        /// 操作包括停止并释放 PPT 管理器、墨迹管理器和长按计时器，停止 PowerPoint 进程监控，关闭 PowerPoint 应用并清除静态 COM/互操作状态；所有异常会被捕获并记录为错误日志。
        /// </remarks>
        private void DisposePPTManagers(bool isShutdown = false)
        {
            try
            {
                if (PresentationLink?.HasSource == true)
                {
                    SaveCurrentPPTInk();
                    try { CancelPresentationNotifications(); }
                    finally { PresentationLink.ReleaseSource(isShutdown); }
                }

                _singlePPTInkManager?.Dispose();
                _singlePPTInkManager = null;

                _longPressTimer?.Stop();
                _longPressTimer = null;

                _pptUIManager = null;

                StopPowerPointProcessMonitoring(isShutdown);

                StopPPTOnlyVisibilityProbeTimer();

                LogHelper.WriteLogToFile("PPT管理器已释放", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"释放PPT管理器失败: {ex}", LogHelper.LogType.Error);
            }
        }

        internal void UnloadPPTModuleForShutdown()
        {
            try
            {
                try
                {
                    _longPressTimer?.Stop();
                    StopPPTOnlyVisibilityProbeTimer();
                    LogHelper.WriteLogToFile("关机时已停止所有 PPT 相关定时器", LogHelper.LogType.Event);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"关机时停止定时器失败: {ex}", LogHelper.LogType.Warning);
                }

                // 再处理需要 dispatcher 或 COM 的清理操作
                if (Dispatcher == null || Dispatcher.CheckAccess())
                {
                    DisposePPTManagers(isShutdown: true);
                    return;
                }

                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    LogHelper.WriteLogToFile("关机时 Dispatcher 已关闭，跳过需要 UI 线程的清理操作", LogHelper.LogType.Warning);
                    return;
                }

                Dispatcher.Invoke(() => DisposePPTManagers(isShutdown: true), DispatcherPriority.Send);
            }
            catch (TaskCanceledException ex)
            {
                LogHelper.WriteLogToFile($"关机时卸载PPT模块被取消: {ex.Message}", LogHelper.LogType.Warning);
            }
            catch (ObjectDisposedException ex)
            {
                LogHelper.WriteLogToFile($"关机时卸载PPT模块对象已释放: {ex.Message}", LogHelper.LogType.Warning);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"关机时卸载PPT模块失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 初始化长按定时器
        /// </summary>
        private void InitializeLongPressTimer()
        {
            _longPressTimer = new DispatcherTimer();
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(LongPressDelay);
            _longPressTimer.Tick += OnLongPressTimerTick;
        }

        /// <summary>
        /// 启动长按检测
        /// </summary>
        /// <param name="sender">触发事件的控件</param>
        /// <param name="isNext">是否为下一页按钮</param>
        private void StartLongPressDetection(object sender, bool isNext)
        {
            if (!Settings.PowerPointSettings.EnablePPTButtonLongPressPageTurn) return;

            _isLongPressNext = isNext;
            // 重置定时器间隔为初始延迟时间，确保每次长按检测都从正确的延迟开始
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(LongPressDelay);
            _longPressTimer?.Start();
        }

        /// <summary>
        /// 停止长按检测
        /// </summary>
        private void StopLongPressDetection()
        {
            _longPressTimer?.Stop();
        }

        /// <summary>
        /// 长按定时器事件处理
        /// </summary>
        private void OnLongPressTimerTick(object sender, EventArgs e)
        {
            if (!Settings.PowerPointSettings.EnablePPTButtonLongPressPageTurn) return;

            _longPressTimer.Interval = TimeSpan.FromMilliseconds(LongPressInterval);

            // 执行翻页
            if (_isLongPressNext)
            {
                BtnPPTSlidesDown_Click(null, null);
            }
            else
            {
                BtnPPTSlidesUp_Click(null, null);
            }
        }
        #endregion

        #region 仅PPT模式可见性（COM + Win32 兜底）

        /// <summary>
        /// 在启用「仅PPT模式」时启动轻量探测，COM 事件延迟或失效时仍可根据全屏放映窗口显示主窗口。
        /// </summary>
        internal void EnsurePPTOnlyVisibilityProbeTimer()
        {
            try
            {
                if (!Settings.ModeSettings.IsPPTOnlyMode)
                {
                    StopPPTOnlyVisibilityProbeTimer();
                    return;
                }

                if (_pptOnlyVisibilityProbeTimer == null)
                {
                    _pptOnlyVisibilityProbeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(PPTOnlyVisibilityProbeIntervalMs)
                    };
                    _pptOnlyVisibilityProbeTimer.Tick += (_, __) => CheckMainWindowVisibility();
                }

                if (!_pptOnlyVisibilityProbeTimer.IsEnabled)
                    _pptOnlyVisibilityProbeTimer.Start();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"仅PPT可见性探测计时器启动失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        internal void StopPPTOnlyVisibilityProbeTimer()
        {
            try
            {
                _pptOnlyVisibilityProbeTimer?.Stop();
            }
            catch
            {
            }
        }

        /// <summary>
        /// 检测是否存在 PowerPoint 全屏放映顶层窗口（类名 screenClass，进程 powerpnt），用于 COM 不可用时的兜底。
        /// </summary>
        internal bool IsPowerPointSlideshowSurfacePresentWin32()
        {
            if (!Settings.ModeSettings.IsPPTOnlyMode)
                return false;

            try
            {
                bool found = false;
                EnumWindows((hWnd, _) =>
                {
                    if (!IsWindow(hWnd) || !IsWindowVisible(hWnd))
                        return true;

                    var cls = new StringBuilder(256);
                    if (GetClassName(hWnd, cls, cls.Capacity) == 0)
                        return true;

                    if (!string.Equals(cls.ToString(), PowerPointSlideShowWindowClassName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    try
                    {
                        GetWindowThreadProcessId(hWnd, out uint pid);
                        using (var proc = Process.GetProcessById((int)pid))
                        {
                            var name = proc.ProcessName;
                            if (string.Equals(name, "POWERPNT", StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                return false;
                            }
                        }
                    }
                    catch
                    {
                    }

                    return true;
                }, IntPtr.Zero);

                return found;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"Win32 检测 PPT 放映窗口失败: {ex.Message}", LogHelper.LogType.Trace);
                return false;
            }
        }

        #endregion

        #region New PPT Event Handlers
        /// <summary>
        /// 处理 PowerPoint 连接状态的变更：更新界面连接/放映状态，并在断开时启动一个短延迟以安全退出 PPT 模式。
        /// </summary>
        /// <param name="isConnected">指示当前是否已与 PowerPoint 建立连接；`true` 表示已连接，`false` 表示已断开。</param>
        private async Task OnPPTConnectionChanged(bool isConnected)
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!PresentationLink.IsCurrentOperation) return;
                    _pptUIManager?.UpdateConnectionStatus(isConnected);

                    if (isConnected)
                    {
                        _exitPPTModeAfterDisconnectTimer?.Stop();
                        _exitPPTModeAfterDisconnectTimer = null;
                        SchedulePPTEnhancedPreviewPreload();
                        LogHelper.WriteLogToFile("PPT连接已建立", LogHelper.LogType.Event);
                    }
                    else
                    {
                        LogHelper.WriteLogToFile("PPT连接已断开", LogHelper.LogType.Event);
                        _singlePPTInkManager?.ClearAllStrokes();
                        CollapseAllPPTNavBarPreviews();
                        ResetPPTEnhancedPreviewCache();
                        _exitPPTModeAfterDisconnectTimer?.Stop();
                        _exitPPTModeAfterDisconnectTimer = null;
                        _pptUIManager?.UpdateSlideShowStatus(false);
                        _pptUIManager?.UpdateSidebarExitButtons(false);

                        // 隐藏浮动栏退出PPT按钮
                        HideFloatingBarExitPPTBtn();
                        ResetPPTStateVariables();
                        if (Settings.Automation.IsAutoFoldAfterPPTSlideShow && !isFloatingBarFolded)
                            FoldFloatingBar_MouseUp(new object(), null);

                        CheckMainWindowVisibility();
                    }
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理PPT连接状态变化失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理 PowerPoint 演示文稿打开事件：清理画布墨迹、初始化墨迹管理器、处理导航逻辑、检查隐藏幻灯片和自动播放设置，并更新连接状态。
        /// </summary>
        /// <param name="pres">已打开的 PowerPoint 演示文稿（Presentation）实例。</param>
        /// <remarks>
        /// 操作包括：清理画布墨迹和备份历史记录，初始化墨迹管理器，处理跳转到首页或上次播放页的逻辑，检查隐藏幻灯片和自动播放设置，更新UI连接状态，并记录事件日志。
        /// 所有操作在UI线程异步执行，异常会被捕获并记录为错误日志。
        /// </remarks>
        private async Task OnPPTPresentationOpen(PresentationLinkState state)
        {
            var metadata = state.Presentation;
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!PresentationLink.IsCurrentOperation) return;
                    // 在初始化墨迹管理器之前，先清理画布上的所有墨迹
                    CancelPresentationNotifications();
                    ResetPPTEnhancedPreviewCache();

                    ClearStrokes(true);

                    // 清理备份历史记录，防止旧演示文稿的墨迹影响新演示文稿
                    if (TimeMachineHistories != null && TimeMachineHistories.Length > 0)
                    {
                        TimeMachineHistories[0] = null;
                    }

                    _singlePPTInkManager?.InitializePresentation(metadata);
                    _inkPresentationSessionId = state.PresentationSessionId;

                    ApplyPresentationLinkSettings();

                    _pptUIManager?.UpdateConnectionStatus(true);

                    SchedulePPTEnhancedPreviewPreload();

                    LogHelper.WriteLogToFile($"已打开新演示文稿: {metadata?.Name}，墨迹状态已清理", LogHelper.LogType.Event);
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理演示文稿打开事件失败: {ex}", LogHelper.LogType.Error);
            }
        }

        internal string GetPresentationStrokeFolderPath(PresentationMetadata metadata)
        {
            string basePath = Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "Auto Saved - Presentations");
            string fullName = metadata?.FullName;
            string name = metadata?.Name;
            int slidesTotal = metadata?.TotalSlides ?? 0;

            if (!string.IsNullOrEmpty(fullName))
            {
                string hash = HashHelper.GetFileHash(fullName);
                return Path.Combine(basePath, $"{name ?? ""}_{slidesTotal}_{hash}");
            }

            return Path.Combine(basePath, $"{name ?? ""}_{slidesTotal}");
        }

        private async Task OnPPTPresentationClose(PresentationLinkState state)
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!PresentationLink.IsCurrentOperation) return;
                    CancelPresentationNotifications();
                    CollapseAllPPTNavBarPreviews();
                    ResetPPTEnhancedPreviewCache();

                    _singlePPTInkManager?.ClearAllStrokes();

                    _pptUIManager?.UpdateConnectionStatus(false);
                });
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 处理 PowerPoint 幻灯片放映状态变化事件：更新UI管理器的放映状态并检查主窗口可见性。
        /// </summary>
        /// <param name="isInSlideShow">指示当前是否处于幻灯片放映状态；`true` 表示正在放映，`false` 表示已退出放映。</param>
        /// <remarks>
        /// 操作包括：在UI线程异步通知UI管理器放映状态变化，检查并更新主窗口的可见性（用于仅PPT模式）。
        /// 异常会被捕获并记录为错误日志，确保方法执行不会中断。
        /// </remarks>
        private async Task OnPPTSlideShowStateChanged(bool isInSlideShow)
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!PresentationLink.IsCurrentOperation) return;
                    // 通知UI管理器放映状态变化
                    _pptUIManager?.OnSlideShowStateChanged(isInSlideShow);

                    if (!isInSlideShow)
                    {
                        CollapseAllPPTNavBarPreviews();
                    }

                    // 检查主窗口可见性（用于仅PPT模式）
                    CheckMainWindowVisibility();
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理PPT放映状态变化失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理 PowerPoint 幻灯片放映开始事件：根据设置折叠或展开浮动栏，初始化放映状态，更新UI，加载当前页墨迹，并设置相关参数。
        /// </summary>
        /// <param name="wn">PowerPoint 幻灯片放映窗口（SlideShowWindow）实例，包含当前放映状态和视图信息。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 根据设置自动折叠或展开浮动栏
        /// 2. 停止墨迹重放
        /// 3. 获取当前活动演示文稿、当前幻灯片和总幻灯片数
        /// 4. 初始化墨迹管理器
        /// 5. 处理跳转到首页或上次播放位置的逻辑
        /// 6. 更新UI状态，包括放映状态、当前幻灯片编号
        /// 7. 设置浮动栏透明度和边距
        /// 8. 显示侧边栏退出按钮
        /// 9. 处理画板显示
        /// 10. 关闭白板模式（如果当前在白板模式）
        /// 11. 显示浮动栏主控件
        /// 12. 根据设置隐藏或显示手势面板和按钮
        /// 13. 如果设置了在新放映时显示画布，则进入批注模式并显示调色盘
        /// 14. 重置幻灯片放映结束事件标志
        /// 15. 加载当前页墨迹
        /// 16. 调整浮动栏边距动画
        /// 
        /// 所有UI操作在UI线程异步执行，异常会被捕获并记录为错误日志。
        /// </remarks>
        private async Task OnPPTSlideShowBegin(PresentationLinkState state)
        {
            int generation = PresentationLink.Generation;
            try
            {
                if (Settings.Automation.IsAutoFoldInPPTSlideShow)
                {
                    if (!isFloatingBarFolded)
                        FoldFloatingBar_MouseUp(new object(), null);
                }
                else
                {
                    if (isFloatingBarFolded)
                    {
                        await UnFoldFloatingBar(new object());
                        if (generation != PresentationLink.Generation) return;
                    }
                }

                isStopInkReplay = true;

                int currentSlide = state.CurrentSlide;
                int totalSlides = state.TotalSlides;
                string presentationName = state.Presentation?.Name;

                _currentSlideShowPosition = currentSlide;
                _previousSlideID = currentSlide;

                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (generation != PresentationLink.Generation) return;
                    if (_singlePPTInkManager != null)
                    {
                        try
                        {
                            _singlePPTInkManager.InitializePresentation(state.Presentation);
                            _inkPresentationSessionId = state.PresentationSessionId;
                        }
                        catch (Exception)
                        {
                        }
                    }

                    // 处理跳转到首页或上次播放位置
                    ApplyPresentationLinkSettings();

                    // 更新UI状态
                    _pptUIManager?.UpdateSlideShowStatus(true, currentSlide, totalSlides);

                    // 设置浮动栏透明度和边距
                    _pptUIManager?.SetFloatingBarOpacity(Settings.Appearance.ViewboxFloatingBarOpacityInPPTValue);
                    ApplyFloatingBarMenuOpacity();
                    _pptUIManager?.SetMainPanelMargin(new Thickness(10, 10, 10, 10));

                    // 显示侧边栏退出按钮
                    _pptUIManager?.UpdateSidebarExitButtons(true);

                    // 显示浮动栏退出PPT按钮
                    ShowFloatingBarExitPPTBtn();

                    // 处理画板显示
                    if (Settings.PowerPointSettings.IsShowCanvasAtNewSlideShow &&
                        !Settings.Automation.IsAutoFoldInPPTSlideShow &&
                        GridTransparencyFakeBackground.Background == Brushes.Transparent && !isFloatingBarFolded)
                    {
                        BtnHideInkCanvas_Click(null, null);
                    }

                    if (currentMode != 0)
                    {
                        ImageBlackboard_MouseUp(null, null);
                        BtnHideInkCanvas_Click(null, null);
                    }

                    SetFloatingBarContentVisibility(true);

                    // 在PPT模式下根据设置决定是否隐藏手势面板和手势按钮
                    AnimationsHelper.HideWithSlideAndFade(TwoFingerGestureBorder);
                    AnimationsHelper.HideWithSlideAndFade(BoardTwoFingerGestureBorder);

                    // 根据设置决定是否在PPT放映模式下显示手势按钮
                    UpdateToolbarComponentVisibility();

                    if (Settings.PowerPointSettings.IsShowCanvasAtNewSlideShow &&
                        !Settings.Automation.IsAutoFoldInPPTSlideShow)
                    {
                        await Task.Delay(300);
                        if (generation != PresentationLink.Generation) return;
                        // 先进入批注模式，这会显示调色盘
                        PenIcon_Click(null, null);
                        // 然后设置颜色
                        BtnColorRed_Click(null, null);
                        try
                        {
                            if (inkCanvas.EditingMode == InkCanvasEditingMode.Ink)
                            {
                                UpdateCurrentToolMode("pen");
                                SetFloatingBarHighlightPosition("pen");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"PPT进入批注模式后同步浮动栏高光状态失败: {ex.Message}", LogHelper.LogType.Error);
                        }
                    }

                    isEnteredSlideShowEndEvent = false;

                    // 加载当前页墨迹
                    LoadCurrentSlideInk(currentSlide);

                    // 仅PPT模式：放映开始立即同步主窗口可见性（勿仅依赖 SlideShowStateChanged 定时器）
                    CheckMainWindowVisibility();

                    // 刷新智慧模式区域
                    if (Settings.PowerPointSettings.EnableSmartMode)
                    {
                        System.Diagnostics.Debug.WriteLine("[SmartMode] SlideShowBegin, refreshing regions...");
                        RefreshSmartModeRegions();
                    }
                }).Task.Unwrap();

                if (generation != PresentationLink.Generation) return;
                if (!isFloatingBarFolded)
                {
                    await Task.Delay(100);
                    if (generation == PresentationLink.Generation) ViewboxFloatingBarMarginAnimation(60);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理幻灯片放映开始事件失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理幻灯片放映中的切换：在幻灯片变更时保存当前页墨迹、加载目标页墨迹并更新界面状态。
        /// </summary>
        /// <param name="state">联动源捕获的翻页状态，不包含 COM 或 Agent 协议对象。</param>
        /// <remarks>
        /// - 如果收到与当前记录相同的页码或已有切换正在处理，则忽略该事件。
        /// - 在切换过程中会保存前一页的墨迹（如存在）、清空画布与历史、加载新页的墨迹、锁定新页墨迹并刷新当前页显示序号，同时更新内部的当前播放位置状态。
        /// </remarks>
        private async Task OnPPTSlideShowNextSlide(PresentationLinkState state)
        {
            if (state == null || state.CurrentSlide <= 0) return;
            try
            {
                int currentSlide = state.CurrentSlide;
                int totalSlides = state.TotalSlides;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!PresentationLink.IsCurrentOperation) return;
                    if (currentSlide == _previousSlideID) return;
                    int prev = _previousSlideID;
                    if (currentMode == 0) _singlePPTInkManager?.ForceSaveSlideStrokes(prev, inkCanvas.Strokes);

                    _currentSlideShowPosition = currentSlide;
                    _previousSlideID = currentSlide;
                    _singlePPTInkManager?.LockInkForSlide(currentSlide);

                    LoadCurrentSlideInk(currentSlide);
                    // 翻页宿主效果与墨迹切换使用同一 UI 队列，避免快速翻页的前页标识错位。
                    if (Settings.PowerPointSettings.EnableSmartMode) RefreshSmartModeRegions();
                    _miniWhiteboardWindow?.OnPPTSlideChangedExternal(currentSlide - 1);
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理幻灯片切换事件失败: {ex}", LogHelper.LogType.Error);
            }
        }

        #region 智慧模式：视频控件区域刷新与坐标转换

        /// <summary>
        /// 从 PPT Agent / COM 获取当前幻灯片的视频控件区域，缓存后用于鼠标进入/离开判断。
        /// </summary>
        private void RefreshSmartModeRegions()
        {
            try
            {
                if (!Settings.PowerPointSettings.EnableSmartMode)
                {
                    _smartModeRegions = null;
                    _smartModeSlideIndex = -1;
                    LogHelper.WriteLogToFile("[SmartMode] 功能未开启", LogHelper.LogType.Info);
                    return;
                }

                var response = PresentationLink?.GetSmartRegions() ?? new PresentationSmartRegions();
                _smartModeRegions = response.Regions;
                _smartModeSlideIndex = response.SlideIndex;
                _smartModeSlideWidth = response.SlideWidth;
                _smartModeSlideHeight = response.SlideHeight;
                _smartModeSlideShowHwnd = response.WindowHandle;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[SmartMode] 刷新失败: {ex}", LogHelper.LogType.Warning);
                _smartModeRegions = null;
            }

            // 将磅值坐标转换为 WPF 窗口坐标（必须在 UI 线程执行）
            Application.Current.Dispatcher.Invoke(() => BuildSmartModeRects());
        }

        #endregion

        /// <summary>
        /// 处理 PowerPoint 幻灯片放映结束时的清理与界面恢复，包括保存当前幻灯片墨迹、重置墨迹管理器状态、恢复主题与工具栏显示，并根据配置折叠或展示浮动工具栏等 UI 调整。
        /// </summary>
        /// <param name="pres">触发结束事件的 PowerPoint 演示文稿（Presentation）实例，用于保存墨迹并尝试读取放映时的当前页码。</param>
        private async Task OnPPTSlideShowEnd(PresentationLinkState state)
        {
            int generation = PresentationLink.Generation;
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() => CollapseAllPPTNavBarPreviews());
                if (generation != PresentationLink.Generation) return;

                if (Settings.Automation.IsAutoFoldAfterPPTSlideShow && !isFloatingBarFolded)
                {
                    FoldFloatingBar_MouseUp(new object(), null);
                }

                if (isEnteredSlideShowEndEvent) return;

                // 清除智慧模式区域
                _smartModeRegions = null;
                _smartModeSlideIndex = -1;

                // 清除WPF坐标映射和定时器
                _mediaPassthroughRects?.Clear();
                StopMediaPassthroughTimer();
                _isMediaRegionMouseMode = false;

                // 获取当前播放页码，优先使用跟踪的页码，否则尝试从PPT管理器获取
                int currentPage = _currentSlideShowPosition;
                if (currentPage <= 0)
                {
                    currentPage = state.CurrentSlide;
                }

                if (currentMode == 0 && currentPage > 0 && inkCanvas?.Strokes != null)
                    _singlePPTInkManager?.ForceSaveSlideStrokes(currentPage, inkCanvas.Strokes);
                isEnteredSlideShowEndEvent = true;

                string presentationNameForSave = state.Presentation?.Name;
                int totalSlidesForSave = state.TotalSlides;

                if (currentPage > 0 && Settings.PowerPointSettings.IsNotifyPreviousPage && !string.IsNullOrEmpty(presentationNameForSave) && totalSlidesForSave > 0)
                {
                    _singlePPTInkManager?.SavePlaybackPosition(currentPage);
                }

                if (Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint && !string.IsNullOrEmpty(presentationNameForSave) && totalSlidesForSave > 0)
                {
                    var inkManager = _singlePPTInkManager;
                    await Task.Run(() => inkManager?.SaveAllStrokesToFile(currentPage));
                    if (generation != PresentationLink.Generation) return;
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (generation != PresentationLink.Generation) return;
                    try
                    {
                        isPresentationHaveBlackSpace = false;

                        // 恢复主题
                        if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Light)
                        {
                            { /* Old UI removed */ }
                            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                        }

                        // 更新UI状态
                        _pptUIManager?.UpdateSlideShowStatus(false);
                        _pptUIManager?.UpdateSidebarExitButtons(false);

                        // 隐藏浮动栏退出PPT按钮
                        HideFloatingBarExitPPTBtn();

                        _pptUIManager?.SetMainPanelMargin(new Thickness(10, 10, 10, 55));
                        _pptUIManager?.SetFloatingBarOpacity(Settings.Appearance.ViewboxFloatingBarOpacityValue);
                        ApplyFloatingBarMenuOpacity();

                        if (currentMode != 0)
                        {
                            CloseWhiteboardImmediately();
                            currentMode = 0;
                            AutomationBootstrap.Monitor?.NotifyInternalStateChanged();
                        }

                        SyncPdfPageSidebarWithCanvas();

                        ClearStrokes(true);
                        // 清空备份历史记录，防止退出白板时恢复已结束PPT的墨迹
                        // 注意：这里只清空索引0的备份，不影响白板页面的墨迹（索引1及以上）
                        TimeMachineHistories[0] = null;

                        // 重置墨迹管理器的锁定状态，防止下次放映时墨迹显示错误
                        ResetInkManagerLockState();

                        // 退出PPT模式时恢复手势面板和手势按钮的显示状态
                        UpdateToolbarComponentVisibility();

                        // 注意：快捷调色盘的可见性现在完全由工具栏规则集管理
                        // 不需要手动设置，UpdateToolbarComponentVisibility 会处理好

                        if (GridTransparencyFakeBackground.Background != Brushes.Transparent)
                            BtnHideInkCanvas_Click(null, null);
                        SetCurrentToolMode(InkCanvasEditingMode.None);

                        UpdateCurrentToolMode("cursor");
                        SetFloatingBarHighlightPosition("cursor");

                        CheckMainWindowVisibility();
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"处理幻灯片放映结束UI更新失败: {ex}", LogHelper.LogType.Error);
                    }
                });

                await Task.Delay(100);
                if (generation != PresentationLink.Generation) return;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (generation != PresentationLink.Generation) return;
                    if (!isFloatingBarFolded)
                    {
                        PureViewboxFloatingBarMarginAnimationInDesktopMode();
                        if (Settings.Automation.IsAutoEnterAnnotationModeWhenExitFoldMode)
                        {
                            Task.Delay(350).ContinueWith(_ =>
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (generation == PresentationLink.Generation && !PresentationLink.IsInSlideShow
                                        && PresentationLink.State.SlideShowSessionId == state.SlideShowSessionId && !isFloatingBarFolded)
                                    {
                                        ViewboxFloatingBarMarginAnimation(-60);
                                    }
                                });
                            });
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理幻灯片放映结束事件失败: {ex}", LogHelper.LogType.Error);
            }
        }
        #endregion

        #region Helper Methods
        private TaskCompletionSource<bool> _inlineDialogTcs;

        private void CancelPresentationNotifications()
        {
            _inlineDialogTcs?.TrySetResult(false);
            _inlineDialogTcs = null;
            InlineDialogRoot.Visibility = Visibility.Collapsed;
        }

        private async Task<bool> ShowInlineYesNoDialog(string title, string content)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inlineDialogTcs = completion;

            InlineDialogTitle.Content = title;
            InlineDialogContent.Text = content;

            InlineDialogRoot.Opacity = 0;
            InlineDialogRoot.Visibility = Visibility.Visible;
            InlineDialogScaleTransform.ScaleX = 1.05;
            InlineDialogScaleTransform.ScaleY = 1.05;

            var showAnimation = new System.Windows.Media.Animation.Storyboard();

            var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                TimeSpan.FromMilliseconds(150));
            opacityAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnimation, InlineDialogRoot);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnimation,
                new PropertyPath(Grid.OpacityProperty));
            showAnimation.Children.Add(opacityAnimation);

            var scaleXAnimation = new System.Windows.Media.Animation.DoubleAnimation(1.05, 1.0,
                TimeSpan.FromMilliseconds(250));
            scaleXAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            scaleXAnimation.EasingFunction = ease;
            System.Windows.Media.Animation.Storyboard.SetTarget(scaleXAnimation, InlineDialogCard);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleXAnimation,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            showAnimation.Children.Add(scaleXAnimation);

            var scaleYAnimation = new System.Windows.Media.Animation.DoubleAnimation(1.05, 1.0,
                TimeSpan.FromMilliseconds(250));
            scaleYAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            scaleYAnimation.EasingFunction = ease;
            System.Windows.Media.Animation.Storyboard.SetTarget(scaleYAnimation, InlineDialogCard);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleYAnimation,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            showAnimation.Children.Add(scaleYAnimation);

            showAnimation.Begin(this);

            return await completion.Task;
        }

        private void HideInlineDialog()
        {
            var completion = _inlineDialogTcs;
            var hideAnimation = new System.Windows.Media.Animation.Storyboard();

            var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
                TimeSpan.FromMilliseconds(150));
            opacityAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnimation, InlineDialogRoot);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnimation,
                new PropertyPath(Grid.OpacityProperty));
            hideAnimation.Children.Add(opacityAnimation);

            var scaleXAnimation = new System.Windows.Media.Animation.DoubleAnimation(1.0, 1.05,
                TimeSpan.FromMilliseconds(100));
            scaleXAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            System.Windows.Media.Animation.Storyboard.SetTarget(scaleXAnimation, InlineDialogCard);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleXAnimation,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            hideAnimation.Children.Add(scaleXAnimation);

            var scaleYAnimation = new System.Windows.Media.Animation.DoubleAnimation(1.0, 1.05,
                TimeSpan.FromMilliseconds(100));
            scaleYAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            System.Windows.Media.Animation.Storyboard.SetTarget(scaleYAnimation, InlineDialogCard);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleYAnimation,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            hideAnimation.Children.Add(scaleYAnimation);

            hideAnimation.Completed += (s, e) =>
            {
                if (ReferenceEquals(completion, _inlineDialogTcs)) InlineDialogRoot.Visibility = Visibility.Collapsed;
            };

            hideAnimation.Begin(this);
        }

        private void InlineDialogPrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            HideInlineDialog();
            _inlineDialogTcs?.TrySetResult(true);
        }

        private void InlineDialogSecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            HideInlineDialog();
            _inlineDialogTcs?.TrySetResult(false);
        }

        /// <summary>
        /// 加载当前幻灯片的墨迹：清空画布和历史记录，然后加载指定幻灯片的墨迹。
        /// </summary>
        /// <param name="slideIndex">要加载墨迹的幻灯片索引。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 清空画布上的所有墨迹
        /// 2. 清空时间机器的墨迹历史记录
        /// 3. 从墨迹管理器加载指定幻灯片的墨迹
        /// 4. 如果加载到墨迹且墨迹集合不为空，则将墨迹添加到画布
        /// 异常会被捕获并记录为错误日志，确保方法执行不会中断。
        /// </remarks>
        private void LoadCurrentSlideInk(int slideIndex)
        {
            try
            {
                ClearStrokes(true);
                timeMachine.ClearStrokeHistory();

                var strokes = _singlePPTInkManager?.LoadSlideStrokes(slideIndex);
                if (strokes != null) inkCanvas.Strokes.Add(strokes);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"加载当前页墨迹失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 重置墨迹管理器的锁定状态，防止墨迹显示错误
        /// </summary>
        private void ResetInkManagerLockState()
        {
            try
            {
                _singlePPTInkManager?.ResetLockState();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"重置墨迹管理器锁定状态失败: {ex}", LogHelper.LogType.Error);
            }
        }

        private void OnPPTLinkPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(PresentationLinkViewModel.TotalSlides)
                && PresentationLink.IsCurrentOperation && _inkPresentationSessionId != Guid.Empty
                && PresentationLink.State.PresentationSessionId == _inkPresentationSessionId)
                _singlePPTInkManager?.UpdateSlideCount(PresentationLink.TotalSlides);
        }

        private void SaveCurrentPPTInk()
        {
            if (_currentSlideShowPosition <= 0) return;
            if (currentMode == 0 && !isEnteredSlideShowEndEvent)
                _singlePPTInkManager?.ForceSaveSlideStrokes(_currentSlideShowPosition, inkCanvas.Strokes);
            if (Settings.PowerPointSettings.IsNotifyPreviousPage)
                _singlePPTInkManager?.SavePlaybackPosition(_currentSlideShowPosition);
            _singlePPTInkManager?.SaveAllStrokesToFile(_currentSlideShowPosition);
        }

        /// <summary>
        /// 重置PPT相关的状态变量，当PPT自动收纳设置变更时调用
        /// </summary>
        /// <remarks>
        /// 将与 PowerPoint 播放和状态追踪相关的内部字段重置为初始默认值。
        /// 具体重置的字段包括：
        /// 1. 播放结束重入保护标志（isEnteredSlideShowEndEvent）
        /// 2. 演示文稿黑边指示（isPresentationHaveBlackSpace）
        /// 3. 当前放映位置（_currentSlideShowPosition）
        /// 
        /// 该方法在执行过程中会：
        /// - 使用线程安全的方式重置滑动切换处理状态
        /// - 成功时记录追踪日志
        /// - 发生异常时记录错误日志并继续执行
        /// </remarks>
        public void ResetPPTStateVariables()
        {
            try
            {
                // 重置PPT放映结束事件标志
                isEnteredSlideShowEndEvent = false;

                // 重置演示文稿黑边状态
                isPresentationHaveBlackSpace = false;

                // 重置当前播放页码跟踪
                _currentSlideShowPosition = 0;
                _previousSlideID = 0;

                LogHelper.WriteLogToFile("PPT状态变量已重置", LogHelper.LogType.Trace);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"重置PPT状态变量失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        #endregion

        /// <summary>
        /// 发起一次手动的 PowerPoint 连接检查并在短延迟后报告结果。
        /// </summary>
        /// <remarks>
        /// 如果尚未初始化 PPT 管理器则先进行初始化，然后重载连接并启动监控；
        /// 延迟约 800 毫秒后在 UI 线程上检查连接状态：若已连接仅记录事件日志，若未连接则弹出提示并记录警告；
        /// 若过程中抛出异常则记录错误日志、将 UI 连接状态置为断开并提示用户未找到幻灯片。
        /// </remarks>
        private async void BtnCheckPPT_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 使用新的PPT管理器进行连接检查
                if (PresentationLink?.HasSource != true)
                {
                    InitializePPTManagers();
                }

                if (PresentationLink?.ReloadCommand.CanExecute(null) == true)
                    await PresentationLink.ReloadCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"手动检查PPT应用程序失败: {ex}", LogHelper.LogType.Error);
                _pptUIManager?.UpdateConnectionStatus(false);
                MessageBox.Show(Properties.PPTStrings.PPT_SlidesNotFound);
            }
        }

        /// <summary>
        /// 处理PowerPoint增强功能开关的切换事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">路由事件参数</param>
        /// <remarks>
        /// 当PowerPoint增强功能被启用时：
        /// 1. 禁用WPS支持
        /// 2. 更新PPT管理器的WPS支持设置
        /// 3. 启动PowerPoint进程守护
        /// 当PowerPoint增强功能被禁用时：
        /// 1. 停止PowerPoint进程守护
        /// 无论开关状态如何变化，都会保存设置到文件
        /// </remarks>
        private void ToggleSwitchPowerPointEnhancement_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;

            var toggle = sender as iNKORE.UI.WPF.Modern.Controls.ToggleSwitch;
            if (toggle != null)
                Settings.PowerPointSettings.EnablePowerPointEnhancement = toggle.IsOn;

            if (Settings.PowerPointSettings.EnablePowerPointEnhancement)
            {
                Settings.PowerPointSettings.IsSupportWPS = false;

                if (PresentationLink?.HasSource == true)
                {
                    PresentationLink.IsSupportWPS = false;
                }
            }

            SaveSettingsToFile();

            if (Settings.PowerPointSettings.EnablePowerPointEnhancement)
            {
                StartPowerPointProcessMonitoring();
            }
            else
            {
                StopPowerPointProcessMonitoring();
            }
        }

        /// <summary>
        /// 处理WPS支持开关的切换事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">路由事件参数</param>
        /// <remarks>
        /// 当WPS支持被启用时：
        /// 1. 如果PowerPoint支持未启用，则启用PowerPoint支持
        /// 2. 启动PPT监控
        /// 3. 如果PowerPoint增强功能已启用，则禁用它并停止PowerPoint进程守护
        /// 无论开关状态如何变化，都会：
        /// 1. 更新PPT管理器的WPS支持设置
        /// 2. 保存设置到文件
        /// </remarks>
        private void ToggleSwitchSupportWPS_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;

            var toggle = sender as iNKORE.UI.WPF.Modern.Controls.ToggleSwitch;
            if (toggle != null)
                Settings.PowerPointSettings.IsSupportWPS = toggle.IsOn;

            if (Settings.PowerPointSettings.IsSupportWPS)
            {
                if (!Settings.PowerPointSettings.PowerPointSupport)
                {
                    Settings.PowerPointSettings.PowerPointSupport = true;

                    if (PresentationLink?.HasSource != true)
                    {
                        InitializePPTManagers();
                    }
                    StartPPTMonitoring();
                }

                if (Settings.PowerPointSettings.EnablePowerPointEnhancement)
                {
                    Settings.PowerPointSettings.EnablePowerPointEnhancement = false;
                    StopPowerPointProcessMonitoring();
                }
            }

            if (PresentationLink?.HasSource == true)
            {
                PresentationLink.IsSupportWPS = Settings.PowerPointSettings.IsSupportWPS;
                PresentationLink.SkipAnimationsWhenNavigating = Settings.PowerPointSettings.SkipAnimationsWhenGoNext;
            }

            SaveSettingsToFile();
        }

        private void ToggleSwitchSkipAnimationsWhenGoNext_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;

            var toggle = sender as iNKORE.UI.WPF.Modern.Controls.ToggleSwitch;
            if (toggle != null)
                Settings.PowerPointSettings.SkipAnimationsWhenGoNext = toggle.IsOn;

            if (PresentationLink?.HasSource == true)
            {
                PresentationLink.SkipAnimationsWhenNavigating = Settings.PowerPointSettings.SkipAnimationsWhenGoNext;
            }

            SaveSettingsToFile();
        }

        /// <summary>
        /// 获取当前是否启用了WPS支持
        /// </summary>
        /// <value>如果启用了WPS支持，则为true；否则为false</value>
        private static bool isWPSSupportOn => Settings.PowerPointSettings.IsSupportWPS;

        /// <summary>
        /// 处理“上一页”按钮的点击操作：在满足自动保存条件时保存当前幻灯片截图并尝试切换到上一张幻灯片；在切换失败或发生异常时记录日志并更新连接状态。
        /// </summary>
        /// <param name="sender">事件的来源对象（通常是触发按钮）。</param>
        /// <param name="e">路由事件参数。</param>
        private void BtnPPTSlidesUp_Click(object sender, RoutedEventArgs e)
        {
            if (PresentationLink?.PreviousCommand.CanExecute(null) == true) PresentationLink.PreviousCommand.Execute(null);
        }

        /// <summary>
        /// 处理“下一页”按钮点击：在满足自动保存条件时保存当前幻灯片的截图并尝试切换到下一张幻灯片。
        /// </summary>
        /// <remarks>
        /// 如果切换操作失败或发生异常，会写入日志并将 PPT 连接状态更新为断开。
        /// </remarks>
        private void BtnPPTSlidesDown_Click(object sender, RoutedEventArgs e)
        {
            if (PresentationLink?.NextCommand.CanExecute(null) == true) PresentationLink.NextCommand.Execute(null);
        }

        /// <summary>
        /// 处理PPT导航按钮的鼠标按下事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 该方法在用户按下PPT导航按钮时执行以下操作：
        /// 1. 记录按下的按钮对象
        /// 2. 检查是否启用了PPT按钮页码点击功能
        /// 3. 根据按下的按钮设置相应的反馈边框透明度
        /// </remarks>
        private void PPTNavigationBtn_MouseDown(object sender, MouseButtonEventArgs e) { }
        private void PPTNavigationBtn_MouseLeave(object sender, MouseEventArgs e) { }
        private void PPTNavigationBtn_MouseUp(object sender, MouseButtonEventArgs e) { }

        private void CollapseAllPPTNavBarPreviews() => PresentationLink?.CollapsePreview();

        /// <summary>在 MainWindow 加载完成后调用,把 4 个 PPTNavBar 的事件接到本类。</summary>
        private bool _pptNavBarsWired;

        private void WirePPTNavBars()
        {
            // InitializePPTManagers 可能被多次调用（切换 COM/ROT、设置变更等）。
            // PPTNavBar 事件若在同一控件上重复订阅，会导致翻页、长按、预览展开等逻辑成倍触发。
            if (_pptNavBarsWired) return;

            var bars = new[]
            {
                LeftBottomPanelForPPTNavigation,
                RightBottomPanelForPPTNavigation,
                LeftSidePanelForPPTNavigation,
                RightSidePanelForPPTNavigation,
            };
            foreach (var bar in bars)
            {
                if (bar == null) continue;
                bar.DataContext = PresentationLink;
                bar.SetBinding(Controls.PPTNavBar.CurrentSlideProperty, new System.Windows.Data.Binding(nameof(PresentationLink.CurrentSlide)));
                bar.SetBinding(Controls.PPTNavBar.TotalSlidesProperty, new System.Windows.Data.Binding(nameof(PresentationLink.TotalSlides)));
                bar.SetBinding(Controls.PPTNavBar.PreviewItemsProperty, new System.Windows.Data.Binding(nameof(PresentationLink.PreviewItems)));
                bar.SetBinding(Controls.PPTNavBar.PreviousCommandProperty, new System.Windows.Data.Binding(nameof(PresentationLink.PreviousCommand)));
                bar.SetBinding(Controls.PPTNavBar.NextCommandProperty, new System.Windows.Data.Binding(nameof(PresentationLink.NextCommand)));
                bar.SetBinding(Controls.PPTNavBar.NavigateCommandProperty, new System.Windows.Data.Binding(nameof(PresentationLink.NavigateCommand)));
                bar.SetBinding(Controls.PPTNavBar.PageCommandProperty, new System.Windows.Data.Binding(nameof(PresentationLink.ShowNavigationCommand)));
                var previewProperties = new[] { nameof(PresentationLink.IsLeftBottomPreviewExpanded), nameof(PresentationLink.IsRightBottomPreviewExpanded),
                    nameof(PresentationLink.IsLeftSidePreviewExpanded), nameof(PresentationLink.IsRightSidePreviewExpanded) };
                bar.SetBinding(Controls.PPTNavBar.IsPreviewExpandedProperty,
                    new System.Windows.Data.Binding(previewProperties[(int)bar.Direction]) { Mode = System.Windows.Data.BindingMode.TwoWay });
                var visibilityProperties = new[] { nameof(PresentationLink.IsLeftBottomNavigationVisible), nameof(PresentationLink.IsRightBottomNavigationVisible),
                    nameof(PresentationLink.IsLeftSideNavigationVisible), nameof(PresentationLink.IsRightSideNavigationVisible) };
                bar.SetBinding(Controls.PPTNavBar.IsNavigationVisibleProperty,
                    new System.Windows.Data.Binding(visibilityProperties[(int)bar.Direction]));
                bar.PreviousPressedDown += (s, e) =>
                {
                    if (Settings.PowerPointSettings.EnablePPTButtonLongPressPageTurn)
                        StartLongPressDetection(s, false);
                };
                bar.NextPressedDown += (s, e) =>
                {
                    if (Settings.PowerPointSettings.EnablePPTButtonLongPressPageTurn)
                        StartLongPressDetection(s, true);
                };
                bar.PressEnded += (s, e) => StopLongPressDetection();
            }

            PPTBtnPageNow.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PresentationLink.CurrentSlideText)) { Source = PresentationLink });
            PPTBtnPageTotal.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PresentationLink.TotalSlidesText)) { Source = PresentationLink });
            _pptNavBarsWired = true;
        }

        private void ResetPPTEnhancedPreviewCache() => PresentationLink?.ResetPreviewCache();

        private void SchedulePPTEnhancedPreviewPreload()
        {
            ApplyPresentationLinkSettings();
            if (PresentationLink != null) _ = PresentationLink.PreloadPreviewAsync();
        }

        /// <summary>
        /// 处理“开始幻灯片放映”按钮的点击事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">路由事件参数</param>
        /// <remarks>
        /// 该方法在用户点击“开始幻灯片放映”按钮时执行以下操作：
        /// 1. 在新线程中尝试启动PPT幻灯片放映
        /// 2. 如果启动失败，记录警告日志
        /// 3. 捕获并记录可能的异常
        /// </remarks>
        private void BtnPPTSlideShow_Click(object sender, RoutedEventArgs e)
        {
            if (PresentationLink?.StartSlideShowCommand.CanExecute(null) == true) PresentationLink.StartSlideShowCommand.Execute(null);
        }

        public async Task ExitPPTPresentation()
        {
            if (PresentationLink?.EndSlideShowCommand.CanExecute(null) == true)
                await PresentationLink.EndSlideShowCommand.ExecuteAsync(null);
        }

        /// <summary>
        /// 手动处理PPT放映结束时的自动收纳
        /// </summary>
        private async Task HandleManualSlideShowEnd()
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() => CollapseAllPPTNavBarPreviews());

                if (Settings.Automation.IsAutoFoldAfterPPTSlideShow && !isFloatingBarFolded)
                {
                    FoldFloatingBar_MouseUp(new object(), null);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"手动处理PPT放映结束自动收纳失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理PPT上一页控制按钮的鼠标按下事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 该方法在用户按下PPT上一页控制按钮时执行以下操作：
        /// 1. 记录按下的按钮对象
        /// 2. 根据按下的按钮设置相应的反馈边框透明度
        /// 3. 如果启用了PPT按钮长按翻页功能，则启动长按检测
        /// </remarks>
        private void GridPPTControlPrevious_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 旧 XAML 入口已废弃，事件由 PPTNavBar 控件转发；保留方法签名以兼容潜在外部引用。
        }
        private void GridPPTControlPrevious_MouseLeave(object sender, MouseEventArgs e)
        {
            StopLongPressDetection();
        }
        private void GridPPTControlPrevious_MouseUp(object sender, MouseButtonEventArgs e)
        {
            StopLongPressDetection();
            BtnPPTSlidesUp_Click(null, null);
        }


        private void GridPPTControlNext_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 旧 XAML 入口已废弃，事件由 PPTNavBar 控件转发；保留方法签名以兼容潜在外部引用。
        }
        private void GridPPTControlNext_MouseLeave(object sender, MouseEventArgs e)
        {
            StopLongPressDetection();
        }
        private void GridPPTControlNext_MouseUp(object sender, MouseButtonEventArgs e)
        {
            StopLongPressDetection();
            BtnPPTSlidesDown_Click(null, null);
        }

        /// <summary>
        /// 处理PPT结束控制按钮的鼠标释放事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 该方法在用户释放PPT结束控制按钮时调用BtnPPTSlideShowEnd_Click方法，实现结束幻灯片放映的功能
        /// </remarks>
        internal async void ImagePPTControlEnd_MouseUp(object sender, MouseButtonEventArgs e)
        {
            await ExitPPTPresentation();
        }

        private void ShowFloatingBarExitPPTBtn()
        {
            UpdateToolbarComponentVisibility();
        }

        private void HideFloatingBarExitPPTBtn()
        {
            UpdateToolbarComponentVisibility();
        }
    }
}
