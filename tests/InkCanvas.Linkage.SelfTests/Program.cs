using Ink_Canvas.Helpers;
using Ink_Canvas.ViewModels;
using InkCanvasLinkage.Core;
using InkCanvasLinkage.Core.Sources;
using InkCanvasPPTAgent.Contracts;
using Microsoft.Office.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Threading;

namespace LinkageSelfTests
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                CheckContract();
                CheckSessions();
                CheckStaDispatcher();
                CheckComSnapshots();
                CheckComCapabilities();
                CheckRotHelperBinding();
                CheckComMonitoringStartup();
                CheckComCallbackDispatch();
                CheckAgentSnapshots();
                RunDispatcherCheck(CheckViewModel);
                RunDispatcherCheck(CheckFakeInkHost);
                CheckPPTStrokeArchive();
                CheckInkRoundTrip(@"C:\Lessons\lesson.pptx");
                CheckInkRoundTrip(null);
                Console.WriteLine("PASS: contract, session deduplication/reentry, STA, COM/Agent snapshots, shared ViewModel queue/source isolation, Fake ink lifecycle, ink/Position and binary/XML ZIP compatibility.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void CheckContract()
        {
            Throws<ArgumentException>(() => new PresentationMetadata("", null, 1));
            Throws<ArgumentOutOfRangeException>(() => new PresentationMetadata("lesson", null, -1));
            Throws<ArgumentOutOfRangeException>(() => new PresentationMetadata("lesson", null, int.MaxValue));
            Throws<ArgumentOutOfRangeException>(() => new PresentationLinkState(true, true, -1, 3));

            using (IPresentationLinkSource source = new FakeSource())
            {
                PresentationLinkState first = null;
                PresentationLinkState second = null;
                source.SlideShowNextSlide += state => first = state;
                source.SlideShowNextSlide += state => second = state;
                source.StartMonitoring();
                Assert(source.TryStartSlideShow(), "Fake show must start.");
                Assert(source.TryNavigateToSlide(2), "Fake navigation must succeed.");
                Assert(first.CurrentSlide == 2 && first.TotalSlides == 3, "Typed page snapshot must reach consumers.");
                Assert(ReferenceEquals(first, second), "Consumers must share the same snapshot.");
                Assert(source.TryNavigateNext() && first.CurrentSlide == 3, "Next must reach slide 3.");
                Assert(second.CurrentSlide == 3 && first.IsConnected && first.IsInSlideShow, "Show state must be captured.");
                Assert(!source.TryNavigateToSlide(0) && !source.TryNavigateNext(), "Invalid navigation must fail.");
                Assert(source.TryNavigatePrevious() && first.CurrentSlide == 2, "Previous must reach slide 2.");
                Assert(source.GetPresentationMetadata().Name == "lesson.pptx", "Metadata must not depend on source type.");
                Assert(source.TryEndSlideShow(), "Fake show must end.");
            }
        }

        private static void CheckComSnapshots()
        {
            var metadata = PresentationLinkSnapshot.ReadMetadata(new PresentationStub());
            Assert(metadata.Name == "lesson.pptx" && metadata.TotalSlides == 3, "COM metadata conversion must preserve fields.");
            Assert(PresentationLinkSnapshot.ReadMetadata(null) == null, "Missing presentation must remain unavailable.");
            Func<int> fallback = () => 2;
            Assert(PresentationLinkSnapshot.ReadSlideNumber(new WindowStub(), fallback) == 3, "Event page must precede fallback.");
            Assert(PresentationLinkSnapshot.ReadSlideNumber(null, fallback) == 2, "Unavailable view must use source fallback.");
            foreach (var mode in new[] { PresentationLinkMode.Com, PresentationLinkMode.Rot, PresentationLinkMode.Agent })
            using (var source = PresentationLinkSourceFactory.Create(mode))
            {
                Assert(source.GetPresentationMetadata() == null, "Disconnected source must not fabricate metadata.");
                Assert(source.Capabilities.HasFlag(PresentationCapabilities.Thumbnails), "Factory must declare thumbnail support.");
                Assert(source.Capabilities.HasFlag(PresentationCapabilities.EnhancedMonitoring) == (mode == PresentationLinkMode.Com),
                    "Only COM source owns enhanced monitoring.");
                Assert(!source.TryNavigateToSlide(1) && !source.TryNavigateNext() && !source.TryNavigatePrevious(),
                    "Disconnected source must reject navigation.");
            }
        }

        private static void CheckComCapabilities()
        {
            foreach (object enabled in new object[] { -1, MsoTriState.msoTrue })
            {
                object disabled = enabled is int ? (object)0 : MsoTriState.msoFalse;
                var transition = new TransitionStub { Hidden = enabled, AdvanceOnTime = enabled, AdvanceTime = 1 };
                var presentation = new TransitionPresentationStub(transition,
                    new TransitionStub { Hidden = disabled, AdvanceOnTime = disabled, AdvanceTime = 1 });
                var metadata = new PresentationMetadata("lesson.pptx", @"C:\Lessons\lesson.pptx", 2);
                var details = ComPresentationCapabilities.ReadDetails(presentation, metadata);
                Assert(details.HasHiddenSlides && details.HasAutoPlayTimings && details.FullName == metadata.FullName,
                    "Late-bound integer and enum values must both report hidden slides and positive autoplay timings.");
                transition.Hidden = disabled;
                transition.AdvanceTime = 0;
                details = ComPresentationCapabilities.ReadDetails(presentation, metadata);
                Assert(!details.HasHiddenSlides && !details.HasAutoPlayTimings,
                    "False flags and zero-duration transitions must not report hidden slides or autoplay.");
                transition.Hidden = enabled is int ? (object)(-2) : MsoTriState.msoTriStateMixed;
                Assert(!ComPresentationCapabilities.ReadDetails(presentation, metadata).HasHiddenSlides,
                    "Mixed tri-state must not count as true.");
            }

            var isMedia = typeof(ComPresentationCapabilities).GetMethod("IsMediaShape", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var shapeType in new[] { MsoShapeType.msoMedia, MsoShapeType.msoOLEControlObject,
                MsoShapeType.msoEmbeddedOLEObject, MsoShapeType.msoAutoShape })
            foreach (object value in new object[] { (int)shapeType, shapeType })
            {
                var shape = new ShapeTypeStub { Type = value, MediaType = 14 };
                Assert((bool)isMedia.Invoke(null, new object[] { shape }) == (shapeType != MsoShapeType.msoAutoShape),
                    "Late-bound shape classification must accept integer and enum values.");
                shape.MediaType = 0;
                if (shapeType == MsoShapeType.msoEmbeddedOLEObject)
                    Assert(!(bool)isMedia.Invoke(null, new object[] { shape }), "Non-media embedded OLE objects must remain excluded.");
            }
        }

        private static void CheckRotHelperBinding()
        {
            foreach (var backend in new[] { typeof(ROTPPTManager), typeof(PPTManager) })
                Assert(backend.GetField(nameof(PPTROTConnectionHelper), BindingFlags.Instance | BindingFlags.NonPublic) == null,
                    "Backend helper fields must not shadow the static ROT helper type during dynamic calls.");
            dynamic item = new object();
            dynamic other = new object();
            Assert(PPTROTConnectionHelper.AreComObjectsEqual(item, item)
                && !PPTROTConnectionHelper.AreComObjectsEqual(item, other)
                && !PPTROTConnectionHelper.AreComObjectsEqual(item, null),
                "ROT identity checks must bind static helpers with dynamic operands.");
            using (dynamic stream = new MemoryStream())
            {
                PPTROTConnectionHelper.SafeReleaseComObject(stream);
                Assert(stream.CanWrite, "ROT cleanup must ignore managed objects without disposing them.");
            }
            PPTROTConnectionHelper.SafeReleaseComObject(null);
            dynamic window = new RotWindowStub();
            Assert(PPTROTConnectionHelper.IsValidSlideShowWindow(window), "ROT window validation must bind the static helper.");
        }

        private static void CheckComMonitoringStartup()
        {
            using (var dispatcher = new StaDispatcher(null))
            {
                var probes = new List<string>();
                var manager = dispatcher.Invoke(() => new PPTManager(dispatcher, new PresentationLinkOptions
                {
                    DebugProbeEnabled = true,
                    Log = (message, _) =>
                    {
                        if (message.StartsWith("首次PPT连接检查完成:") || message.StartsWith("PPT连接重试完成:")) probes.Add(message);
                    }
                }));
                var fields = BindingFlags.Instance | BindingFlags.NonPublic;
                var ticks = typeof(PPTManager).GetField("_monitorTickCount", fields);
                try
                {
                    dispatcher.Invoke(() =>
                    {
                        var timer = (System.Timers.Timer)typeof(PPTManager).GetField("_unifiedPPTTimer", fields).GetValue(manager);
                        timer.Interval = 60000;
                        // Keep the absent-process cache valid: this check must never connect to real Office.
                        typeof(PPTManager).GetField("_lastPptProcessProbeTimestamp", fields).SetValue(manager, long.MaxValue);
                        manager.StartMonitoring();
                    });
                    Assert(dispatcher.Invoke(() => (int)ticks.GetValue(manager) == 1 && probes.Count == 1),
                        "COM startup must check its connection on the first pass without waiting for the periodic timer.");
                    dispatcher.Invoke(manager.StartMonitoring);
                    Assert(dispatcher.Invoke(() => (int)ticks.GetValue(manager) == 1 && probes.Count == 1),
                        "Repeated COM start must not enqueue another initial probe.");

                    var elapsed = typeof(PPTManager).GetMethod("OnUnifiedPPTTimerElapsed", fields);
                    dispatcher.Invoke(() => elapsed.Invoke(manager, new object[] { null, null }));
                    Assert(dispatcher.Invoke(() => (int)ticks.GetValue(manager) == 2 && probes.Count == 2),
                        "Disconnected COM must retry on the next tick instead of waiting three ticks.");

                    dispatcher.Invoke(() => { manager.StopMonitoring(true); manager.StartMonitoring(); manager.StopMonitoring(true); });
                    Assert(dispatcher.Invoke(() => (int)ticks.GetValue(manager) == 0 && probes.Count == 2),
                        "Stopping COM before its queued initial probe must cancel that probe.");
                    dispatcher.Invoke(() => { manager.StartMonitoring(); manager.StopMonitoring(true); manager.StartMonitoring(); });
                    Assert(dispatcher.Invoke(() => (int)ticks.GetValue(manager) == 1 && probes.Count == 3),
                        "Rapid COM restart must execute only the newest initial probe.");
                    dispatcher.Invoke(() => { manager.StopMonitoring(true); manager.StartMonitoring(); manager.Dispose(); });
                    Assert(dispatcher.Invoke(() => (int)ticks.GetValue(manager) == 0 && probes.Count == 3),
                        "Disposed COM backend must reject its queued initial probe.");
                }
                finally { dispatcher.Invoke(manager.Dispose); }
            }
        }

        private static void CheckComCallbackDispatch()
        {
            using (var dispatcher = new StaDispatcher(null))
            {
                var manager = dispatcher.Invoke(() => new PPTManager(dispatcher, new PresentationLinkOptions()));
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var received = new List<string>();
                var threads = new List<int>();
                Action<string> record = name => { received.Add(name); threads.Add(Thread.CurrentThread.ManagedThreadId); };
                manager.PresentationOpen += _ => record("open");
                manager.SlideShowBegin += _ => record("begin");
                manager.SlideShowNextSlide += _ => record("slide");
                manager.SlideShowEnd += _ => record("end");
                manager.PresentationClose += _ => record("close");
                Action<string> notify = name => typeof(PPTManager).GetMethod(name, flags).Invoke(manager, new object[] { null });
                try
                {
                    dispatcher.Invoke(() =>
                    {
                        ((System.Timers.Timer)typeof(PPTManager).GetField("_unifiedPPTTimer", flags).GetValue(manager)).Interval = 60000;
                        typeof(PPTManager).GetField("_lastPptProcessProbeTimestamp", flags).SetValue(manager, long.MaxValue);
                        manager.StartMonitoring();
                    });
                    Task.Run(() =>
                    {
                        notify("OnPresentationOpen");
                        notify("OnSlideShowBegin");
                        notify("OnSlideShowNextSlide");
                        notify("OnSlideShowEnd");
                    }).GetAwaiter().GetResult();
                    Assert(dispatcher.Invoke(() => string.Join(",", received) == "open,begin,slide,end"
                        && threads.TrueForAll(thread => thread == Thread.CurrentThread.ManagedThreadId)),
                        "COM callbacks from a worker must execute in order on the source STA.");

                    dispatcher.Invoke(() =>
                    {
                        Task.Run(() => notify("OnPresentationClose")).GetAwaiter().GetResult();
                        manager.StopMonitoring(true);
                        manager.StartMonitoring();
                        received.Clear();
                    });
                    Assert(dispatcher.Invoke(() => received.Count == 0), "Queued old COM close must not disconnect a restarted source.");

                    Task.Run(() => notify("OnPresentationClose")).GetAwaiter().GetResult();
                    Assert(dispatcher.Invoke(() => received.Count == 1 && received[0] == "close"
                        && threads[threads.Count - 1] == Thread.CurrentThread.ManagedThreadId),
                        "Current COM close must still be delivered on the source STA.");
                    dispatcher.Invoke(manager.Dispose);
                    Task.Run(() => notify("OnSlideShowBegin")).GetAwaiter().GetResult();
                    Assert(dispatcher.Invoke(() => received.Count == 1 && !manager.IsInSlideShow),
                        "Disposed COM backend must reject late callbacks and preserve stopped state.");
                }
                finally { dispatcher.Invoke(manager.Dispose); }
            }
        }

        private static void CheckAgentSnapshots()
        {
            using (var agent = new PPTAgentLinkManager(new PresentationLinkOptions()))
            {
                var handle = typeof(PPTAgentLinkManager).GetMethod("HandleState", BindingFlags.NonPublic | BindingFlags.Instance);
                PresentationLinkState snapshot = null;
                int changes = 0;
                agent.SlideShowNextSlide += state => { snapshot = state; changes++; };
                var protocol = new PPTState
                {
                    IsRunning = true, SlideIndex = 1, TotalSlides = 3,
                    PresentationName = "lesson.pptx", PresentationFullName = @"C:\Lessons\lesson.pptx"
                };
                handle.Invoke(agent, new object[] { protocol, null });
                protocol = new PPTState
                {
                    IsRunning = true, SlideIndex = 2, TotalSlides = 3,
                    PresentationName = "lesson.pptx", PresentationFullName = @"C:\Lessons\lesson.pptx"
                };
                handle.Invoke(agent, new object[] { protocol, null });
                var metadata = agent.GetPresentationMetadata();
                Assert(changes == 1 && snapshot.CurrentSlide == 2 && snapshot.TotalSlides == 3, "Agent page change must publish typed state.");
                handle.Invoke(agent, new object[] { protocol, null });
                Assert(changes == 1, "Repeated state poll must not publish another page change.");
                handle.Invoke(agent, new object[] { new PPTState { SlideIndex = -1, TotalSlides = -1 }, null });
                Assert(changes == 1 && agent.GetPresentationMetadata().TotalSlides == 3, "Invalid protocol state must not replace valid metadata.");
                handle.Invoke(agent, new object[] { new PPTState { SlideIndex = 4, TotalSlides = 3 }, null });
                Assert(agent.GetCurrentSlideNumber() == 2, "Out-of-range protocol page must be rejected.");
                Assert(metadata.Name == "lesson.pptx" && metadata.FullName == protocol.PresentationFullName, "Agent metadata must preserve legacy identity.");
                protocol.SlideIndex = 3;
                protocol.PresentationName = "changed.pptx";
                Assert(snapshot.CurrentSlide == 2 && metadata.Name == "lesson.pptx", "Published snapshots must not track mutable protocol objects.");
                Assert(agent.CurrentState.SlideIndex == 2 && agent.CurrentState.PresentationName == "lesson.pptx", "Backend must copy incoming protocol state.");
                int updates = 0;
                agent.StateUpdated += () => updates++;
                handle.Invoke(agent, new object[] { new PPTState { IsRunning = true, SlideIndex = 2, TotalSlides = 3,
                    PresentationName = "lesson.pptx", HasHiddenSlides = true }, null });
                Assert(updates == 1 && agent.CurrentState.HasHiddenSlides, "Capability-only Agent state must notify the public source wrapper.");
                agent.StopMonitoring();
                Assert(agent.GetPresentationMetadata() == null, "Stop must clear state even without an active pipe.");
                agent.Dispose();
                handle.Invoke(agent, new object[] { protocol, null });
                Assert(agent.GetPresentationMetadata() == null, "Disposed backend must ignore late state.");
            }
        }

        private static void CheckSessions()
        {
            var session = new PresentationLinkSession();
            var events = new List<string>();
            session.PresentationOpen += _ => events.Add("open");
            session.PresentationClose += _ => events.Add("close");
            session.SlideShowBegin += _ => events.Add("begin");
            session.SlideShowEnd += _ => events.Add("end");
            session.SlideShowNextSlide += _ => events.Add("slide");
            int changes = 0;
            session.StateChanged += _ => changes++;
            var first = new PresentationMetadata("lesson.pptx", @"C:\Lessons\lesson.pptx", 3);
            session.Update(true, true, 1, first);
            var old = session.State;
            Assert(old.PresentationSessionId != Guid.Empty && old.SlideShowSessionId != Guid.Empty, "Running session needs both identities.");
            session.Update(true, true, 1, first);
            Assert(changes == 1 && string.Join(",", events) == "open,begin", "Repeated poll/events must be deduplicated.");
            session.Update(true, true, 2, first);
            Assert(session.State.SlideShowSessionId == old.SlideShowSessionId && events[2] == "slide", "Page changes must preserve show identity.");
            session.Update(true, false, 2, first);
            session.Update(true, true, 2, first);
            Assert(session.State.PresentationSessionId == old.PresentationSessionId && session.State.SlideShowSessionId != old.SlideShowSessionId,
                "Show reentry must change only show identity.");
            events.Clear();
            session.Update(true, true, 1, new PresentationMetadata("other.pptx", null, 3));
            Assert(string.Join(",", events) == "end,close,open,begin", "Document replacement must close old show before opening new one.");
            Assert(session.State.PresentationSessionId != old.PresentationSessionId, "Replacement needs a fresh document identity.");
            int beforeDetails = changes;
            session.Update(true, true, 1, new PresentationMetadata("other.pptx", null, 3, true, true));
            Assert(changes == beforeDetails + 1 && session.State.Presentation.HasHiddenSlides, "Capability detail changes must refresh state.");
            session.Update(false, true, 99, first);
            Assert(!session.State.IsInSlideShow && session.State.Presentation == null && session.State.CurrentSlide == 0,
                "Disconnect must normalize contradictory state.");
            session.Update(true, true, 1, first);
            Assert(session.State.PresentationSessionId != old.PresentationSessionId, "Reopen after disconnect must not reuse old identity.");
        }

        private static void CheckStaDispatcher()
        {
            var dispatcher = new StaDispatcher(null);
            int thread = dispatcher.Invoke(() => Thread.CurrentThread.ManagedThreadId);
            Assert(dispatcher.Invoke(() => Thread.CurrentThread.GetApartmentState()) == ApartmentState.STA, "Core dispatcher must own STA thread.");
            Assert(dispatcher.Invoke(() => dispatcher.Invoke(() => Thread.CurrentThread.ManagedThreadId)) == thread, "Nested dispatch must not deadlock.");
            var order = new List<int>();
            dispatcher.Post(() => order.Add(1));
            dispatcher.Invoke(() => order.Add(2));
            Assert(string.Join(",", order) == "1,2", "Posted and synchronous work must be serialized.");
            dispatcher.Dispose();
            dispatcher.Dispose();
            Throws<ObjectDisposedException>(() => dispatcher.Invoke(() => true));
        }

        private static void RunDispatcherCheck(Func<Task> check)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            Exception failure = null;
            bool completed = false;
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timeout.Tick += (_, __) => { failure = new TimeoutException("ViewModel check did not finish."); frame.Continue = false; };
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await check(); completed = true; }
                catch (Exception ex) { failure = ex; }
                finally { frame.Continue = false; }
            }));
            timeout.Start();
            Dispatcher.PushFrame(frame);
            timeout.Stop();
            if (failure != null) throw failure;
            Assert(completed, "Dispatcher check must execute, not merely build.");
        }

        private static async Task CheckViewModel()
        {
            var errors = new List<Exception>();
            using (var source = new FakeSource())
            using (var replacement = new FakeSource())
            using (var vm = new PresentationLinkViewModel(Dispatcher.CurrentDispatcher, errors.Add,
                () => source.TryNavigatePrevious(), () => source.TryNavigateNext(), page => source.TryNavigateToSlide(page), _ => { }))
            {
                vm.Attach(source);
                Assert(!vm.NextCommand.CanExecute(null), "Disconnected commands must be disabled.");
                source.StartMonitoring();
                source.TryStartSlideShow();
                await vm.WhenIdle;
                Assert(vm.NextCommand.CanExecute(null) && !vm.NavigateCommand.CanExecute(4) && vm.CurrentSlide == 1,
                    "Shared bindings and commands must follow source state.");
                Assert(vm.Capabilities == source.Capabilities, "ViewModel must expose source capabilities, not source type.");

                var entered = new TaskCompletionSource<bool>();
                var release = new TaskCompletionSource<bool>();
                var pages = new List<int>();
                Func<PresentationLinkState, Task> ordered = async state =>
                {
                    pages.Add(state.CurrentSlide);
                    if (state.CurrentSlide == 2) { entered.SetResult(true); await release.Task; }
                };
                vm.SlideChanged += ordered;
                source.TryNavigateToSlide(2);
                await entered.Task;
                source.TryNavigateToSlide(3);
                source.TryNavigateToSlide(3);
                source.TryNavigateToSlide(1);
                await Task.Yield();
                Assert(string.Join(",", pages) == "2", "Next host effect must wait for previous async effect.");
                release.SetResult(true);
                await vm.WhenIdle;
                Assert(string.Join(",", pages) == "2,3,1" && vm.CurrentSlide == 1, "Rapid navigation must preserve order and deduplicate pages.");
                vm.SlideChanged -= ordered;

                entered = new TaskCompletionSource<bool>();
                release = new TaskCompletionSource<bool>();
                bool stale = false;
                int secondaryCalls = 0;
                Func<PresentationLinkState, Task> blocked = async state =>
                {
                    if (state.CurrentSlide != 2) return;
                    entered.SetResult(true);
                    await release.Task;
                    stale = !vm.IsCurrentOperation;
                };
                Func<PresentationLinkState, Task> secondary = _ => { secondaryCalls++; return Task.CompletedTask; };
                vm.SlideChanged += blocked;
                vm.SlideChanged += secondary;
                source.TryNavigateToSlide(2);
                await entered.Task;
                source.TryNavigateToSlide(3);
                var lateNotification = source.CaptureStateNotification();
                int generation = vm.Generation;
                vm.Attach(replacement);
                replacement.StartMonitoring();
                replacement.TryStartSlideShow();
                release.SetResult(true);
                await vm.WhenIdle;
                Assert(stale && secondaryCalls == 0 && vm.Generation != generation && vm.CurrentSlide == 1,
                    "Replacement must invalidate running continuation, later subscribers and queued old work.");
                lateNotification();
                await vm.WhenIdle;
                Assert(vm.CurrentSlide == 1, "Already captured old-source callback must retain old subscription generation.");
                source.TryNavigateToSlide(1);
                await vm.WhenIdle;
                Assert(secondaryCalls == 0, "Replaced source must be unsubscribed.");
                vm.SlideChanged -= blocked;
                vm.SlideChanged -= secondary;
                Func<PresentationLinkState, Task> failing = _ => throw new InvalidOperationException("Expected host failure.");
                vm.SlideChanged += failing;
                replacement.TryNavigateToSlide(2);
                await vm.WhenIdle;
                vm.SlideChanged -= failing;
                replacement.TryNavigateToSlide(3);
                await vm.WhenIdle;
                Assert(errors.Count == 1 && vm.CurrentSlide == 3, "Host exception must not strand the serial queue.");
            }
        }

        private static async Task CheckFakeInkHost()
        {
            string root = Path.Combine(Path.GetTempPath(), "opencode", "linkage-self-tests", Guid.NewGuid().ToString("N"));
            try
            {
                using (var ink = new PPTInkManager { AutoSaveLocation = root })
                using (var source = new FakeSource())
                using (var vm = new PresentationLinkViewModel(Dispatcher.CurrentDispatcher, ex => throw ex,
                    () => { }, () => { }, _ => { }, _ => { }))
                {
                    StrokeCollection canvas = new StrokeCollection();
                    int previous = 0;
                    vm.PresentationOpened += state => { ink.InitializePresentation(state.Presentation); return Task.CompletedTask; };
                    vm.SlideShowStarted += state => { previous = state.CurrentSlide; canvas = ink.LoadSlideStrokes(previous); return Task.CompletedTask; };
                    vm.SlideChanged += async state =>
                    {
                        ink.ForceSaveSlideStrokes(previous, canvas);
                        await Task.Yield();
                        previous = state.CurrentSlide;
                        canvas = ink.LoadSlideStrokes(previous);
                    };
                    vm.SlideShowEnded += state =>
                    {
                        ink.ForceSaveSlideStrokes(previous, canvas);
                        ink.SaveAllStrokesToFile(previous);
                        canvas = new StrokeCollection();
                        return Task.CompletedTask;
                    };
                    vm.Attach(source);
                    source.StartMonitoring();
                    source.TryStartSlideShow();
                    await vm.WhenIdle;
                    canvas.Add(new Stroke(new StylusPointCollection { new StylusPoint(10, 10), new StylusPoint(20, 20) }));
                    source.TryNavigateToSlide(2);
                    await vm.WhenIdle;
                    Assert(canvas.Count == 0, "Fake slide 2 must not inherit slide 1 ink.");
                    canvas.Add(new Stroke(new StylusPointCollection { new StylusPoint(30, 30), new StylusPoint(40, 40) }));
                    source.TryNavigateToSlide(3);
                    source.TryNavigateToSlide(1);
                    await vm.WhenIdle;
                    Assert(canvas.Count == 1 && Math.Abs(canvas[0].StylusPoints[0].X - 10) < 0.1,
                        $"Rapid Fake navigation must restore target ink (count={canvas.Count}, x={(canvas.Count > 0 ? canvas[0].StylusPoints[0].X : -1)}).");
                    source.ReplacePresentation(new PresentationMetadata("other.pptx", null, 3));
                    await vm.WhenIdle;
                    Assert(canvas.Count == 0 && previous == 1, "Document replacement must save old ink and initialize empty new canvas.");
                    Assert(File.ReadAllText(Path.Combine(root, "Auto Saved - Presentations", "lesson.pptx_3", "Position")) == "1",
                        "Fake lifecycle must save old document Position before opening replacement.");
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void CheckInkRoundTrip(string fullName)
        {
            string root = Path.Combine(Path.GetTempPath(), "opencode", "linkage-self-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var metadata = new PresentationMetadata("lesson.pptx", fullName, 3);
                string id = "lesson.pptx_3";
                if (fullName != null)
                {
                    using (var md5 = MD5.Create())
                        id += "_" + BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(fullName))).Replace("-", "").Substring(0, 8);
                }
                string folder = Path.Combine(root, "Auto Saved - Presentations", id);
                var strokes = new StrokeCollection
                {
                    new Stroke(new StylusPointCollection { new StylusPoint(10, 10), new StylusPoint(20, 20) })
                };
                using (var ink = new PPTInkManager { AutoSaveLocation = root })
                {
                    ink.InitializePresentation(metadata);
                    ink.ForceSaveSlideStrokes(2, strokes);
                    ink.InitializePresentation(null);
                    Assert(ink.LoadSlideStrokes(2).Count == 1, "Unavailable metadata must not discard current ink.");
                    ink.SaveAllStrokesToFile(2);
                }
                Assert(File.Exists(Path.Combine(folder, "0002.icstk")), "Legacy folder and stroke filename must remain unchanged.");
                Assert(File.ReadAllText(Path.Combine(folder, "Position")) == "2", "Legacy Position format must remain unchanged.");
                using (var ink = new PPTInkManager { AutoSaveLocation = root })
                {
                    ink.InitializePresentation(metadata);
                    Assert(ink.LoadSlideStrokes(2).Count == 1, "Saved ink must restore using metadata only.");
                    var edited = strokes.Clone();
                    edited.Add(strokes[0].Clone());
                    ink.ForceSaveSlideStrokes(2, edited);
                    Assert(ink.LoadSlideStrokes(2).Count == 2, "Syncing the displayed page must replace stale ink before manual saving.");
                    ink.SaveAllStrokesToFile(2);
                    using (var file = File.OpenRead(Path.Combine(folder, "0002.icstk")))
                        Assert(new StrokeCollection(file).Count == 2, "Manual/save lifecycle must persist the latest displayed ink.");
                    ink.ForceSaveSlideStrokes(2, new StrokeCollection());
                    Assert(ink.LoadSlideStrokes(2).Count == 0, "An empty displayed page must overwrite cached ink.");
                    ink.SaveAllStrokesToFile(1);
                    Assert(!File.Exists(Path.Combine(folder, "0002.icstk")), "Clearing a page must delete its legacy file, not save empty ISF.");
                    ink.IsAutoSaveEnabled = false;
                    ink.ForceSaveSlideStrokes(2, strokes);
                    ink.SaveAllStrokesToFile(3);
                    Assert(!File.Exists(Path.Combine(folder, "0002.icstk")) && File.ReadAllText(Path.Combine(folder, "Position")) == "1",
                        "Disabled autosave must not rewrite ink or Position.");
                    ink.SavePlaybackPosition(3);
                    Assert(File.ReadAllText(Path.Combine(folder, "Position")) == "3" && !File.Exists(Path.Combine(folder, "0002.icstk")),
                        "Playback position must remain independently saveable when ink autosave is disabled.");
                    ink.SavePlaybackPosition(4);
                    Assert(File.ReadAllText(Path.Combine(folder, "Position")) == "3", "Invalid playback position must not replace a valid one.");
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void CheckPPTStrokeArchive()
        {
            string root = Path.Combine(Path.GetTempPath(), "opencode", "linkage-self-tests", Guid.NewGuid().ToString("N"));
            string input = Path.Combine(root, "input");
            string extracted = Path.Combine(root, "extracted");
            Directory.CreateDirectory(input);
            try
            {
                var original = new StrokeCollection
                {
                    new Stroke(new StylusPointCollection { new StylusPoint(10, 10), new StylusPoint(20, 20) })
                };
                using (var stream = File.Create(Path.Combine(input, "page_0001.icstk"))) original.Save(stream);
                File.WriteAllText(Path.Combine(input, "page_0002.xml"),
                    "<InkCanvasStrokes><Stroke DrawingAttributes=\"Width=3;Height=3\"><StylusPoints>"
                    + "<StylusPoint X=\"30\" Y=\"30\" PressureFactor=\"0.5\"/>"
                    + "</StylusPoints></Stroke></InkCanvasStrokes>");
                string zip = Path.Combine(root, "legacy.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(input, zip);
                SafeZipExtractor.ExtractZipSafely(zip, extracted, overwrite: true);
                var pages = ReadArchivePages(extracted, 3);
                Assert(pages.Count == 2 && pages[1].Count == 1 && pages[2].Count == 1
                    && Math.Abs(pages[2][0].StylusPoints[0].X - 30) < 0.1, "Legacy ZIP binary/XML pages must load without a MainWindow instance.");
                using (var ink = new PPTInkManager { AutoSaveLocation = root })
                {
                    ink.InitializePresentation(new PresentationMetadata("lesson.pptx", null, 3));
                    ink.ForceSaveSlideStrokes(3, original);
                    ink.SaveAllStrokesToFile(3);
                    ink.ClearAllStrokes();
                    for (int page = 1; page <= 3; page++)
                        ink.ForceSaveSlideStrokes(page, pages.TryGetValue(page, out var strokes) ? strokes : new StrokeCollection());
                    ink.SaveAllStrokesToFile(2);
                    Assert(ink.LoadSlideStrokes(1).Count == 1 && ink.LoadSlideStrokes(2).Count == 1
                        && ink.LoadSlideStrokes(3).Count == 0, "Restored archive must replace the same cache used by navigation.");
                    Assert(!File.Exists(Path.Combine(root, "Auto Saved - Presentations", "lesson.pptx_3", "0003.icstk")),
                        "Absent ZIP pages must not resurrect pre-import ink on reopen.");
                    File.WriteAllText(Path.Combine(extracted, "page_0002.xml"), "<wrong/>");
                    Throws<InvalidDataException>(() => ReadArchivePages(extracted, 3));
                    Assert(ink.LoadSlideStrokes(1).Count == 1, "Failed archive read must leave existing ink intact.");
                    File.Delete(Path.Combine(extracted, "page_0002.xml"));
                    File.WriteAllText(Path.Combine(extracted, "page_0000.icstk"), "invalid");
                    Throws<InvalidDataException>(() => ReadArchivePages(extracted, 3));
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static Dictionary<int, StrokeCollection> ReadArchivePages(string directory, int totalSlides)
        {
            var read = typeof(Ink_Canvas.MainWindow).GetMethod("ReadPPTStrokePages", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(read != null, "Archive loader must remain available for host regression checks.");
            try { return (Dictionary<int, StrokeCollection>)read.Invoke(null, new object[] { directory, totalSlides }); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }
    }

    public sealed class PresentationStub
    {
        public string Name => "lesson.pptx";
        public string FullName => @"C:\Lessons\lesson.pptx";
        public SlidesStub Slides => new SlidesStub();
    }
    public sealed class SlidesStub { public int Count => 3; }
    public sealed class WindowStub { public ViewStub View => new ViewStub(); }
    public sealed class ViewStub { public int CurrentShowPosition => 3; }
    public sealed class RotWindowStub { public int Active => -1; }

    public sealed class TransitionPresentationStub
    {
        public TransitionPresentationStub(params TransitionStub[] transitions) { Slides = new TransitionSlidesStub(transitions); }
        public TransitionSlidesStub Slides { get; }
    }
    public sealed class TransitionSlidesStub
    {
        private readonly TransitionStub[] _transitions;
        public TransitionSlidesStub(TransitionStub[] transitions) { _transitions = transitions; }
        public int Count => _transitions.Length;
        public TransitionSlideStub this[int index] => new TransitionSlideStub { SlideShowTransition = _transitions[index - 1] };
    }
    public sealed class TransitionSlideStub { public TransitionStub SlideShowTransition { get; set; } }
    public sealed class TransitionStub
    {
        public object Hidden { get; set; }
        public object AdvanceOnTime { get; set; }
        public double AdvanceTime { get; set; }
    }
    public sealed class ShapeTypeStub
    {
        public object Type { get; set; }
        public int MediaType { get; set; }
    }

    internal sealed class FakeSource : IPresentationLinkSource
    {
        private readonly PresentationLinkSession _session = new PresentationLinkSession();
        private PresentationMetadata _metadata = new PresentationMetadata("lesson.pptx", null, 3);
        public FakeSource() { _session.StateChanged += state => StateChanged?.Invoke(state); }
        public event Action<PresentationLinkState> StateChanged;
        public Action CaptureStateNotification()
        {
            var notify = StateChanged;
            var state = State;
            return () => notify?.Invoke(state);
        }
        public void ReplacePresentation(PresentationMetadata metadata)
        {
            _metadata = metadata;
            _session.Update(true, IsInSlideShow, 1, metadata);
        }
        public event Action<PresentationLinkState> SlideShowNextSlide { add => _session.SlideShowNextSlide += value; remove => _session.SlideShowNextSlide -= value; }
        public event Action<PresentationLinkState> PresentationOpen { add => _session.PresentationOpen += value; remove => _session.PresentationOpen -= value; }
        public event Action<PresentationLinkState> PresentationClose { add => _session.PresentationClose += value; remove => _session.PresentationClose -= value; }
        public event Action<PresentationLinkState> SlideShowBegin { add => _session.SlideShowBegin += value; remove => _session.SlideShowBegin -= value; }
        public event Action<PresentationLinkState> SlideShowEnd { add => _session.SlideShowEnd += value; remove => _session.SlideShowEnd -= value; }
        public event Action<bool> PPTConnectionChanged { add => _session.PPTConnectionChanged += value; remove => _session.PPTConnectionChanged -= value; }
        public event Action<bool> SlideShowStateChanged { add => _session.SlideShowStateChanged += value; remove => _session.SlideShowStateChanged -= value; }
        public PresentationLinkState State => _session.State;
        public PresentationCapabilities Capabilities => PresentationCapabilities.SlideNavigation;
        public bool IsSupportWPS { get; set; }
        public bool SkipAnimationsWhenNavigating { get; set; }
        public bool IsConnected => State.IsConnected;
        public bool IsInSlideShow => State.IsInSlideShow;
        public int SlidesCount => State.TotalSlides;
        public PresentationMetadata GetPresentationMetadata() => State.Presentation;
        public int GetCurrentSlideNumber() => State.CurrentSlide;
        public string GetPresentationName() => State.Presentation?.Name ?? "";
        public void StartMonitoring() => _session.Update(true, false, 1, _metadata);
        public void StopMonitoring(bool isShutdown = false) => _session.Update(false, false, 0, null);
        public void ReloadConnection() { StopMonitoring(); StartMonitoring(); }
        public bool TryStartSlideShow() { if (!IsConnected) return false; _session.Update(true, true, State.CurrentSlide, _metadata); return true; }
        public bool TryEndSlideShow() { if (!IsInSlideShow) return false; _session.Update(true, false, State.CurrentSlide, _metadata); return true; }
        public bool TryNavigateToSlide(int slideNumber)
        {
            if (!IsConnected || slideNumber <= 0 || slideNumber > SlidesCount) return false;
            _session.Update(true, IsInSlideShow, slideNumber, _metadata);
            return true;
        }
        public bool TryNavigateNext() => TryNavigateToSlide(State.CurrentSlide + 1);
        public bool TryNavigatePrevious() => TryNavigateToSlide(State.CurrentSlide - 1);
        public bool TryShowSlideNavigation() => IsConnected;
        public IReadOnlyList<PresentationThumbnail> ExportSlideThumbnails(int width, int height) => Array.Empty<PresentationThumbnail>();
        public PresentationSmartRegions GetSmartRegions() => new PresentationSmartRegions();
        public bool TryUnhideHiddenSlides() => false;
        public bool TryDisableAutoPlayTimings() => false;
        public bool SetEnhancedMonitoring(bool enabled, bool isShutdown = false) => false;
        public void Dispose() => StopMonitoring();
    }
}
