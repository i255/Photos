using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class WindowAdapter
    {
        object _lock = new object();
        public long TotalMemoryHintKB;
        public int MaxRenderTargetSize;
        public Action Close, Invalidate;
        public Action<bool> Minimize;
        public Action<string> SetClipboard;
        public Func<string> GetClipboard;
        public Func<SKPoint?> MousePosition;
        public Func<bool> IsBorderless;
        public GRContext GR;
        public Func<string, string> OpenUrl;
        public bool ShowGrid;
        public float ScaleFactor = 1;
        public bool ManualClickDetection;
        public bool FixedScaleFactor;
        public string DeviceName = Environment.MachineName;
        public string StoragePrefix;

        DateTime[] _renderHist = new DateTime[300];

        internal readonly List<Animation> _animations = new();
        DateTime _lastRender;
        readonly List<TaskCompletionSource<SKImage>> _screenshots = new();

        public BaseView RootView { get; private set; }

        public void RouteMouseEvent(libMouseEvent e)
        {
            if (_disposed || !_initialized)
                return;

            lock (_lock)
                RootView.RouteMouseEvent(e);
        }

        public void RouteKeyEvent(libKeyMessage e)
        {
            if (_disposed || !_initialized)
                return;

#if DEBUG
            if (e.Key == libKeys.G)
                ShowGrid = !ShowGrid;
#endif
            lock (_lock)
                RootView.RouteKeyEvent(e);
        }

        internal bool LayoutChanged;
        int _cntr;
        public void RenderFrame(SKSurface surface)
        {
            if (_disposed || !_initialized)
                return;

            _lastRender = _renderHist[_cntr] = DateTime.UtcNow;

            //bool lockWasTaken = false;
            //try
            //{
            //    Monitor.TryEnter(_lock, 50, ref lockWasTaken);
            //    if (lockWasTaken)
            //    {
            //        ctx.Clear(SKColors.Black.WithAlpha(0x90));
            //        RootView.RouteRenderFrame(ctx);
            //    }
            //}
            //finally
            //{
            //    if (lockWasTaken) 
            //        Monitor.Exit(_lock);
            //}

            lock (_lock)
            {
                Animate();

                for (int i = 0; i < 5; i++)
                {
                    LayoutChanged = false;
                    RootView.Layout(surface.Canvas.DeviceClipBounds);
                    if (!LayoutChanged)
                        break;
                };

                if (LayoutChanged)
                    throw new Exception("no stable layout");

                RootView.RouteRenderFrame(surface.Canvas);

                if (_screenshots.Count > 0)
                {
                    var shot = surface.Snapshot();
                    foreach (var item in _screenshots)
                        item.SetResult(shot);
                    _screenshots.Clear();
                }
            }

            _renderHist[_cntr + 1] = DateTime.UtcNow;
            _cntr = (_cntr + 2) % _renderHist.Length;

            RenderFinished?.Invoke(_lastRender);

        }

        public Task<SKImage> GetScreenshot()
        {
            lock (_lock)
            {
                var res = new TaskCompletionSource<SKImage>();
                _screenshots.Add(res);
                return res.Task;
            }
        }

        public event Action<DateTime> RenderFinished;

        public string GetRenderStats()
        {
            double sum = 0;
            double max = 0;
            int cnt = 0;
            var dt = DateTime.UtcNow.AddSeconds(-1);

            for (int i = 0; i < _renderHist.Length; i += 2)
            {
                if (i == _cntr || _renderHist[i] == default)
                    continue;

                var ms = (_renderHist[i + 1] - _renderHist[i]).TotalMilliseconds;
                max = Math.Max(max, ms);

                sum += ms;
                if (_renderHist[i] > dt)
                    cnt++;
            }

            return $"avg {sum / _renderHist.Length * 2:0.0}; max {max:0.0}; fps: {cnt + 1}, mem {GetMemUsage()} MB";
        }

        readonly Process currentProc = Process.GetCurrentProcess();
        DateTime _lastProcUpdate;
        long _lastMemValue;
        long GetMemUsage()
        {
            var dt = DateTime.UtcNow;
            if ((dt - _lastProcUpdate).TotalSeconds > 1.2)
            {
                _lastProcUpdate = dt;
                currentProc.Refresh();
                _lastMemValue = currentProc.WorkingSet64 / 1024 / 1024;
            }

            return _lastMemValue;
        }
        public bool Initialized => _initialized;
        public void Initialize(BaseView rootView)
        {
            if (_initialized)
                throw new Exception("initialized");

            lock (_lock)
            {
                RootView = rootView;
                _ = RunTimer();

                InitializeInternal(RootView);
                _initialized = true;
            }
        }

        internal void InitializeInternal(BaseView target)
        {
            if (target.Window != null)
                throw new Exception("initialized");

            target.Window = this;
            foreach (var item in target.Children)
                InitializeInternal(item);
            target.Init();
            OnInit?.Invoke(target);
        }

        bool _disposed, _initialized;

        public void Dispose()
        {
            _disposed = true;
            lock (_lock)
                RootView?.Dispose();
        }

        public event Action<BaseView> OnInit;

        private void Animate()
        {
            foreach (var item in _animations.ToArray())
                item.Run();

            if (_animations.Count > 0)
                Invalidate();
        }

        const int ForcedRenderTimeMs = 300;

        async Task RunTimer()
        {
            var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ForcedRenderTimeMs / 3));

            while (await timer.WaitForNextTickAsync())
            {
                if (_disposed)
                    return;

                if ((DateTime.UtcNow - _lastRender).TotalMilliseconds > ForcedRenderTimeMs)
                    Invalidate?.Invoke();
            }
        }

        public void EnsureWindowThread()
        {
            if (!Monitor.IsEntered(_lock))
                throw new Exception("bad thread");
        }

        public void Run(Action value)
        {
            lock (_lock)
                value();
        }

    }

}
