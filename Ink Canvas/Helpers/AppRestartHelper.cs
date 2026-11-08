using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace Ink_Canvas.Helpers
{
    public static class AppRestartHelper
    {
        public static bool IsRunningAsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static void RestartApp(bool asAdmin)
        {
            try
            {
                App.IsAppExitByUser = true;

                (Application.Current as App)?.ReleaseMutexForRestart();

                string exePath = Process.GetCurrentProcess().MainModule.FileName;

                if (asAdmin)
                {
                    var psi = new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" };
                    Process.Start(psi);
                }
                else
                {
                    // 当前已是管理员时，直接通过用户令牌降权启动，避免经由 explorer 中转的延迟
                    if (IsRunningAsAdmin() && UIAccessHelper.RestartAsNormalUser())
                    {
                        Application.Current.Shutdown();
                        return;
                    }

                    Process.Start("explorer.exe", "\"" + exePath + "\"");
                }

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"重启应用时出错: {ex.Message}");
            }
        }

        public static void RestartWithCurrentPrivileges()
        {
            RestartApp(IsRunningAsAdmin());
        }

        public static void RestartAsAdmin()
        {
            RestartApp(true);
        }

        public static void RestartAsNormal()
        {
            RestartApp(false);
        }

        public static void SwitchToUIATopMostAndRestart()
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(new Action(SwitchToUIATopMostAndRestart));
                return;
            }

            bool started = false;
            bool timerStopped = false;
            try
            {
                SettingsManager.Settings.Advanced.EnableUIAccessTopMost = true;
                SettingsManager.Settings.Advanced.IsAlwaysOnTop = true;
                SettingsManager.SaveSettingsToFile();

                if (UIAccessHelper.HasUIAccess())
                {
                    started = true;
                    App.IsUIAccessTopMostEnabled = true;
                    if (Application.Current?.MainWindow != null)
                        WindowSettingsHelper.ApplyAlwaysOnTop(Application.Current.MainWindow);
                    return;
                }

                timerStopped = WindowSettingsHelper.OnStopKillProcessTimer?.Invoke() == true;
                bool useProcessToken = SettingsManager.Settings.Advanced.UIAMode == UIAMode.ProcessToken;

                if (IsRunningAsAdmin())
                {
                    if (useProcessToken)
                    {
                        started = UIAccessHelper.RestartAsNormalUserWithUIAccess_ProcessToken(sourcePid: (uint)Process.GetCurrentProcess().Id);
                    }
                    else
                    {
                        started = UIAccessHelper.RestartAsNormalUserWithUIAccess();
                    }
                }
                else
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    string helperArgs = UIAccessHelper.UIA_HELPER_SWITCH;
                    if (useProcessToken)
                        helperArgs += $" --uia-source-pid {Process.GetCurrentProcess().Id}";

                    var psi = new ProcessStartInfo(exePath)
                    {
                        Arguments = UIAccessHelper.BuildRelaunchArguments(Environment.GetCommandLineArgs(), helperArgs),
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using (var helper = Process.Start(psi))
                    {
                        if (helper != null)
                        {
                            // shortcut: 同步等待 helper 的创建结果，不观察应用初始化；若实测 helper 卡住，再加有界接力。
                            helper.WaitForExit();
                            started = helper.ExitCode == 0;
                        }
                    }
                }

                if (started)
                {
                    App.IsAppExitByUser = true;
                    try
                    {
                        if (App.watchdogProcess != null && !App.watchdogProcess.HasExited)
                            App.watchdogProcess.Kill();
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"UIAccess | 停止看门狗失败: {ex.Message}", LogHelper.LogType.Warning);
                    }
                    (Application.Current as App)?.ReleaseMutexForRestart();
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"UIAccess | 重启失败或授权取消: {ex.Message}", LogHelper.LogType.Warning);
            }
            finally
            {
                if (!started)
                {
                    if (timerStopped) WindowSettingsHelper.OnStartKillProcessTimer?.Invoke();
                    FallbackToNormalTopMost();
                }
            }
        }

        internal static void FallbackToNormalTopMost()
        {
            App.IsAppExitByUser = false;
            App.IsUIAccessTopMostEnabled = false;
            SettingsManager.Settings.Advanced.EnableUIAccessTopMost = false;
            SettingsManager.SaveSettingsToFile();
            if (Application.Current?.MainWindow != null)
                WindowSettingsHelper.ApplyAlwaysOnTop(Application.Current.MainWindow);
            LogHelper.WriteLogToFile("UIAccess | 已关闭 UIA，保留普通置顶运行", LogHelper.LogType.Warning);
        }

        public static void SwitchToNormalTopMostAndRestart()
        {
            try
            {
                SettingsManager.Settings.Advanced.EnableUIAccessTopMost = false;
                SettingsManager.SaveSettingsToFile();

                App.IsUIAccessTopMostEnabled = false;
                RestartApp(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"切换到普通置顶模式时出错: {ex.Message}");
            }
        }
    }
}
