using InkCanvasLinkage.Core;
using InkCanvasLinkage.Core.Sources;
using InkCanvasPPTAgent.Contracts;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;

namespace LinkageSelfTests
{
    internal static class CoreRegressionChecks
    {
        public static void Run()
        {
            CheckAgentShowEvents();
            CheckOptionalComDetails();
            CheckRotNavigation();
            CheckRotMonitoringRestart();
            CheckEnhancedApplicationOwnership();
        }

        private static void CheckAgentShowEvents()
        {
            var warnings = new List<string>();
            string pipeName = "linkage-regression-" + Guid.NewGuid().ToString("N");
            using (var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
            using (var source = PresentationLinkSourceFactory.Create(PresentationLinkMode.Agent,
                new PresentationLinkOptions { Log = (message, level) => { if (level == PresentationLogLevel.Warning) warnings.Add(message); } }))
            {
                var dispatcher = GetField<StaDispatcher>(source, "_dispatcher");
                var agent = GetField<PPTAgentLinkManager>(source, "_backend");
                var waiting = server.BeginWaitForConnection(null, null);
                pipe.Connect(1000);
                server.EndWaitForConnection(waiting);
                // IsConnected reads the pipe; use a private test pipe without starting the Agent connection loop.
                var client = new PPTAgentPipeClient(new LogHelper(null));
                SetField(client, "_pipe", pipe);
                SetField(client, "_isConnected", true);
                SetField(agent, "_client", client);
                dispatcher.Invoke(() => SetField(source, "_monitoring", true));
                int begins = 0, ends = 0, updates = 0;
                var eventThreads = new List<int>();
                source.SlideShowBegin += _ => { begins++; eventThreads.Add(Thread.CurrentThread.ManagedThreadId); };
                source.SlideShowEnd += _ => { ends++; eventThreads.Add(Thread.CurrentThread.ManagedThreadId); };
                agent.StateUpdated += () => updates++;
                PPTState eventPayload = null;
                agent.SlideShowEnd += value => eventPayload = (PPTState)value;

                Action<PPTState, string> feed = (state, name) =>
                {
                    typeof(PPTAgentLinkManager).GetMethod("HandleState", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(agent, new object[] { state, name });
                    dispatcher.Invoke(() => { });
                };
                feed(AgentState(true), null);
                Assert(begins == 1 && ends == 0 && source.IsInSlideShow,
                    $"Initial Agent state must begin exactly one show: begins={begins}, ends={ends}, running={source.IsInSlideShow}. {string.Join("\n", warnings)}");
                var documentId = source.State.PresentationSessionId;
                var showId = source.State.SlideShowSessionId;

                var staleEnd = AgentState(true);
                feed(staleEnd, PPTEvents.SlideShowEnd);
                Assert(begins == 1 && ends == 1 && !source.IsInSlideShow && !agent.CurrentState.IsRunning && !agent.IsInSlideShow,
                    "Explicit Agent end must override stale running state; StateUpdated must not restart the wrapper.");
                Assert(eventPayload != null && !eventPayload.IsRunning && staleEnd.IsRunning,
                    "End payload must be normalized without mutating incoming protocol state.");
                eventPayload.IsRunning = true;
                Assert(!agent.CurrentState.IsRunning, "Mutable event payload must not expose the Agent cache.");
                Assert(source.State.PresentationSessionId == documentId && source.State.SlideShowSessionId == showId,
                    "Explicit end must preserve document identity and must not create a phantom show identity.");
                feed(staleEnd, PPTEvents.SlideShowEnd);
                Assert(begins == 1 && ends == 1, "Repeated explicit end must remain deduplicated by the public wrapper.");

                var staleBegin = AgentState(false);
                feed(staleBegin, PPTEvents.SlideShowBegin);
                Assert(begins == 2 && ends == 1 && source.IsInSlideShow && agent.CurrentState.IsRunning && agent.IsInSlideShow,
                    "Explicit Agent begin must override a stale stopped snapshot.");
                Assert(!staleBegin.IsRunning && source.State.PresentationSessionId == documentId
                    && source.State.SlideShowSessionId != showId, "Real reentry must change only show identity and preserve input.");

                feed(null, PPTEvents.SlideShowEnd);
                Assert(begins == 2 && ends == 2 && !source.IsInSlideShow && !agent.CurrentState.IsRunning
                    && source.State.CurrentSlide == 2 && source.State.PresentationSessionId == documentId,
                    "End without snapshot must use cached document/page but stop the show.");
                feed(null, PPTEvents.SlideShowBegin);
                Assert(begins == 3 && ends == 2 && source.IsInSlideShow && agent.CurrentState.IsRunning,
                    "Begin without snapshot must start from cached metadata.");
                int previousUpdates = updates;
                feed(null, null);
                feed(null, "unknownEvent");
                Assert(updates == previousUpdates && begins == 3 && ends == 2,
                    "Missing snapshots without explicit show events must remain ignored.");
                int staThread = dispatcher.Invoke(() => Thread.CurrentThread.ManagedThreadId);
                Assert(eventThreads.TrueForAll(thread => thread == staThread), "Agent wrapper events must remain on its STA.");
                Assert(GetField<object>(client, "_connectLoopTask") == null && GetField<object>(client, "_cts") == null,
                    "Agent regression must never start the real connection loop or monitoring.");
            }
        }

        private static PPTState AgentState(bool running) => new PPTState
        {
            IsRunning = running, SlideIndex = 2, TotalSlides = 3,
            PresentationName = "regression.pptx", PresentationFullName = @"C:\Lessons\regression.pptx"
        };

        private static void CheckOptionalComDetails()
        {
            for (int failure = 0; failure < 5; failure++)
            {
                var transition = new CoreTransitionStub
                {
                    FailHidden = failure == 0 || failure == 3,
                    FailTiming = failure == 1 || failure == 3,
                    FailAdvanceTime = failure == 2
                };
                var presentation = new CoreDetailsPresentationStub(transition) { FailSlides = failure == 4 };
                var metadata = new PresentationMetadata("details.pptx", @"C:\Lessons\details.pptx", 3);
                int errors = 0;
                var details = ComPresentationCapabilities.ReadDetails(presentation, metadata, _ => errors++);
                bool hidden = !transition.FailHidden && !presentation.FailSlides;
                bool timing = !transition.FailTiming && !transition.FailAdvanceTime && !presentation.FailSlides;
                Assert(details.Name == metadata.Name && details.FullName == metadata.FullName && details.TotalSlides == 3
                    && details.HasHiddenSlides == hidden && details.HasAutoPlayTimings == timing,
                    "Optional COM getter failure must preserve base metadata and the independent successful scan.");
                Assert(errors == (failure >= 3 ? 2 : 1), "Each failed optional scan must report its error.");
                Assert(presentation.FailSlides || (transition.HiddenReads > 0 && transition.TimingReads > 0),
                    "Both optional scans must execute even when the other getter throws.");
                CheckComCapture(presentation, metadata, hidden, timing, errors, false);
            }
            CheckComCapture(null, new PresentationMetadata("acquisition.pptx", null, 3), false, false, 1, true);
        }

        private static void CheckComCapture(object presentation, PresentationMetadata metadata,
            bool hidden, bool timing, int expectedWarnings, bool failAcquisition)
        {
            var warnings = new List<string>();
            using (var source = PresentationLinkSourceFactory.Create(PresentationLinkMode.Agent,
                new PresentationLinkOptions { Log = (message, level) => { if (level == PresentationLogLevel.Warning) warnings.Add(message); } }))
            {
                var dispatcher = GetField<StaDispatcher>(source, "_dispatcher");
                var original = GetField<IPPTLinkManager>(source, "_backend");
                var backend = new CaptureBackend { Metadata = metadata, Presentation = presentation, FailAcquisition = failAcquisition };
                dispatcher.Invoke(() => { SetField(source, "_backend", backend); SetField(source, "_monitoring", true); });
                try
                {
                    Capture(source, dispatcher, true, 2, true);
                    Assert(source.IsConnected && source.IsInSlideShow && source.State.CurrentSlide == 2 && source.SlidesCount == 3
                        && source.State.Presentation.Name == metadata.Name && source.State.Presentation.FullName == metadata.FullName,
                        "Real Capture must publish base session state even if optional COM details fail.");
                    Assert(source.State.Presentation.HasHiddenSlides == hidden && source.State.Presentation.HasAutoPlayTimings == timing,
                        "Capture must preserve the successful optional scan when the other fails.");
                    Assert(warnings.Count == expectedWarnings && warnings.TrueForAll(message => message.Contains("读取联动状态失败")),
                        "Optional Capture errors must retain the existing warning logging.");
                    int ends = 0;
                    source.SlideShowEnd += _ => ends++;
                    Capture(source, dispatcher, false, 3, true);
                    Assert(ends == 1 && !source.IsInSlideShow && source.State.CurrentSlide == 3,
                        "Optional details failure during refresh must not swallow a real show end/page update.");
                    var previous = source.State;
                    int previousWarnings = warnings.Count;
                    backend.FailMetadata = true;
                    Capture(source, dispatcher, true, 1, true);
                    Assert(ReferenceEquals(source.State, previous) && warnings.Count == previousWarnings + 1,
                        "Required metadata failure must retain the original outer error handling, not publish fabricated state.");
                    Assert(GetField<object>(original, "_client") == null, "COM Capture regression must not start real monitoring.");
                }
                finally { dispatcher.Invoke(() => SetField(source, "_backend", original)); }
            }
        }

        private static void Capture(IPresentationLinkSource source, StaDispatcher dispatcher, bool running, int page, bool refresh)
        {
            dispatcher.Invoke(() => typeof(PresentationLinkSource).GetMethod("Capture", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(source, new object[] { null, (bool?)running, false, (int?)page, refresh }));
        }

        private static void CheckRotNavigation()
        {
            foreach (bool next in new[] { false, true })
            {
                CheckRotNavigation(next, true, 1, 3, next ? "goto:2" : "previous");
                CheckRotNavigation(next, true, 2, 3, next ? "goto:3" : "goto:1");
                CheckRotNavigation(next, true, 3, 3, next ? "next" : "goto:2");
                CheckRotNavigation(next, true, 1, 1, next ? "next" : "previous");
                CheckRotNavigation(next, false, 2, 3, next ? "next" : "previous");
                CheckRotNavigation(next, true, 0, 3, next ? "next" : "previous");
                CheckRotNavigation(next, true, 4, 3, next ? "next" : "previous");
                CheckRotNavigation(next, true, 2, 0, next ? "next" : "previous");
            }
            CheckRotNavigation(true, true, int.MaxValue, int.MaxValue, "next");
            CheckRotNavigation(true, true, int.MaxValue - 1, int.MaxValue, "goto:" + int.MaxValue);
        }

        private static void CheckRotNavigation(bool next, bool skip, int current, int total, string expected)
        {
            var view = new CoreNavigationViewStub { TotalSlides = total };
            ROTPPTManager.NavigateSlideShowView(view, next, skip, current, total);
            Assert(view.Calls.Count == 1 && view.Calls[0] == expected,
                $"Real ROT view navigation must issue {expected}: next={next}, skip={skip}, page={current}/{total}.");
        }

        private static void CheckRotMonitoringRestart()
        {
            using (var dispatcher = new StaDispatcher(null))
            using (var backend = new ROTPPTManager(dispatcher, new PresentationLinkOptions()))
            {
                dispatcher.Invoke(() =>
                {
                    // Model stopping after the delayed recovery has already returned without clearing unloading.
                    SetField(backend, "_shouldStop", true);
                    SetField(backend, "_isModuleUnloading", true);
                    int version = GetField<int>(backend, "_recoveryVersion");
                    backend.StartMonitoring();
                    var timer = GetField<System.Timers.Timer>(backend, "_monitoringTimer");
                    timer.Interval = 60000;
                    Assert(!GetField<bool>(backend, "_shouldStop") && !GetField<bool>(backend, "_isModuleUnloading")
                        && GetField<int>(backend, "_recoveryVersion") > version && timer.Enabled,
                        "ROT restart must resume polling and invalidate recovery tasks from the stopped generation.");
                    SetField(backend, "_isModuleUnloading", true);
                    version = GetField<int>(backend, "_recoveryVersion");
                    backend.StartMonitoring();
                    Assert(ReferenceEquals(timer, GetField<object>(backend, "_monitoringTimer"))
                        && GetField<bool>(backend, "_isModuleUnloading") && GetField<int>(backend, "_recoveryVersion") == version,
                        "Duplicate start must not replace the timer or clear a running connection's recovery gate.");
                    backend.StopMonitoring();
                    backend.StartMonitoring();
                    GetField<System.Timers.Timer>(backend, "_monitoringTimer").Interval = 60000;
                    Assert(!GetField<bool>(backend, "_isModuleUnloading"), "A second stop/restart must also clear unloading.");
                    backend.StopMonitoring();
                });
            }
        }

        private static void CheckEnhancedApplicationOwnership()
        {
            using (var dispatcher = new StaDispatcher(null))
            using (var backend = new ComPPTLinkManager(dispatcher, new PresentationLinkOptions()))
            {
                dispatcher.Invoke(() =>
                {
                    var inner = GetField<PPTManager>(backend, "_inner");
                    var property = typeof(PPTManager).GetProperty("PPTApplication");
                    foreach (int count in new[] { 1, 0, -1 })
                    {
                        int quits = 0;
                        var warnings = new List<string>();
                        var presentations = new OfficeProxy(typeof(Microsoft.Office.Interop.PowerPoint.Presentations), method =>
                            method == "get_Count" ? (object)count : throw new InvalidOperationException(method)).GetTransparentProxy();
                        var application = (Microsoft.Office.Interop.PowerPoint.Application)new OfficeProxy(
                            typeof(Microsoft.Office.Interop.PowerPoint.Application), method =>
                            {
                                if (method == "get_Name") return "PowerPoint";
                                if (method == "get_Presentations")
                                {
                                    if (count < 0) throw new COMException("Presentations unavailable.");
                                    return presentations;
                                }
                                if (method == "Quit") { quits++; return null; }
                                throw new InvalidOperationException(method);
                            }).GetTransparentProxy();
                        property.SetValue(inner, application);
                        SetField(inner, "_cachedIsConnected", true);
                        var enhanced = new EnhancedPowerPointSession(dispatcher, backend, new LogHelper((message, _) => warnings.Add(message)));
                        SetField(enhanced, "_ownedApplication", application);
                        try
                        {
                            enhanced.SetEnabled(false);
                            enhanced.Dispose();
                            Assert(ReferenceEquals(backend.PPTApplication, application) && backend.IsConnected
                                && application.Name == "PowerPoint" && GetField<object>(enhanced, "_ownedApplication") == null,
                                "Disabling enhanced monitoring must leave the backend's application reference and connection intact.");
                            Assert(quits == (count == 0 ? 1 : 0) && warnings.Count == (count < 0 ? 1 : 0),
                                $"Enhanced cleanup policy failed: documents={count}, quits={quits}, warnings={warnings.Count}. {string.Join("\n", warnings)}");
                        }
                        finally { property.SetValue(inner, null); SetField(inner, "_cachedIsConnected", false); }
                    }
                });
            }
        }

        // Managed Office interface proxies exercise cleanup policy without creating or connecting real Office.
        private sealed class OfficeProxy : RealProxy
        {
            private readonly Func<string, object> _invoke;
            private readonly Type _type;
            public OfficeProxy(Type type, Func<string, object> invoke) : base(type) { _type = type; _invoke = invoke; }
            public override IMessage Invoke(IMessage message)
            {
                var call = (IMethodCallMessage)message;
                try { return new ReturnMessage(call.MethodName == "GetType" ? _type : _invoke(call.MethodName), null, 0, call.LogicalCallContext, call); }
                catch (Exception ex) { return new ReturnMessage(ex, call); }
            }
        }

        private static T GetField<T>(object target, string name) => (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class CaptureBackend : IPPTLinkManager
        {
            public PresentationMetadata Metadata;
            public object Presentation;
            public bool FailAcquisition, FailMetadata;
            public bool IsConnected => true;
            public bool IsInSlideShow => true;
            public int SlidesCount => Metadata.TotalSlides;
            public bool IsSupportWPS { get; set; }
            public bool SkipAnimationsWhenNavigating { get; set; }
            public object PPTApplication => null;
            public PresentationMetadata GetPresentationMetadata() => FailMetadata
                ? throw new COMException("Required metadata unavailable.") : Metadata;
            public object GetCurrentActivePresentation() => FailAcquisition
                ? throw new COMException("Optional presentation unavailable.") : Presentation;
            public int GetCurrentSlideNumber() => 2;
            public string GetPresentationName() => Metadata.Name;
            public event Action<PresentationLinkState> SlideShowNextSlide { add { } remove { } }
            public event Action<object> SlideShowBegin { add { } remove { } }
            public event Action<object> SlideShowEnd { add { } remove { } }
            public event Action<object> PresentationOpen { add { } remove { } }
            public event Action<object> PresentationClose { add { } remove { } }
            public event Action<bool> PPTConnectionChanged { add { } remove { } }
            public event Action<bool> SlideShowStateChanged { add { } remove { } }
            public void StartMonitoring() => throw new InvalidOperationException("Regression tests must not start monitoring.");
            public void StopMonitoring(bool isShutdown = false) { }
            public void ReloadConnection() => throw new InvalidOperationException("Regression tests must not reload monitoring.");
            public bool TryStartSlideShow() => false;
            public bool TryEndSlideShow() => false;
            public bool TryNavigateToSlide(int slideNumber) => false;
            public bool TryNavigateNext() => false;
            public bool TryNavigatePrevious() => false;
            public bool TryShowSlideNavigation() => false;
            public List<PPTSlideThumbnail> ExportSlideThumbnails(int width, int height) => new List<PPTSlideThumbnail>();
            public void Dispose() { }
        }
    }

    // Public late-bound stubs must be visible to the separate core assembly's runtime binder.
    public sealed class CoreTransitionStub
    {
        public bool FailHidden, FailTiming, FailAdvanceTime;
        public int HiddenReads, TimingReads;
        public int Hidden
        {
            get { HiddenReads++; if (FailHidden) throw new COMException("Hidden unavailable."); return -1; }
        }
        public int AdvanceOnTime
        {
            get { TimingReads++; if (FailTiming) throw new COMException("AdvanceOnTime unavailable."); return -1; }
        }
        public float AdvanceTime => FailAdvanceTime ? throw new COMException("AdvanceTime unavailable.") : 1;
    }

    public sealed class CoreDetailsPresentationStub
    {
        private readonly CoreDetailsSlidesStub _slides;
        public bool FailSlides;
        public CoreDetailsPresentationStub(CoreTransitionStub transition) { _slides = new CoreDetailsSlidesStub(transition); }
        public CoreDetailsSlidesStub Slides => FailSlides ? throw new COMException("Slides unavailable.") : _slides;
    }

    public sealed class CoreDetailsSlidesStub
    {
        private readonly CoreDetailsSlideStub _slide;
        public CoreDetailsSlidesStub(CoreTransitionStub transition) { _slide = new CoreDetailsSlideStub { SlideShowTransition = transition }; }
        public int Count => 3;
        public CoreDetailsSlideStub this[int index] => index >= 1 && index <= Count ? _slide : throw new ArgumentOutOfRangeException(nameof(index));
    }

    public sealed class CoreDetailsSlideStub
    {
        public CoreTransitionStub SlideShowTransition { get; set; }
    }

    public sealed class CoreNavigationViewStub
    {
        public readonly List<string> Calls = new List<string>();
        public int TotalSlides;
        public void GotoSlide(int slide)
        {
            if (slide <= 0 || slide > TotalSlides) throw new COMException("Slide index out of range.");
            Calls.Add("goto:" + slide);
        }
        public void Next() => Calls.Add("next");
        public void Previous() => Calls.Add("previous");
    }
}
