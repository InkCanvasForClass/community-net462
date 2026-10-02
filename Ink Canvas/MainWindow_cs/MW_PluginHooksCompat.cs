using System;
using System.Windows.Ink;
using System.Windows.Media;
using Ink_Canvas.Plugins;

namespace Ink_Canvas
{
    // ponytail: net462 分支不含插件子系统；此文件用空实现兜住 net10 核心流程里对插件钩子的调用，
    // 钩子成为 no-op。若日后恢复插件系统，删除此文件并恢复被剥离的 partial 实现。
    public partial class MainWindow
    {
        internal bool IsWhiteboardMode => currentMode == 1;

        internal event EventHandler<WhiteboardPageChangingEventArgs> PluginWhiteboardPageChanging;
        internal event EventHandler<WhiteboardPageChangedEventArgs> PluginWhiteboardDocumentChanged;
        internal event EventHandler<WhiteboardPageRemovedEventArgs> PluginWhiteboardPageRemoved;
        internal event EventHandler PluginWhiteboardPageClearing;
        internal event EventHandler<CanvasViewportTransformEventArgs> PluginCanvasViewportTransformChanged;
        internal event EventHandler<CanvasLineFinalizedEventArgs> PluginCanvasLineFinalized;

        private void BeginPluginWhiteboardPageChange(int targetIndex, string targetPageId = null) { }
        private void CompletePluginWhiteboardPageChange(int previousIndex) { }
        private string CreatePluginWhiteboardPageId() => null;
        private void InsertPluginWhiteboardPageId(int pageIndex, string newPageId) { }
        private WhiteboardPageInfo RemovePluginWhiteboardPageId(int pageIndex, int oldPageCount) => null;
        private void NotifyPluginWhiteboardPageRemoved(WhiteboardPageInfo removedPage) { }
        private void NotifyPluginWhiteboardPageClearing() { }
        private WhiteboardPageInfo CreatePluginWhiteboardPageInfo(int pageIndex, string explicitPageId = null) => null;
        private void CaptureCurrentPluginPageStates() { }
        private void RestoreCurrentPluginPageStates() { }
        private void SavePluginPageDocumentSidecar(string documentPath, int pageIndex) { }
        private bool HasPluginPageState(int pageIndex) => false;
        private void LoadPluginPageDocumentSidecar(string documentPath) { }
        private void SavePluginDocumentStateToDirectory(string directory) { }
        private void LoadPluginDocumentStateFromDirectory(string directory) { }
        private void PublishPluginCanvasViewportTransform(Matrix delta) { }
        private void CompletePluginCanvasViewportTransform() { }
        private void PublishPluginCanvasLineCandidate(Stroke stroke, CanvasLineSource source) { }
        private void ApplyPluginUndoState(string pluginId, string state) { }
    }
}

namespace Ink_Canvas.Plugins
{
    public sealed class WhiteboardPageInfo
    {
        public string Id { get; set; }
        public int Index { get; set; }
        public int Count { get; set; }
        public bool IsFrozen { get; set; }
    }

    public sealed class WhiteboardPageChangingEventArgs : EventArgs
    {
        public WhiteboardPageInfo CurrentPage { get; set; }
        public WhiteboardPageInfo TargetPage { get; set; }
    }

    public sealed class WhiteboardPageChangedEventArgs : EventArgs
    {
        public WhiteboardPageInfo PreviousPage { get; set; }
        public WhiteboardPageInfo CurrentPage { get; set; }
    }

    public sealed class WhiteboardPageRemovedEventArgs : EventArgs
    {
        public WhiteboardPageInfo RemovedPage { get; set; }
        public WhiteboardPageInfo CurrentPage { get; set; }
    }

    public enum CanvasLineSource
    {
        GeometryLine = 0,
        AutoStraightenedInk = 1
    }

    public sealed class CanvasLineFinalizedEventArgs : EventArgs
    {
        public string CandidateToken { get; set; }
        public string PageId { get; set; }
        public System.Windows.Point Start { get; set; }
        public System.Windows.Point End { get; set; }
        public CanvasLineSource Source { get; set; }
    }

    public sealed class CanvasViewportTransformEventArgs : EventArgs
    {
        public Matrix Delta { get; set; } = Matrix.Identity;
        public bool IsCompleted { get; set; }
    }
}
