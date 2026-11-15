using Ink_Canvas;
using Ink_Canvas.ViewModels;
using Ink_Canvas.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LinkageSelfTests
{
    internal static class ImageRegressionChecks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        internal static async Task Run()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            string root = Path.Combine(Path.GetTempPath(), "opencode", "linkage-self-tests", Guid.NewGuid().ToString("N"));
            string previousRoot = MainWindow.Settings.Automation.AutoSavedStrokesLocation;
            var errors = new List<Exception>();
            using (var source = new FakeSource())
            using (var replacement = new FakeSource())
            using (var vm = new PresentationLinkViewModel(dispatcher, errors.Add, () => { }, () => { }, _ => { }, _ => { }))
            {
                // Initialize only what these real handlers need; no application, audio, automation or Office startup.
                var host = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
                var panel = (PPTQuickPanel)FormatterServices.GetUninitializedObject(typeof(PPTQuickPanel));
                typeof(DispatcherObject).GetField("_dispatcher", Fields).SetValue(host, dispatcher);
                typeof(DispatcherObject).GetField("_dispatcher", Fields).SetValue(panel, dispatcher);
                var canvas = new InkCanvas();
                var images = new Dictionary<FrameworkElement, int>();
                var paths = new Dictionary<int, List<string>>();
                var loading = new HashSet<int>();
                Set(host, "inkCanvas", canvas);
                Set(host, "<PresentationLink>k__BackingField", vm);
                Set(panel, "_mainWindow", host);
                Set(panel, "_pptImages", images);
                Set(panel, "_pptImagePaths", paths);
                Set(panel, "_loadingSlides", loading);
                Set(panel, "_imagePathsIoLock", new object());
                MainWindow.Settings.Automation.AutoSavedStrokesLocation = root;
                try
                {
                    vm.Attach(source);
                    Call(panel, "SubscribeToPPTEvents", source);
                    await Task.Run(() => { source.StartMonitoring(); source.TryStartSlideShow(); });
                    await vm.WhenIdle;
                    Assert(vm.CurrentSlide == 1 && errors.Count == 0, "Background begin must reach QuickPanel without blocking the shared queue.");
                    for (int attempt = 0; loading.Count != 0 && attempt < 1000; attempt++) await Task.Delay(1);
                    Assert(loading.Count == 0, "Initial independent load must finish before seeding saved image paths.");

                    var image = new Image { Tag = "already-inserted.png", Visibility = Visibility.Visible };
                    canvas.Children.Add(image);
                    images[image] = 1;
                    string folder = Path.Combine(root, "Auto Saved - Presentations", "lesson.pptx_3");
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "0001.images.json"), "[\"already-inserted.png\",\"already-inserted.png\"]");
                    paths.Remove(1);
                    await Load(panel, 1, Generation(panel), vm.Generation);
                    Assert(canvas.Children.Count == 1 && paths[1].Count == 2,
                        "Real JSON/image loader must recognize the existing page image without copying or inserting duplicates.");
                    int pageGeneration = Generation(panel);
                    await Task.Run(() => source.TryNavigateToSlide(2));
                    await vm.WhenIdle;
                    Assert(vm.CurrentSlide == 2 && image.Visibility == Visibility.Collapsed && Generation(panel) != pageGeneration,
                        "Background page change must hide previous-page images without awaiting their loading.");
                    await Load(panel, 1, pageGeneration, vm.Generation);
                    await Task.Run(() => source.TryNavigateToSlide(1));
                    await vm.WhenIdle;
                    Assert(image.Visibility == Visibility.Visible && canvas.Children.Count == 1,
                        "Returning to a loaded page must reuse its existing image.");

                    // Keep the independent loader queued while exercising the real synchronous event handlers.
                    int posted = 0;
                    DispatcherHookEventHandler observe = (_, args) =>
                    {
                        if (args.Operation.Priority == DispatcherPriority.Background) posted++;
                    };
                    dispatcher.Hooks.OperationPosted += observe;
                    int queuedGeneration;
                    try
                    {
                        using (dispatcher.DisableProcessing())
                        {
                            Call(panel, "OnPPTSlideShowEnd", source.State);
                            Assert(((Task)Call(panel, "OnPPTSlideShowBegin", source.State)).IsCompleted,
                                "Begin subscriber must finish synchronously while image work remains queued.");
                            queuedGeneration = Generation(panel);
                            Assert(((Task)Call(panel, "OnPPTSlideChanged", source.State)).IsCompleted
                                && ((Task)Call(panel, "OnPPTSlideShowBegin", source.State)).IsCompleted,
                                "Page and duplicate begin subscribers must not await image work.");
                            Assert(posted == 1 && loading.Count == 1 && Generation(panel) == queuedGeneration,
                                "Repeated notifications for the same session/page must share one pending image load.");
                            Assert(((Task)Call(panel, "OnPPTSlideShowEnd", source.State)).IsCompleted
                                && image.Visibility == Visibility.Collapsed && Generation(panel) != queuedGeneration && loading.Count == 0,
                                "End must hide existing images and invalidate the pending load immediately.");
                        }
                    }
                    finally { dispatcher.Hooks.OperationPosted -= observe; }
                    await Load(panel, 1, queuedGeneration, vm.Generation);
                    Assert(image.Visibility == Visibility.Collapsed && canvas.Children.Count == 1,
                        "An actual stale loader must neither insert nor redisplay images after end.");

                    await Task.Run(() => source.TryEndSlideShow());
                    await vm.WhenIdle;
                    image.Visibility = Visibility.Visible;
                    int generation = Generation(panel);
                    await Task.Run(() => source.StopMonitoring());
                    await vm.WhenIdle;
                    Assert(image.Visibility == Visibility.Collapsed && Generation(panel) != generation,
                        "Background close/disconnect subscriptions must hide images and invalidate image work.");

                    source.StartMonitoring();
                    source.TryStartSlideShow();
                    await vm.WhenIdle;
                    images[image] = 1;
                    image.Visibility = Visibility.Visible;
                    generation = Generation(panel);
                    int sourceGeneration = vm.Generation;
                    await Task.Run(() => Call(panel, "SubscribeToPPTEvents", (object)null));
                    Assert(image.Visibility == Visibility.Collapsed && Generation(panel) != generation && loading.Count == 0,
                        "Unsubscription from a worker must hide images on the dispatcher and invalidate pending work.");
                    int detachedGeneration = Generation(panel);
                    await Task.Run(() => source.TryNavigateToSlide(2));
                    await vm.WhenIdle;
                    Assert(Generation(panel) == detachedGeneration && loading.Count == 0,
                        "Detached QuickPanel must ignore later background page events.");

                    vm.Attach(replacement);
                    Call(panel, "SubscribeToPPTEvents", replacement);
                    replacement.StartMonitoring();
                    replacement.TryStartSlideShow();
                    await vm.WhenIdle;
                    await Load(panel, 1, generation, sourceGeneration);
                    Assert(canvas.Children.Count == 1 && image.Visibility == Visibility.Collapsed,
                        "Same-page replacement must reject an old source/session loader without showing old images.");
                    for (int attempt = 0; loading.Count != 0 && attempt < 1000; attempt++) await Task.Delay(1);
                    Assert(loading.Count == 0, "Existing independent loads must finish before the path-write regression.");
                    await CheckPathWrites(panel, vm, images, paths, image, folder);
                    await CheckReadFailure(panel, vm, paths, folder);
                    await CheckPathIoSerialization(panel, paths, folder);
                    await CheckInsertedImageInitialization(panel, host, replacement, vm, canvas);
                    await CheckScreenshotGuards(panel, host, replacement, vm, canvas);
                    Assert(errors.Count == 0, "QuickPanel events must not fault or strand the shared ViewModel queue.");
                }
                finally
                {
                    Call(panel, "SubscribeToPPTEvents", (object)null);
                    MainWindow.Settings.Automation.AutoSavedStrokesLocation = previousRoot;
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
            }
        }

        private static async Task CheckPathWrites(PPTQuickPanel panel, PresentationLinkViewModel vm,
            Dictionary<FrameworkElement, int> images, Dictionary<int, List<string>> paths, Image oldImage, string folder)
        {
            string jsonFile = Path.Combine(folder, "0001.images.json");
            string oldPath = (string)oldImage.Tag;
            string addedPath = Path.Combine(folder, "new-screenshot.png");
            paths.Clear();
            images[oldImage] = 1;
            File.WriteAllText(jsonFile, JsonConvert.SerializeObject(new[] { oldPath }));
            await WhileReading(panel, vm, () =>
            {
                Call(panel, "AddPPTImagePath", 1, addedPath);
                var saved = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(jsonFile));
                Assert(saved.SequenceEqual(new[] { oldPath, addedPath }),
                    "The real shared screenshot/image save path must load the old page list before its first append.");
            });
            Assert(paths[1].SequenceEqual(new[] { oldPath, addedPath }),
                "Completing an older JSON read must not overwrite paths added while it was pending.");

            Call(panel, "RemoveImageFromPPT", oldImage);
            Call(panel, "SavePPTImagePaths", 1);
            Assert(JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(jsonFile)).SequenceEqual(new[] { addedPath }),
                "Deleting one image and saving again must not merge the deleted path back from disk.");
            var addedImage = new Image { Tag = addedPath };
            images[addedImage] = 1;
            await WhileReading(panel, vm, () =>
            {
                Call(panel, "RemoveImageFromPPT", addedImage);
                Assert(paths.ContainsKey(1) && paths[1].Count == 0 && !File.Exists(jsonFile),
                    "Deleting the last image during a pending read must delete JSON but retain an empty cache marker.");
            });
            Assert(paths[1].Count == 0 && !File.Exists(jsonFile),
                "The actual older loader must not resurrect the last deleted path or JSON file.");
            string laterPath = Path.Combine(folder, "later-screenshot.png");
            Call(panel, "AddPPTImagePath", 1, laterPath);
            Call(panel, "SavePPTImagePaths", 1);
            Assert(JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(jsonFile)).SequenceEqual(new[] { laterPath }),
                "Appending after a last-image deletion must preserve the deletion, not restore either older path.");
        }

        private static async Task WhileReading(PPTQuickPanel panel, PresentationLinkViewModel vm, Action mutate)
        {
            var previousSettings = JsonConvert.DefaultSettings;
            var gate = new PausedListRead();
            Task pending = null;
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings { Converters = { gate } };
            try
            {
                pending = Load(panel, 1, Generation(panel), vm.Generation);
                Assert(await Task.WhenAny(gate.Entered.Task, pending) == gate.Entered.Task && !pending.IsCompleted,
                    "The real image loader must be paused after reading old JSON, before returning its snapshot.");
                mutate();
            }
            finally
            {
                gate.Release.TrySetResult(true);
                JsonConvert.DefaultSettings = previousSettings;
                if (pending != null) await pending;
            }
        }

        private static async Task CheckReadFailure(PPTQuickPanel panel, PresentationLinkViewModel vm,
            Dictionary<int, List<string>> paths, string folder)
        {
            string jsonFile = Path.Combine(folder, "0001.images.json");
            string json = "[\"preserved-image.png\"]";
            File.WriteAllText(jsonFile, json);
            paths.Clear();
            using (File.Open(jsonFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await Load(panel, 1, Generation(panel), vm.Generation);
                Assert(!paths.ContainsKey(1), "A failed JSON read must not become a cached empty page.");
                bool failed = false;
                try { Call(panel, "AddPPTImagePath", 1, "new-image.png"); }
                catch (IOException) { failed = true; }
                Assert(failed && !paths.ContainsKey(1), "First append after a read failure must abort without caching or saving.");
            }
            Assert(File.ReadAllText(jsonFile) == json, "A read failure must never overwrite the existing JSON file.");
            await Load(panel, 1, Generation(panel), vm.Generation);
            Assert(paths[1].SequenceEqual(new[] { "preserved-image.png" }), "A later successful read must restore the original paths.");
            Call(panel, "AddPPTImagePath", 1, "new-image.png");
            Assert(JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(jsonFile))
                .SequenceEqual(new[] { "preserved-image.png", "new-image.png" }), "Retrying append must preserve the original image path.");
        }

        private static async Task CheckPathIoSerialization(PPTQuickPanel panel, Dictionary<int, List<string>> paths, string folder)
        {
            string jsonFile = Path.Combine(folder, "0001.images.json");
            var gate = typeof(PPTQuickPanel).GetField("_imagePathsIoLock", Fields).GetValue(panel);
            foreach (string operation in new[] { "LoadPPTImagePaths", "SavePPTImagePaths", "DeletePPTImagePathsFile" })
            {
                File.WriteAllText(jsonFile, "[\"old-image.png\"]");
                paths[1] = new List<string> { "new-image.png" };
                Task<object> pending = null;
                bool blocked = false;
                using (var entered = new ManualResetEventSlim())
                {
                    // Model an open file held by another panel operation under the shared I/O gate.
                    lock (gate)
                    using (File.Open(jsonFile, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        pending = Task.Run(() =>
                        {
                            entered.Set();
                            return operation == "LoadPPTImagePaths" ? Call(panel, operation, 1, folder) : Call(panel, operation, 1);
                        });
                        Assert(entered.Wait(5000), "The competing image-path operation must start.");
                        blocked = !pending.Wait(200);
                    }
                    var result = await pending;
                    Assert(blocked, operation + " must wait for the shared I/O gate instead of failing on an open JSON file.");
                    if (operation == "LoadPPTImagePaths")
                        Assert(((List<string>)result).SequenceEqual(new[] { "old-image.png" }), "The serialized read must return the saved paths.");
                    else if (operation == "SavePPTImagePaths")
                        Assert(JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(jsonFile)).SequenceEqual(paths[1]),
                            "The serialized write must reach disk, not only update memory.");
                    else
                        Assert(!File.Exists(jsonFile), "The serialized delete must remove JSON after the reader closes it.");
                }
            }
        }

        private static async Task CheckInsertedImageInitialization(PPTQuickPanel panel, MainWindow host,
            FakeSource source, PresentationLinkViewModel vm, InkCanvas canvas)
        {
            Set(host, "timeMachine", new Ink_Canvas.Helpers.TimeMachine());
            var inserted = new Image { Width = 100, Height = 100 };
            await (Task)Call(panel, "InsertImageToMainWindow", inserted, null, true, 1);
            Assert(canvas.Children.Contains(inserted), "The real manual insertion must add the image before its Loaded callback.");
            int generation = Generation(panel);
            source.TryNavigateToSlide(2);
            await vm.WhenIdle;
            Assert(Generation(panel) != generation, "The test must cross a real page generation change.");
            inserted.Measure(new Size(100, 100));
            inserted.Arrange(new Rect(0, 0, 100, 100));
            inserted.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            inserted.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert(inserted.RenderTransform is System.Windows.Media.TransformGroup && inserted.IsManipulationEnabled
                && inserted.Cursor == System.Windows.Input.Cursors.Hand,
                "An inserted image must still receive transform and event setup after navigating away.");
            source.TryNavigateToSlide(1);
            await vm.WhenIdle;
            Assert(inserted.Visibility == Visibility.Visible && inserted.IsManipulationEnabled,
                "Returning to the page must reuse the initialized manual image.");
            var moved = new System.Windows.Media.TransformGroup();
            moved.Children.Add(new System.Windows.Media.TranslateTransform(45, 60));
            inserted.RenderTransform = moved;
            InkCanvas.SetLeft(inserted, 25);
            InkCanvas.SetTop(inserted, 35);
            canvas.Children.Remove(inserted);
            canvas.Children.Add(inserted);
            inserted.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert(ReferenceEquals(inserted.RenderTransform, moved) && InkCanvas.GetLeft(inserted) == 25 && InkCanvas.GetTop(inserted) == 35,
                "Restoring an inserted image must not reset its transform or recenter it on another Loaded event.");
            var eventStore = typeof(UIElement).GetProperty("EventHandlersStore", Fields).GetValue(inserted);
            var handlers = (Array)eventStore.GetType().GetMethod("GetRoutedEventHandlers", Fields)
                .Invoke(eventStore, new object[] { UIElement.MouseLeftButtonDownEvent });
            Assert(handlers.Length == 1, "Repeated Loaded events must not bind duplicate image interaction handlers.");
            var removed = new Image { Width = 100, Height = 100 };
            await (Task)Call(panel, "InsertImageToMainWindow", removed, null, true, 1);
            removed.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            canvas.Children.Remove(removed);
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert(!removed.IsManipulationEnabled && !(removed.RenderTransform is System.Windows.Media.TransformGroup),
                "Removing an image before its queued first initialization must still reject the callback.");
            var pendingDocument = new Image { Width = 100, Height = 100 };
            await (Task)Call(panel, "InsertImageToMainWindow", pendingDocument, null, true, 1);
            inserted.RenderTransform = System.Windows.Media.Transform.Identity;
            inserted.IsManipulationEnabled = false;
            source.ReplacePresentation(new InkCanvasLinkage.Core.PresentationMetadata("new-document.pptx", null, 3));
            await vm.WhenIdle;
            pendingDocument.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            inserted.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert(!inserted.IsManipulationEnabled && !(inserted.RenderTransform is System.Windows.Media.TransformGroup),
                "A new document must invalidate the old inserted image's Loaded callback.");
            Assert(!pendingDocument.IsManipulationEnabled && !(pendingDocument.RenderTransform is System.Windows.Media.TransformGroup),
                "A new document must reject the old image's first initialization, not only subsequent Loaded events.");
        }

        private static async Task CheckScreenshotGuards(PPTQuickPanel panel, MainWindow host,
            FakeSource source, PresentationLinkViewModel vm, InkCanvas canvas)
        {
            var history = (Ink_Canvas.Helpers.TimeMachine)typeof(MainWindow).GetField("timeMachine", Fields).GetValue(host);
            foreach (string change in new[] { "page", "end", "document", "detach" })
            {
                int generation = Generation(panel);
                var state = vm.State;
                Func<bool> canInsert = () => generation == Generation(panel) && ReferenceEquals(state, vm.State);
                Assert(canInsert(), "A new screenshot must initially belong to the current page/session.");
                int images = canvas.Children.Count, histories = history.ExportTimeMachineHistory().Length;
                using (var bitmap = new System.Drawing.Bitmap(1, 1))
                {
                    var ready = new TaskCompletionSource<bool>();
                    Func<Task> insert = async () =>
                    {
                        await ready.Task;
                        await (Task)Call(host, "InsertScreenshotToCanvas", bitmap, canInsert);
                        await (Task)Call(host, "InsertBitmapSourceToCanvas", null, null, null, canInsert);
                    };
                    var pending = insert();
                    if (change == "page") source.TryNavigateToSlide(2);
                    else if (change == "end") source.TryEndSlideShow();
                    else if (change == "document") source.ReplacePresentation(new InkCanvasLinkage.Core.PresentationMetadata("screenshot.pptx", null, 3));
                    else Call(panel, "SubscribeToPPTEvents", (object)null);
                    await vm.WhenIdle;
                    Assert(!canInsert(), "The screenshot's original page/session must be invalidated by " + change + ".");
                    ready.SetResult(true);
                    await pending;
                    Assert(canvas.Children.Count == images && history.ExportTimeMachineHistory().Length == histories,
                        "Late screen/camera images must not mutate the new canvas or history after " + change + ".");
                    bool disposed = false;
                    try { int width = bitmap.Width; }
                    catch (ArgumentException) { disposed = true; }
                    Assert(disposed, "Rejecting a stale bitmap screenshot must still release its native resources.");
                }
            }
        }

        private sealed class PausedListRead : JsonConverter
        {
            internal readonly TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _reads;
            public override bool CanConvert(Type objectType) => objectType == typeof(List<string>);
            public override bool CanWrite => false;
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var paths = JArray.Load(reader).Select(item => (string)item).ToList();
                if (Interlocked.Increment(ref _reads) == 1)
                {
                    Entered.TrySetResult(true);
                    Release.Task.GetAwaiter().GetResult();
                }
                return paths;
            }
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();
        }

        private static int Generation(PPTQuickPanel panel) => (int)typeof(PPTQuickPanel).GetField("_imageGeneration", Fields).GetValue(panel);
        private static Task Load(PPTQuickPanel panel, int slide, int generation, int sourceGeneration) =>
            (Task)Call(panel, "LoadPPTImages", slide, generation, sourceGeneration);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
        private static object Call(object target, string name, params object[] args)
        {
            try { return target.GetType().GetMethod(name, Fields).Invoke(target, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
