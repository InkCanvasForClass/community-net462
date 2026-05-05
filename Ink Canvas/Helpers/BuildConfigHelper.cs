using System;
using System.Reflection;
using System.Windows;

namespace Ink_Canvas.Helpers
{
    public static class BuildConfigHelper
    {
        public static bool IsMinimized
        {
            get
            {
#if MINIMIZE
                return true;
#else
                return false;
#endif
            }
        }

        public static bool IsResourceAvailable(string resourcePath)
        {
            if (IsMinimized) return false;

            try
            {
                var uri = new Uri(resourcePath, UriKind.Relative);
                var streamInfo = Application.GetResourceStream(uri);
                return streamInfo != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
