using Microsoft.Office.Interop.PowerPoint;
using System;
using System.Timers;

namespace InkCanvasLinkage.Core.Sources
{
    internal sealed class EnhancedPowerPointSession : IDisposable
    {
        private readonly StaDispatcher _dispatcher;
        private readonly ComPPTLinkManager _backend;
        private readonly LogHelper _log;
        private Application _ownedApplication;
        private Timer _timer;
        public EnhancedPowerPointSession(StaDispatcher dispatcher, ComPPTLinkManager backend, LogHelper log)
        { _dispatcher = dispatcher; _backend = backend; _log = log; }

        public bool SetEnabled(bool enabled)
        {
            if (!enabled) { Dispose(); return true; }
            if (_timer != null) return true;
            _timer = new Timer(1000);
            _timer.Elapsed += (s, e) => _dispatcher.Post(() => { if (_timer != null) EnsureApplication(); });
            _timer.Start();
            EnsureApplication();
            return true;
        }

        private void EnsureApplication()
        {
            try
            {
                if (_ownedApplication != null)
                {
                    try { var name = _ownedApplication.Name; return; }
                    catch { PresentationLinkSnapshot.Release(_ownedApplication); _ownedApplication = null; }
                }
                if (_backend.IsConnected) return;
                _ownedApplication = new Application();
                _ownedApplication.WindowState = PpWindowState.ppWindowMinimized;
                _backend.AttachApplication(_ownedApplication);
            }
            catch (Exception ex) { _log.WriteLogToFile($"PowerPoint 增强监控失败: {ex}", LogHelper.LogType.Warning); }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
            if (_ownedApplication == null) return;
            try
            {
                // Never close documents opened by the user, including unsaved ones.
                object presentations = _ownedApplication.Presentations;
                try { if (((dynamic)presentations).Count == 0) _ownedApplication.Quit(); }
                finally { PresentationLinkSnapshot.Release(presentations); }
            }
            catch (Exception ex) { _log.WriteLogToFile($"释放增强实例失败: {ex}", LogHelper.LogType.Warning); }
            finally { PresentationLinkSnapshot.Release(_ownedApplication); _ownedApplication = null; }
        }
    }
}
