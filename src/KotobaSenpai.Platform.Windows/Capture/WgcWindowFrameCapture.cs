using System.Diagnostics;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace KotobaSenpai.Platform.Windows.Capture;

/// <summary>Captures one selected window independently of desktop occlusion.</summary>
public sealed class WgcWindowFrameCapture : IWindowFrameCapture, IDisposable
{
    private readonly object _gate = new();
    private readonly ITargetWindowTracker? _tracker;
    private Session? _session;
    private Task _cleanup = Task.CompletedTask;
    private long _stopVersion;
    private bool _disposed;

    public WgcWindowFrameCapture(ITargetWindowTracker? tracker = null)
    {
        _tracker = tracker;
        if (_tracker is not null) _tracker.Changed += OnTargetChanged;
    }

    public Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        var version = Volatile.Read(ref _stopVersion);
        return Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew();
            for (var attempt = 0; ; attempt++)
            {
                Session session;
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (version != _stopVersion) throw new OperationCanceledException("Capture was stopped before initialization.");
                    if (_session is not null && (_session.Handle != target.Handle || _session.IsStopped))
                        ReleaseSession();
                    session = _session ??= new Session(target.Handle);
                }
                try { return await session.CaptureAsync(target, region, TimeSpan.FromSeconds(2) - clock.Elapsed, cancellationToken).ConfigureAwait(false); }
                catch (LayoutChangedException) when (attempt == 0)
                {
                    // WGC can retain an obsolete non-client origin after style/maximize/DPI transitions.
                    // Recreate the item/session instead of correcting pixels with a guessed border offset.
                    lock (_gate)
                        if (ReferenceEquals(_session, session)) ReleaseSession();
                    await session.Stop().ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    lock (_gate)
                        if (ReferenceEquals(_session, session)) ReleaseSession();
                    if (ex is WindowsPlatformException) throw;
                    throw new WindowsPlatformException(ErrorCodes.WindowCaptureFailed, "Windows Graphics Capture failed.", ex);
                }
            }
        }, cancellationToken);
    }

    public void Stop()
    {
        lock (_gate)
        {
            _stopVersion++;
            ReleaseSession();
        }
    }

    public void Dispose()
    {
        Task cleanup;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stopVersion++;
            ReleaseSession();
            cleanup = _cleanup;
        }
        if (_tracker is not null) _tracker.Changed -= OnTargetChanged;
        cleanup.GetAwaiter().GetResult();
    }

    private void ReleaseSession()
    {
        if (_session is null) return;
        _cleanup = Task.WhenAll(_cleanup, _session.Stop());
        _session = null;
    }

    private void OnTargetChanged(object? sender, TargetWindowSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_session is null || (snapshot.Handle == _session.Handle && snapshot.IsCapturable)) return;
            _session.Stop(new WindowsPlatformException(ErrorCodes.TargetWindowUnavailable, "The tracked capture target is no longer available."));
            _stopVersion++;
            ReleaseSession();
        }
    }

    private sealed class LayoutChangedException() : Exception("The target window layout changed during capture.");

    private sealed class Session(nint handle)
    {
        private static long _nextGeneration;
        private readonly object _nativeGate = new();
        private readonly object _stopGate = new();
        private TaskCompletionSource _changed = NewSignal();
        private Task? _cleanupTask;
        private int _stopped;
        private Exception? _failure;
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDirect3DDevice? _winrtDevice;
        private GraphicsCaptureItem? _item;
        private Direct3D11CaptureFramePool? _pool;
        private GraphicsCaptureSession? _capture;
        private ID3D11Texture2D? _latest;
        private ID3D11Texture2D? _staging;
        private SizeInt32 _bufferSize;
        private ScreenRect? _latestClient;
        private uint _processId;
        private uint _threadId;
        private WgcInterop.WindowLayout _layout;
        private long _generation = Interlocked.Increment(ref _nextGeneration);

        internal nint Handle => handle;
        internal bool IsStopped => Volatile.Read(ref _stopped) != 0;

        internal async Task<CapturedFrame> CaptureAsync(WindowTarget target, ScreenRect? region, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var changed = Volatile.Read(ref _changed).Task;
                lock (_nativeGate)
                {
                    ThrowIfStopped();
                    var geometry = WgcInterop.ReadGeometry(handle);
                    if (geometry.ClientBounds.Width != target.Bounds.Width || geometry.ClientBounds.Height != target.Bounds.Height)
                        throw new WindowsPlatformException(ErrorCodes.TargetWindowUnavailable, "The target client size changed; retry with its current bounds.");
                    if (_device is null) Initialize(geometry);
                    if (_processId != geometry.ProcessId || _threadId != geometry.ThreadId)
                        throw new WindowsPlatformException(ErrorCodes.TargetWindowUnavailable, "The selected window identity changed.");
                    if (_layout != geometry.Layout) throw new LayoutChangedException();

                    if (_latest is not null)
                    {
                        var bounds = WgcInterop.MatchFrameBounds(handle, geometry, (int)_latest.Description.Width, (int)_latest.Description.Height);
                        var client = bounds is { } b ? WgcFrameLayout.ResolveCrop(b, geometry.ClientBounds, b.Width, b.Height, null) : null;
                        if (client is not null && client == _latestClient)
                        {
                            var crop = WgcFrameLayout.ResolveCrop(bounds!.Value, geometry.ClientBounds, bounds.Value.Width, bounds.Value.Height, region)!.Value;
                            var frame = ReadPixels(crop);
                            ThrowIfStopped();
                            cancellationToken.ThrowIfCancellationRequested();
                            var after = WgcInterop.ReadGeometry(handle);
                            if (after.Layout != _layout) throw new LayoutChangedException();
                            if (after == geometry) return frame;
                        }
                        InvalidateFrame();
                    }
                }
                var remaining = timeout - clock.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    throw new WindowsPlatformException(ErrorCodes.WindowCaptureTimedOut, "No geometrically valid window frame arrived within two seconds.");
                try { await changed.WaitAsync(remaining, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lock (_nativeGate) ThrowIfStopped();
                    throw;
                }
                catch (TimeoutException ex)
                {
                    throw new WindowsPlatformException(ErrorCodes.WindowCaptureTimedOut, "No geometrically valid window frame arrived within two seconds.", ex);
                }
            }
        }

        private void Initialize(WgcInterop.Geometry geometry)
        {
            if (!GraphicsCaptureSession.IsSupported())
                throw new WindowsPlatformException(ErrorCodes.WindowCaptureNotSupported, "Windows Graphics Capture is not supported in this Windows session.");
            _device = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            _context = _device.ImmediateContext;
            _winrtDevice = WgcInterop.CreateWinrtDevice(_device);
            _item = WgcInterop.CreateItem(handle);
            _processId = geometry.ProcessId;
            _threadId = geometry.ThreadId;
            _layout = geometry.Layout;
            _bufferSize = _item.Size;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _bufferSize);
            _capture = _pool.CreateCaptureSession(_item);
            _capture.IsCursorCaptureEnabled = false;
            _pool.FrameArrived += OnFrameArrived;
            _item.Closed += OnClosed;
            _capture.StartCapture();
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            lock (_nativeGate)
            {
                if (IsStopped) return;
                try
                {
                    SizeInt32 size;
                    using (var frame = sender.TryGetNextFrame())
                    {
                        if (frame is null || frame.ContentSize.Width <= 0 || frame.ContentSize.Height <= 0) return;
                        size = frame.ContentSize;
                        if (size.Width == _bufferSize.Width && size.Height == _bufferSize.Height)
                        {
                            CacheFrame(frame);
                            return;
                        }
                    }
                    InvalidateFrame();
                    _bufferSize = size;
                    sender.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _bufferSize);
                    Signal();
                }
                catch (Exception ex)
                {
                    _failure = ex;
                    Stop();
                }
            }
        }

        private void CacheFrame(Direct3D11CaptureFrame frame)
        {
            var geometry = WgcInterop.ReadGeometry(handle);
            if (geometry.Layout != _layout)
            {
                InvalidateFrame();
                Signal();
                return;
            }
            using var texture = WgcInterop.GetTexture(frame.Surface);
            if (texture.Description.Width < frame.ContentSize.Width || texture.Description.Height < frame.ContentSize.Height) return;
            var bounds = WgcInterop.MatchFrameBounds(handle, geometry, frame.ContentSize.Width, frame.ContentSize.Height);
            var client = bounds is { } b ? WgcFrameLayout.ResolveCrop(b, geometry.ClientBounds, frame.ContentSize.Width, frame.ContentSize.Height, null) : null;
            if (client is null) return;
            if (_latestClient is not null && _latestClient != client) InvalidateFrame();
            if (_latest is null)
            {
                var description = texture.Description;
                description.Width = (uint)frame.ContentSize.Width;
                description.Height = (uint)frame.ContentSize.Height;
                description.Usage = ResourceUsage.Default;
                description.BindFlags = BindFlags.None;
                description.CPUAccessFlags = CpuAccessFlags.None;
                description.MiscFlags = ResourceOptionFlags.None;
                _latest = _device!.CreateTexture2D(description);
            }
            _context!.CopySubresourceRegion(_latest, 0, 0, 0, 0, texture, 0,
                new Vortice.Mathematics.Box(0, 0, 0, frame.ContentSize.Width, frame.ContentSize.Height, 1));
            _latestClient = client;
            Signal();
        }

        private CapturedFrame ReadPixels(ScreenRect crop)
        {
            if (_staging is null || _staging.Description.Width != crop.Width || _staging.Description.Height != crop.Height)
            {
                _staging?.Dispose();
                _staging = _device!.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)crop.Width, Height = (uint)crop.Height, ArraySize = 1, MipLevels = 1,
                    Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read
                });
            }
            _context!.CopySubresourceRegion(_staging, 0, 0, 0, 0, _latest, 0,
                new Vortice.Mathematics.Box(crop.X, crop.Y, 0, crop.Right, crop.Bottom, 1));
            var mapped = _context.Map(_staging, 0, MapMode.Read);
            try { return WgcFrameLayout.CopyRows(mapped.DataPointer, checked((int)mapped.RowPitch), crop.Width, crop.Height, _generation); }
            finally { _context.Unmap(_staging, 0); }
        }

        private void InvalidateFrame()
        {
            _latest?.Dispose();
            _latest = null;
            _latestClient = null;
            _generation = Interlocked.Increment(ref _nextGeneration);
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        private void Signal() => Interlocked.Exchange(ref _changed, NewSignal()).TrySetResult();

        private void ThrowIfStopped()
        {
            var failure = Volatile.Read(ref _failure);
            if (failure is WindowsPlatformException platformError) throw platformError;
            if (failure is not null) throw new WindowsPlatformException(ErrorCodes.WindowCaptureFailed, "The window capture session failed.", failure);
            if (IsStopped) throw new OperationCanceledException("The window capture session was stopped.");
        }

        private void OnClosed(GraphicsCaptureItem sender, object args)
        {
            _failure = new WindowsPlatformException(ErrorCodes.TargetWindowUnavailable, "The selected capture window was closed.");
            Stop();
        }

        internal Task Stop(Exception? failure = null)
        {
            lock (_stopGate)
            {
                if (failure is not null) Interlocked.CompareExchange(ref _failure, failure, null);
                if (_cleanupTask is not null) return _cleanupTask;
                Volatile.Write(ref _stopped, 1);
                Volatile.Read(ref _changed).TrySetCanceled();
                return _cleanupTask = Task.Run(Cleanup);
            }
        }

        private void Cleanup()
        {
            IDisposable?[] resources;
            lock (_nativeGate)
            {
                if (_pool is not null) _pool.FrameArrived -= OnFrameArrived;
                if (_item is not null) _item.Closed -= OnClosed;
                resources = [_capture, _pool, _staging, _latest, _winrtDevice, _context, _device];
                _capture = null;
                _pool = null;
                _staging = null;
                _latest = null;
                _winrtDevice = null;
                _context = null;
                _device = null;
                _item = null;
            }
            // Dispose outside the native gate so queued callbacks can observe Stop and leave immediately.
            foreach (var resource in resources) resource?.Dispose();
        }
    }
}
