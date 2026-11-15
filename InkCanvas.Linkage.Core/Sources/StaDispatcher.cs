using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace InkCanvasLinkage.Core.Sources
{
    internal sealed class StaDispatcher : IDisposable
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Thread _thread;
        private readonly LogHelper _log;
        private readonly object _gate = new object();
        private bool _stopping;

        public StaDispatcher(Action<string, PresentationLogLevel> log)
        {
            _log = new LogHelper(log);
            _thread = new Thread(Run) { IsBackground = true, Name = "Presentation linkage STA" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public bool CheckAccess => Thread.CurrentThread == _thread;
        public void Post(Action action)
        {
            lock (_gate)
            {
                if (_stopping) return;
                _queue.Enqueue(action);
                _wake.Set();
            }
        }

        public T Invoke<T>(Func<T> action)
        {
            if (CheckAccess) return action();
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                if (_stopping) throw new ObjectDisposedException(nameof(StaDispatcher));
                _queue.Enqueue(() =>
                {
                    try { completion.SetResult(action()); }
                    catch (Exception ex) { completion.SetException(ex); }
                });
                _wake.Set();
            }
            return completion.Task.GetAwaiter().GetResult();
        }
        public void Invoke(Action action) => Invoke(() => { action(); return true; });

        private void Run()
        {
            int hr = OleInitialize(IntPtr.Zero);
            try
            {
                var handles = new[] { _wake.SafeWaitHandle.DangerousGetHandle() };
                while (true)
                {
                    // COM callbacks require a message pump even while no work is queued.
                    while (PeekMessage(out var message, IntPtr.Zero, 0, 0, 1))
                    {
                        TranslateMessage(ref message);
                        DispatchMessage(ref message);
                    }
                    if (_queue.TryDequeue(out var action))
                    {
                        try { action(); }
                        catch (Exception ex) { _log.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error); }
                        continue;
                    }
                    lock (_gate) { if (_stopping) break; }
                    MsgWaitForMultipleObjects(1, handles, false, uint.MaxValue, 0x04FF);
                }
            }
            finally
            {
                if (hr >= 0) OleUninitialize();
                _wake.Dispose();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_stopping) return;
                _stopping = true;
                _wake.Set();
            }
            if (!CheckAccess) _thread.Join();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr Hwnd;
            public uint Id;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X, Y;
            public uint Private;
        }
        [DllImport("ole32.dll")] private static extern int OleInitialize(IntPtr reserved);
        [DllImport("ole32.dll")] private static extern void OleUninitialize();
        [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds, uint wakeMask);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    }
}
