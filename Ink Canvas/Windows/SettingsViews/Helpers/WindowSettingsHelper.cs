using Ink_Canvas.Helpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Ink_Canvas.Windows.SettingsViews.Helpers
{
    public static class WindowSettingsHelper
    {
        #region Keyboard Hook

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_KEYBOARD_LL = 13;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static LowLevelKeyboardProc _keyboardProc;
        private static IntPtr _keyboardHookId = IntPtr.Zero;

        private static IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        public static void InstallKeyboardHook()
        {
            if (_keyboardHookId == IntPtr.Zero)
            {
                _keyboardProc = KeyboardHookProc;
                _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc,
                    GetModuleHandle(null), 0);
                if (_keyboardHookId == IntPtr.Zero)
                {
                    LogHelper.WriteLogToFile("安装低级键盘钩子失败", LogHelper.LogType.Error);
                }
            }
        }

        public static void UninstallKeyboardHook()
        {
            if (_keyboardHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
                _keyboardProc = null;
            }
        }

        #endregion

        #region Timer Callbacks

        public static Func<bool> OnStopKillProcessTimer { get; set; }
        public static Action OnStartKillProcessTimer { get; set; }

        #endregion

        #region PPT Only Mode

        private static DispatcherTimer _pptOnlyVisibilityProbeTimer;
        private static Window _pptModeWindow;
        private const int PPTOnlyVisibilityProbeIntervalMs = 800;

        public static Action<bool> OnPPTOnlyModeChanged { get; set; }

        public static void ApplyPPTOnlyMode(Window window, bool isEnabled)
        {
            try
            {
                SettingsManager.Settings.ModeSettings.IsPPTOnlyMode = isEnabled;
                SettingsManager.SaveSettingsToFile();

                if (isEnabled)
                {
                    window.Hide();
                    LogHelper.WriteLogToFile("已切换到仅PPT模式，主窗口已隐藏", LogHelper.LogType.Event);
                    EnsurePPTOnlyVisibilityProbeTimer(window);
                }
                else
                {
                    StopPPTOnlyVisibilityProbeTimer();
                    window.Show();
                    LogHelper.WriteLogToFile("已切换到正常模式，主窗口已显示", LogHelper.LogType.Event);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"切换模式时出错: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private static void EnsurePPTOnlyVisibilityProbeTimer(Window window)
        {
            try
            {
                if (!SettingsManager.Settings.ModeSettings.IsPPTOnlyMode)
                {
                    StopPPTOnlyVisibilityProbeTimer();
                    return;
                }

                _pptModeWindow = window;

                if (_pptOnlyVisibilityProbeTimer == null)
                {
                    _pptOnlyVisibilityProbeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(PPTOnlyVisibilityProbeIntervalMs)
                    };
                    _pptOnlyVisibilityProbeTimer.Tick += PPTOnlyVisibilityProbeTimer_Tick;
                }

                if (!_pptOnlyVisibilityProbeTimer.IsEnabled)
                    _pptOnlyVisibilityProbeTimer.Start();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"仅PPT可见性探测计时器启动失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private static void StopPPTOnlyVisibilityProbeTimer()
        {
            try
            {
                _pptOnlyVisibilityProbeTimer?.Stop();
            }
            catch { }
        }

        private static void PPTOnlyVisibilityProbeTimer_Tick(object sender, EventArgs e)
        {
            OnPPTOnlyModeChanged?.Invoke(true);
        }

        #endregion

        #region Window Settings Methods

        public static bool IsTemporarilyDisablingNoFocusMode { get; set; }

        public static void ApplyNoFocusMode(Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            int exStyle = NativeWindowHelper.GetWindowLong(hwnd, NativeWindowHelper.GWL_EXSTYLE);

            bool shouldBeNoFocus = !IsTemporarilyDisablingNoFocusMode && SettingsManager.Settings.Advanced.IsNoFocusMode;

            if (shouldBeNoFocus)
            {
                NativeWindowHelper.SetWindowLong(hwnd, NativeWindowHelper.GWL_EXSTYLE, exStyle | NativeWindowHelper.WS_EX_NOACTIVATE);
                InstallKeyboardHook();
            }
            else
            {
                NativeWindowHelper.SetWindowLong(hwnd, NativeWindowHelper.GWL_EXSTYLE, exStyle & ~NativeWindowHelper.WS_EX_NOACTIVATE);
                UninstallKeyboardHook();
            }
        }

        public static void SetWindowMode(Window window)
        {
            if (SettingsManager.Settings.Advanced.WindowMode)
            {
                window.WindowState = WindowState.Normal;
                window.Left = 0.0;
                window.Top = 0.0;
                window.Height = SystemParameters.PrimaryScreenHeight;
                window.Width = SystemParameters.PrimaryScreenWidth;
            }
            else
            {
                window.WindowState = WindowState.Maximized;
            }
        }

        public static void ApplyAlwaysOnTop(Window window)
        {
            try
            {
                WindowTopmostManager.ApplyMainWindowTopmost(window, SettingsManager.Settings.Advanced.IsAlwaysOnTop);

                if (SettingsManager.Settings.Advanced.IsAlwaysOnTop &&
                    SettingsManager.Settings.Advanced.IsNoFocusMode &&
                    !SettingsManager.Settings.Advanced.EnableUIAccessTopMost)
                {
                    StartTopmostMaintenance(window);
                }
                else
                {
                    StopTopmostMaintenance();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用窗口置顶失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        public static void ApplyUIAccessTopMost(Window window)
        {
            try
            {
                if (SettingsManager.Settings.Advanced.EnableUIAccessTopMost && SettingsManager.Settings.Advanced.IsAlwaysOnTop)
                {
                    AppRestartHelper.SwitchToUIATopMostAndRestart();
                }
                else
                {
                    LogHelper.WriteLogToFile("UIA置顶功能已禁用", LogHelper.LogType.Trace);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用UIA置顶功能时出错: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        public static void SetTopmostBasedOnSettings(Window window, bool shouldBeTopmost)
        {
            if (SettingsManager.Settings.Advanced.IsAlwaysOnTop)
            {
                WindowTopmostManager.ApplyMainWindowTopmost(window, true);
                ApplyAlwaysOnTop(window);
            }
            else
            {
                WindowTopmostManager.ApplyMainWindowTopmost(window, shouldBeTopmost);
                if (!shouldBeTopmost)
                {
                    ApplyAlwaysOnTop(window);
                }
            }
        }

        public static void PauseTopmostMaintenance()
        {
            WindowTopmostManager.PauseTopmostMaintenance();
        }

        public static void ResumeTopmostMaintenance(Window window)
        {
            if (SettingsManager.Settings.Advanced.IsAlwaysOnTop &&
                SettingsManager.Settings.Advanced.IsNoFocusMode &&
                !SettingsManager.Settings.Advanced.EnableUIAccessTopMost)
            {
                WindowTopmostManager.ResumeTopmostMaintenance(window);
            }
        }

        private static void StartTopmostMaintenance(Window window)
        {
            if (SettingsManager.Settings.Advanced.EnableUIAccessTopMost) return;

            WindowTopmostManager.StartTopmostMaintenance(window);
            LogHelper.WriteLogToFile("启动置顶维护定时器", LogHelper.LogType.Trace);
        }

        private static void StopTopmostMaintenance()
        {
            WindowTopmostManager.StopTopmostMaintenance();
            LogHelper.WriteLogToFile("停止置顶维护定时器", LogHelper.LogType.Trace);
        }

        #endregion
    }
}
