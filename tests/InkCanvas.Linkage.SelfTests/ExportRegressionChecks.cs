using Ink_Canvas;
using Ink_Canvas.Helpers;
using Ink_Canvas.ViewModels;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;

namespace LinkageSelfTests
{
    internal static class ExportRegressionChecks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        internal static async Task Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "opencode", "linkage-self-tests", Guid.NewGuid().ToString("N"));
            var settings = MainWindow.Settings.Automation;
            string previousRoot = settings.AutoSavedStrokesLocation;
            bool previousXml = settings.IsSaveStrokesAsXML;
            bool previousFull = settings.IsSaveFullPageStrokes;
            bool previousCustom = settings.IsUseCustomSaveFileName;
            using (var source = new FakeSource())
            using (var vm = new PresentationLinkViewModel(Dispatcher.CurrentDispatcher, ex => throw ex, () => { }, () => { }, _ => { }, _ => { }))
            using (var ink = new PPTInkManager { AutoSaveLocation = root })
            {
                // Exercise real export handlers without starting the window, automation or Office.
                var host = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
                typeof(DispatcherObject).GetField("_dispatcher", Fields).SetValue(host, Dispatcher.CurrentDispatcher);
                var canvas = new InkCanvas { Width = 200, Height = 200, Strokes = Strokes(50) };
                canvas.Measure(new Size(200, 200));
                canvas.Arrange(new Rect(0, 0, 200, 200));
                Set(host, "inkCanvas", canvas);
                Set(host, "_singlePPTInkManager", ink);
                Set(host, "_pptManager", source);
                Set(host, "<PresentationLink>k__BackingField", vm);
                Set(host, "_currentSlideShowPosition", 2);
                Set(host, "currentMode", 1);
                Set(host, "_autoSavePathValidated", true);
                Set(host, "CurrentWhiteboardIndex", 2);
                Set(host, "WhiteboardTotalCount", 2);
                var histories = new TimeMachineHistory[101][];
                histories[1] = new[] { new TimeMachineHistory(Strokes(80), TimeMachineHistoryType.UserInput, false) };
                histories[2] = new[] { new TimeMachineHistory(Strokes(90), TimeMachineHistoryType.UserInput, false) };
                Set(host, "TimeMachineHistories", histories);
                typeof(MainWindow).GetProperty("IsInPPTPresentationMode", Fields).SetValue(host, true);
                try
                {
                    source.StartMonitoring();
                    source.TryStartSlideShow();
                    vm.Attach(source);
                    await vm.WhenIdle;
                    ink.InitializePresentation(source.State.Presentation);
                    for (int page = 1; page <= 3; page++) ink.ForceSaveSlideStrokes(page, Strokes(page * 10));
                    settings.IsUseCustomSaveFileName = false;
                    foreach (string format in new[] { "XML", "FullPage", "Binary" })
                    {
                        settings.AutoSavedStrokesLocation = Path.Combine(root, format);
                        settings.IsSaveStrokesAsXML = format == "XML";
                        settings.IsSaveFullPageStrokes = format == "FullPage";
                        Save(host);
                        string folder = Path.Combine(settings.AutoSavedStrokesLocation, "User Saved - BlackBoard Strokes");
                        if (format == "Binary")
                        {
                            var files = Directory.GetFiles(folder, "*.icstk").OrderBy(path => path).ToArray();
                            Assert(files.Length == 2, "Binary whiteboard overlay export must contain board pages, not PPT pages.");
                            for (int page = 1; page <= 2; page++)
                                using (var stream = File.OpenRead(files[page - 1])) CheckStroke(new StrokeCollection(stream), page == 1 ? 80 : 50);
                        }
                        else
                        {
                            var files = Directory.GetFiles(folder, "*.zip");
                            Assert(files.Length == 1, format + " whiteboard overlay export must produce one board ZIP.");
                            using (var zip = ZipFile.OpenRead(files[0]))
                            {
                                using (var reader = new StreamReader(zip.GetEntry("metadata.txt").Open()))
                                {
                                    string metadata = reader.ReadToEnd();
                                    Assert(metadata.Contains("总页数: 2") && !metadata.Contains("PPT放映"), "Board archive metadata must not describe PPT pages.");
                                }
                                for (int page = 1; page <= 2; page++)
                                {
                                    double expected = page == 1 ? 80 : 50;
                                    using (var stream = zip.GetEntry($"page_{page:D4}." + (format == "XML" ? "xml" : "icstk")).Open())
                                    {
                                        if (format == "XML")
                                            Assert((double)XDocument.Load(stream).Descendants("StylusPoint").First().Attribute("X") == expected,
                                                "XML board export must include stored pages and current unsaved ink, not PPT ink.");
                                        else CheckStroke(new StrokeCollection(stream), expected);
                                    }
                                    if (format == "FullPage") Assert(zip.GetEntry($"page_{page:D4}.png").Length > 0, "Full-page board export must retain page images.");
                                }
                            }
                        }
                        for (int page = 1; page <= 3; page++) CheckStroke(ink.LoadSlideStrokes(page), page * 10);
                    }
                    Set(host, "CurrentWhiteboardIndex", 1);
                    Set(host, "WhiteboardTotalCount", 1);
                    foreach (string format in new[] { "XML", "FullPage", "Binary" })
                    {
                        bool xml = format == "XML";
                        settings.AutoSavedStrokesLocation = Path.Combine(root, "Single" + format);
                        settings.IsSaveStrokesAsXML = xml;
                        settings.IsSaveFullPageStrokes = format == "FullPage";
                        Save(host);
                        string folder = Path.Combine(settings.AutoSavedStrokesLocation, "User Saved - BlackBoard Strokes");
                        var files = Directory.GetFiles(folder, xml ? "*.xml" : "*.icstk");
                        Assert(files.Length == 1, "Single-page whiteboard overlay export must not expand into PPT pages.");
                        using (var stream = File.OpenRead(files[0]))
                        {
                            if (xml) Assert((double)XDocument.Load(stream).Descendants("StylusPoint").First().Attribute("X") == 50, "Single XML export must use board ink.");
                            else CheckStroke(new StrokeCollection(stream), 50);
                        }
                        if (format == "FullPage") Assert(Directory.GetFiles(folder, "*.png").Length == 1,
                            "Single-page full-board export must retain its PNG without exporting PPT pages.");
                        for (int page = 1; page <= 3; page++) CheckStroke(ink.LoadSlideStrokes(page), page * 10);
                    }
                }
                finally
                {
                    settings.AutoSavedStrokesLocation = previousRoot;
                    settings.IsSaveStrokesAsXML = previousXml;
                    settings.IsSaveFullPageStrokes = previousFull;
                    settings.IsUseCustomSaveFileName = previousCustom;
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
            }
        }

        private static StrokeCollection Strokes(double x) => new StrokeCollection
        {
            new Stroke(new StylusPointCollection { new StylusPoint(x, x), new StylusPoint(x + 10, x + 10) })
        };
        private static void CheckStroke(StrokeCollection strokes, double x) => Assert(strokes.Count == 1 && Math.Abs(strokes[0].StylusPoints[0].X - x) < 0.1,
            "Export must preserve board ink without reading or overwriting cached PPT ink.");
        private static void Set(object host, string name, object value) => typeof(MainWindow).GetField(name, Fields).SetValue(host, value);
        private static void Save(MainWindow host) => typeof(MainWindow).GetMethod("SaveInkCanvasStrokes", Fields).Invoke(host, new object[] { false, true });
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
