using InkCanvasLinkage.Core;
using System;
using System.Runtime.InteropServices;

namespace InkCanvasLinkage.Core.Sources
{
    internal static class PresentationLinkSnapshot
    {
        public static PresentationMetadata ReadMetadata(object presentation, LogHelper log = null)
        {
            if (presentation == null) return null;
            object slides = null;
            try
            {
                dynamic pres = presentation;
                string name = pres.Name;
                string fullName = pres.FullName;
                slides = pres.Slides;
                int count = ((dynamic)slides).Count;
                return new PresentationMetadata(name, fullName, count);
            }
            catch (Exception ex)
            {
                log?.WriteLogToFile($"读取演示文稿元数据失败: {ex}", LogHelper.LogType.Warning);
                return null;
            }
            finally
            {
                Release(slides);
            }
        }

        public static int ReadSlideNumber(object window, Func<int> fallback, LogHelper log = null)
        {
            object view = null;
            try
            {
                if (window != null)
                {
                    view = ((dynamic)window).View;
                    int number = ((dynamic)view).CurrentShowPosition;
                    if (number > 0) return number;
                }
            }
            catch (Exception ex)
            {
                log?.WriteLogToFile($"读取放映页码失败，将使用联动源查询: {ex.Message}", LogHelper.LogType.Warning);
            }
            finally
            {
                Release(view);
            }

            return Math.Max(0, fallback());
        }

        public static void Release(object value)
        {
            try
            {
                if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"释放联动快照 COM 对象失败: {ex.Message}");
            }
        }
    }
}
