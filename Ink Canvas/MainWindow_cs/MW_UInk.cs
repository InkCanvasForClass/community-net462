using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using InkUInk;
using InkUInk.Model;

namespace Ink_Canvas
{
    /// <summary>
    /// UInk 1.1：导出当前场景的墨迹副本，或把文件中的单页导入到当前画布。
    /// </summary>
    public partial class MainWindow
    {
        public const string UInkFileExtension = ".uink";

        /// <summary>收集当前场景的全部页面（白板多页 / PPT 每页 / 单页批注）。</summary>
        private List<StrokeCollection> CollectUInkPages(out int currentPageIndex, out int workspaceType, out string workspaceName)
        {
            var pages = new List<StrokeCollection>();
            currentPageIndex = 0;

            // PPT 放映：每张幻灯片一页，按 pageIndex 绑定（binding = 1）。
            if (currentMode == 0 && IsInPPTPresentationMode && PresentationLink?.IsConnected == true)
            {
                workspaceType = UInkWorkspaceType.Presentation;
                workspaceName = "Presentation";

                int totalSlides = Math.Max(1, PresentationLink.TotalSlides);
                int currentSlide = Math.Max(1, _currentSlideShowPosition);
                currentPageIndex = Math.Min(totalSlides - 1, currentSlide - 1);

                for (int i = 1; i <= totalSlides; i++)
                {
                    if (i == currentSlide)
                    {
                        pages.Add(inkCanvas.Strokes.Clone());
                        continue;
                    }

                    var slideStrokes = _singlePPTInkManager?.LoadSlideStrokes(i);
                    pages.Add(slideStrokes != null && slideStrokes.Count > 0 ? slideStrokes : new StrokeCollection());
                }

                return pages;
            }

            // 白板：多页时逐页收集，当前页取画布内容，其余从历史恢复。
            if (currentMode != 0 && WhiteboardTotalCount > 1)
            {
                workspaceType = UInkWorkspaceType.Whiteboard;
                workspaceName = "Whiteboard";
                currentPageIndex = Math.Max(0, Math.Min(WhiteboardTotalCount - 1, CurrentWhiteboardIndex - 1));

                for (int i = 1; i <= WhiteboardTotalCount; i++)
                {
                    if (i == CurrentWhiteboardIndex)
                    {
                        pages.Add(inkCanvas.Strokes.Clone());
                    }
                    else if (TimeMachineHistories[i] != null)
                    {
                        pages.Add(ApplyHistoriesToNewStrokeCollection(TimeMachineHistories[i]));
                    }
                    else
                    {
                        pages.Add(new StrokeCollection());
                    }
                }

                return pages;
            }

            // 单页：屏幕批注或单页白板。
            workspaceType = currentMode == 0 ? UInkWorkspaceType.ScreenAnnotation : UInkWorkspaceType.Whiteboard;
            workspaceName = currentMode == 0 ? "Annotation" : "Whiteboard";
            pages.Add(inkCanvas.Strokes.Clone());
            return pages;
        }

        private UInkDocument BuildCurrentUInkDocument()
        {
            int currentPageIndex;
            int workspaceType;
            string workspaceName;
            var pages = CollectUInkPages(out currentPageIndex, out workspaceType, out workspaceName);

            double dpiScale = VisualTreeHelper.GetDpi(inkCanvas).DpiScaleX;

            var options = new UInkBuildOptions
            {
                WorkspaceType = workspaceType,
                WorkspaceName = workspaceName,
                CurrentPageIndex = currentPageIndex,
                DpiScale = dpiScale,
                ScreenWidth = (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpiScale),
                ScreenHeight = (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpiScale)
            };

            return UInkStrokeConverter.Build(pages, options);
        }

        /// <summary>导出当前场景的独立副本，不覆盖已有 UInk 文件。</summary>
        private void ExportUInkTo(string path)
        {
            if (File.Exists(path))
                path = Path.ChangeExtension(path, null) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + UInkFileExtension;

            var document = BuildCurrentUInkDocument();

            // 默认按完整内容写出；写出前的字段校正由 UInkNormalizer 完成，
            // 被修正或丢弃的内容通过 Warnings 反馈，避免静默丢数据。
            var options = new UInkWriteOptions { Overwrite = false };
            UInkWriter.WriteFile(document, path, options);

            foreach (string warning in options.Warnings)
            {
                LogHelper.WriteLogToFile("UInk 导出 | " + warning, LogHelper.LogType.Info);
            }
        }

        /// <summary>
        /// 打开 .uink 文件。多页文件载入 currentPageIndex 指向的页（保留页不参与正常页序）。
        /// </summary>
        public bool OpenUInkFile(string path)
        {
            if (TryBlockFrozenPageMutation(MainWindowStrings.Main_Freeze_Action_OpenInkFile)) return false;

            UInkDocument document;
            try
            {
                document = UInkReader.ReadFile(path);
            }
            catch (UInkFormatException ex)
            {
                ShowNotification("不是有效的 UInk 1.1 文件：" + ex.Message);
                return false;
            }

            var visiblePages = new List<UInkPage>();
            foreach (var page in document.Pages)
            {
                if (!page.IsRetained) visiblePages.Add(page);   // CANVAS-11：保留页不放进正常页序
            }

            if (visiblePages.Count == 0)
            {
                ShowNotification("UInk 文件里没有可显示的页面");
                return false;
            }

            var pageToImport = visiblePages[0];
            var workspace = document.GetWorkspace(pageToImport.WorkspaceGuid);
            if (workspace != null && workspace.CurrentPageIndex < (uint)visiblePages.Count)
            {
                var requestedPage = visiblePages[(int)workspace.CurrentPageIndex];
                if (string.Equals(requestedPage.WorkspaceGuid, workspace.Guid, StringComparison.OrdinalIgnoreCase))
                    pageToImport = requestedPage;
            }

            var strokes = UInkStrokeConverter.ToStrokeCollection(pageToImport, out string warning,
                VisualTreeHelper.GetDpi(inkCanvas).DpiScaleX);

            var previousStrokes = new StrokeCollection(inkCanvas.Strokes);
            if (previousStrokes.Count > 0 || strokes.Count > 0)
            {
                var previousCommitType = _currentCommitType;
                _currentCommitType = CommitReason.CodeInput;
                try
                {
                    if (previousStrokes.Count == 0) inkCanvas.Strokes.Add(strokes);
                    else if (strokes.Count == 0) inkCanvas.Strokes.Clear();
                    else inkCanvas.Strokes.Replace(previousStrokes, strokes);
                }
                finally
                {
                    _currentCommitType = previousCommitType;
                }

                // 使用已有的「旧笔迹 → 新笔迹」历史项，一次撤销整个导入。
                timeMachine.CommitStrokeShapeHistory(previousStrokes, strokes);
                MarkCurrentPageInkChanged();
            }

            if (!string.IsNullOrEmpty(warning)) ShowNotification(warning);

            if (document.Pages.Count > 1)
                ShowNotification(string.Format(MainWindowStrings.Main_UInk_ImportedSinglePage,
                    document.Pages.IndexOf(pageToImport) + 1, document.Pages.Count));

            if (document.Status != UInkReadStatus.Ok)
                ShowNotification("文件不完整，已尽力恢复可读内容");

            if (document.HasUnknownContent && document.Warnings.Count > 0)
                LogHelper.WriteLogToFile("UInk 回退：" + string.Join("; ", document.Warnings), LogHelper.LogType.Warning);

            return true;
        }
    }
}
