using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InkCanvasLinkage.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Threading;

namespace Ink_Canvas.ViewModels
{
    public sealed class PresentationPreviewItem
    {
        public int SlideNumber { get; set; }
        public System.Windows.Media.Imaging.BitmapImage Thumbnail { get; set; }
    }

    public sealed partial class PresentationLinkViewModel : ObservableObject, IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly Action<Exception> _error;
        private readonly IPresentationLinkHost _host;
        private readonly object _gate = new object();
        private Task _tail = Task.CompletedTask;
        private int _generation;
        private readonly AsyncLocal<int?> _operationGeneration = new AsyncLocal<int?>();
        private IPresentationLinkSource _source;
        private Action _detachSource;
        private PresentationLinkState _state = new PresentationLinkState(false, false, 0, 0);
        private IList<PresentationPreviewItem> _previewItems;
        private bool _ownsSource;
        private readonly SemaphoreSlim _commandGate = new SemaphoreSlim(1, 1);

        public PresentationLinkViewModel(Dispatcher dispatcher, Action<Exception> error, IPresentationLinkHost host = null)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _error = error;
            _host = host;
            PreviousCommand = new AsyncRelayCommand(() => NavigateRelativeAsync(false), () => IsConnected && IsInSlideShow);
            NextCommand = new AsyncRelayCommand(() => NavigateRelativeAsync(true), () => IsConnected && IsInSlideShow);
            NavigateCommand = new AsyncRelayCommand<int>(async page =>
            {
                var state = _source?.State;
                int generation = Generation;
                await NavigateToSlideAsync(page);
                if (IsCurrentSession(state, generation, true)) CollapsePreview();
            },
                page => IsConnected && page > 0 && page <= TotalSlides);
            ShowNavigationCommand = new AsyncRelayCommand<int>(ShowNavigationAsync,
                _ => PageNavigationEnabled && IsConnected && IsInSlideShow);
            StartSlideShowCommand = new AsyncRelayCommand(() => RunSourceAsync(source => source.TryStartSlideShow()),
                () => IsConnected && !IsInSlideShow);
            EndSlideShowCommand = new AsyncRelayCommand(() => RunSourceAsync(source => source.TryEndSlideShow(), true,
                _ => _host?.BeforeEndSlideShowAsync() ?? Task.CompletedTask), () => IsConnected && IsInSlideShow);
            ReloadCommand = new AsyncRelayCommand(ReloadAsync, () => HasSource);
        }

        public PresentationLinkState State => _state;
        public int CurrentSlide => _state.CurrentSlide;
        public int TotalSlides => _state.TotalSlides;
        public bool IsConnected => _state.IsConnected;
        public bool IsInSlideShow => _state.IsInSlideShow;
        public PresentationMetadata Presentation => _state.Presentation;
        public PresentationCapabilities Capabilities => _source?.Capabilities ?? PresentationCapabilities.None;
        public string CurrentSlideText => CurrentSlide > 0 ? CurrentSlide.ToString() : "?";
        public string TotalSlidesText => TotalSlides > 0 ? $"/ {TotalSlides}" : "/ ?";
        public IList<PresentationPreviewItem> PreviewItems { get => _previewItems; private set => SetProperty(ref _previewItems, value); }
        public IAsyncRelayCommand PreviousCommand { get; }
        public IAsyncRelayCommand NextCommand { get; }
        public IAsyncRelayCommand<int> NavigateCommand { get; }
        public IAsyncRelayCommand<int> ShowNavigationCommand { get; }
        public IAsyncRelayCommand StartSlideShowCommand { get; }
        public IAsyncRelayCommand EndSlideShowCommand { get; }
        public IAsyncRelayCommand ReloadCommand { get; }
        internal IPresentationLinkSource Source => _source;
        public bool HasSource => _source != null;
        public int Generation => _generation;
        public bool IsCurrentOperation => !_operationGeneration.Value.HasValue || _operationGeneration.Value == _generation;
        public Task WhenIdle { get { lock (_gate) return _tail; } }

        public event Func<PresentationLinkState, Task> PresentationOpened;
        public event Func<PresentationLinkState, Task> PresentationClosed;
        public event Func<PresentationLinkState, Task> SlideShowStarted;
        public event Func<PresentationLinkState, Task> SlideChanged;
        public event Func<PresentationLinkState, Task> SlideShowEnded;
        public event Func<bool, Task> ConnectionChanged;
        public event Func<bool, Task> SlideShowStateChanged;
        public event Action SourceChanged;

        /// <summary>Observe a source. Use InitializeSource when this ViewModel owns its lifetime.</summary>
        public void Attach(IPresentationLinkSource source)
        {
            if (source != null && ReferenceEquals(source, _source)) return;
            if (_ownsSource) ReleaseSource();
            int generation;
            lock (_gate) generation = ++_generation;
            _detachSource?.Invoke();
            _detachSource = null;
            _source = source;
            _skipAnimationsWhenNavigating = source?.SkipAnimationsWhenNavigating == true;
            ResetPreviewCache();
            Apply(source?.State ?? new PresentationLinkState(false, false, 0, 0));
            if (source == null) { SourceChanged?.Invoke(); return; }
            // Capture subscription identity too: an event already in flight can outlive unsubscription.
            Action<PresentationLinkState> changed = state => Enqueue(() => { Apply(state); return Task.CompletedTask; }, generation);
            Action<PresentationLinkState> opened = state => Enqueue(async () =>
            {
                Apply(state);
                await Invoke(PresentationOpened, state);
                if (IsCurrentSession(state)) _ = NotifyPresentationOpenedAsync(state);
            }, generation);
            Action<PresentationLinkState> closed = state => Enqueue(() => Invoke(PresentationClosed, state), generation);
            Action<PresentationLinkState> began = state => Enqueue(async () =>
            {
                Apply(state);
                await Invoke(SlideShowStarted, state);
                await NavigateOnSlideShowBeginAsync(state);
            }, generation);
            Action<PresentationLinkState> slide = state => Enqueue(() => { Apply(state); return Invoke(SlideChanged, state); }, generation);
            Action<PresentationLinkState> ended = state => Enqueue(() =>
            {
                Apply(new PresentationLinkState(state.IsConnected, false, state.CurrentSlide, state.TotalSlides,
                    state.Presentation, state.PresentationSessionId, state.SlideShowSessionId));
                return Invoke(SlideShowEnded, state);
            }, generation);
            Action<bool> connection = value => Enqueue(() => Invoke(ConnectionChanged, value), generation);
            Action<bool> showState = value => Enqueue(() => Invoke(SlideShowStateChanged, value), generation);
            source.StateChanged += changed;
            source.PresentationOpen += opened;
            source.PresentationClose += closed;
            source.SlideShowBegin += began;
            source.SlideShowNextSlide += slide;
            source.SlideShowEnd += ended;
            source.PPTConnectionChanged += connection;
            source.SlideShowStateChanged += showState;
            _detachSource = () =>
            {
                source.StateChanged -= changed;
                source.PresentationOpen -= opened;
                source.PresentationClose -= closed;
                source.SlideShowBegin -= began;
                source.SlideShowNextSlide -= slide;
                source.SlideShowEnd -= ended;
                source.PPTConnectionChanged -= connection;
                source.SlideShowStateChanged -= showState;
            };
            SourceChanged?.Invoke();
        }

        private void Enqueue(Func<Task> work, int generation)
        {
            Task previous;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                if (generation != _generation) return;
                previous = _tail;
                _tail = completion.Task;
            }
            _dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await previous;
                    if (generation != _generation) return;
                    _operationGeneration.Value = generation;
                    await work();
                }
                catch (Exception ex) { _error?.Invoke(ex); }
                finally { completion.TrySetResult(true); }
            }));
        }

        private void Apply(PresentationLinkState state)
        {
            if (_state.PresentationSessionId != state.PresentationSessionId || _state.TotalSlides != state.TotalSlides
                || (_state.IsInSlideShow && !state.IsInSlideShow) || !state.IsConnected)
                ResetPreviewCache();
            _state = state;
            foreach (string property in new[] { nameof(State), nameof(CurrentSlide), nameof(TotalSlides), nameof(IsConnected),
                nameof(IsInSlideShow), nameof(Presentation), nameof(Capabilities), nameof(CurrentSlideText), nameof(TotalSlidesText),
                nameof(HasSource) })
                OnPropertyChanged(property);
            NotifyNavigationVisibility();
            PreviousCommand.NotifyCanExecuteChanged(); NextCommand.NotifyCanExecuteChanged();
            NavigateCommand.NotifyCanExecuteChanged(); ShowNavigationCommand.NotifyCanExecuteChanged();
            StartSlideShowCommand.NotifyCanExecuteChanged(); EndSlideShowCommand.NotifyCanExecuteChanged(); ReloadCommand.NotifyCanExecuteChanged();
        }

        private async Task Invoke<T>(Func<T, Task> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Func<T, Task> handler in handlers.GetInvocationList())
            {
                if (!IsCurrentOperation) return;
                await handler(value);
            }
        }
        public void Dispose()
        {
            if (_ownsSource) ReleaseSource();
            else Attach(null);
        }
    }
}
