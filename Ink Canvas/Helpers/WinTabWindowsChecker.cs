using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Automation;

namespace Ink_Canvas.Helpers
{
    internal class WinTabWindowsChecker
    {
        // A2：UI Automation 跨进程枚举开销大，缓存查询结果，TTL 1.5s。
        // 调用方（自动收纳 500ms 轮询）可接受 ≤1.5s 的状态滞后。
        private const long CacheTtlMs = 1500;
        private static readonly ConcurrentDictionary<string, (bool result, long ticks)> Cache
            = new ConcurrentDictionary<string, (bool, long)>();

        // 单调毫秒时钟（net462 无 Environment.TickCount64）
        private static long MonotonicMs() => (long)(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000.0);

        /*
        public static bool IsWindowMinimized(string windowName, bool matchFullName = true) {
            // 获取Win+Tab预览中的窗口
            AutomationElementCollection windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

            foreach (AutomationElement window in windows) {
                //LogHelper.WriteLogToFile("" + window.Current.Name);

                string windowTitle = window.Current.Name;

                // 如果窗口标题包含 windowName，则进行检查
                if (!string.IsNullOrEmpty(windowTitle) && windowTitle.Contains(windowName)) {
                    if (matchFullName) {
                        if (windowTitle.Length == windowName.Length) {
                            // 检查窗口是否最小化
                            WindowPattern windowPattern = window.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
                            if (windowPattern != null) {
                                bool isMinimized = windowPattern.Current.WindowVisualState == WindowVisualState.Minimized;
                                //LogHelper.WriteLogToFile("" + windowTitle + isMinimized);
                                return isMinimized;
                            }
                        }
                    } else {
                        // 检查窗口是否最小化
                        WindowPattern windowPattern = window.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
                        if (windowPattern != null) {
                            bool isMinimized = windowPattern.Current.WindowVisualState == WindowVisualState.Minimized;
                            return isMinimized;
                        }
                    }
                }
            }
            // 未找到软件白板窗口
            return true;
        }
        */

        public static bool IsWindowExisted(string windowName, bool matchFullName = true)
        {
            var key = windowName + "|" + matchFullName;
            long now = MonotonicMs();
            if (Cache.TryGetValue(key, out var cached) && now - cached.ticks < CacheTtlMs)
            {
                return cached.result;
            }

            var result = QueryWindowExisted(windowName, matchFullName);
            Cache[key] = (result, now);
            return result;
        }

        private static bool QueryWindowExisted(string windowName, bool matchFullName)
        {
            try
            {
                // 获取Win+Tab预览中的窗口
                
                AutomationElementCollection windows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

                foreach (AutomationElement window in windows)
                {
                    //LogHelper.WriteLogToFile("" + window.Current.Name);

                    string windowTitle;
                    try
                    {
                        windowTitle = window.Current.Name;
                    }
                    catch (System.Windows.Automation.ElementNotAvailableException)
                    {
                        // 窗口在枚举过程中被销毁，跳过
                        continue;
                    }

                    // 如果窗口标题包含 windowName，则进行检查
                    if (!string.IsNullOrEmpty(windowTitle) && windowTitle.Contains(windowName))
                    {
                        if (matchFullName)
                        {
                            if (windowTitle.Length == windowName.Length)
                            {
                                WindowPattern windowPattern;
                                try
                                {
                                    windowPattern = window.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
                                }
                                catch (System.Windows.Automation.ElementNotAvailableException)
                                {
                                    // 窗口在获取模式时被销毁，跳过
                                    continue;
                                }
                                if (windowPattern != null)
                                {
                                    return true;
                                }
                            }
                        }
                        else
                        {
                            WindowPattern windowPattern;
                            try
                            {
                                windowPattern = window.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
                            }
                            catch (System.Windows.Automation.ElementNotAvailableException)
                            {
                                // 窗口在获取模式时被销毁，跳过
                                continue;
                            }
                            if (windowPattern != null)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (System.Windows.Automation.ElementNotAvailableException)
            {
                // 枚举过程中窗口被销毁，视为未找到
            }
            return false;
        }
    }
}
