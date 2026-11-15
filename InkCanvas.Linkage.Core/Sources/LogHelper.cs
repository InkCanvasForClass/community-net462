using System;

namespace InkCanvasLinkage.Core.Sources
{
    internal sealed class LogHelper
    {
        internal enum LogType { Trace, Info, Event, Warning, Error }
        private readonly Action<string, PresentationLogLevel> _write;
        public LogHelper(Action<string, PresentationLogLevel> write) { _write = write; }
        public void WriteLogToFile(string message, LogType level = LogType.Info)
        {
            // A failing host logger must not interrupt COM cleanup or navigation.
            try { _write?.Invoke(message, (PresentationLogLevel)level); }
            catch { }
        }
    }
}
