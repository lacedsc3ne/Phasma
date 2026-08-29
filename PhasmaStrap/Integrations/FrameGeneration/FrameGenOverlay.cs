using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.DirectComposition;
using Vortice.Mathematics;
using D3D11 = Vortice.Direct3D11.D3D11;
using DCompApi = Vortice.DirectComposition.DComp;

namespace PhasmaStrap.Integrations.FrameGeneration
{
    /// <summary>
    /// Self-contained shader-based frame interpolation overlay.
    ///
    /// This is intentionally built as its own standalone D3D11 + DirectComposition
    /// pipeline (own device, own topmost click-through window, own desktop-duplication
    /// capture, own composition swapchain) rather than hooking into a shared overlay
    /// compositor, because no such shared "Overlays"/"RiShade" infrastructure exists
    /// in this checkout of PhasmaStrap at the time this was written.
    ///
    /// It captures the Roblox window via DXGI desktop duplication, tracks the last two
    /// captured frames, and uses <see cref="FrameGenPipeline"/> (optical-flow based
    /// warping) to synthesize an in-between frame that is displayed while waiting for
    /// the next real captured frame to arrive. The result is presented through a
    /// borderless, click-through, always-on-top window placed exactly over the Roblox
    /// window using DirectComposition, so Roblox itself keeps rendering normally
    /// underneath and the user only perceives the smoothed output on top.
    /// </summary>
    internal static class FrameGenOverlay
    {
        private static FrameGenRuntime? _runtime;
        private static readonly object _sync = new();

        public static void Start()
        {
            lock (_sync)
            {
                if (_runtime != null)
                    return;

                if (FrameGenSettings.ModeIndex <= 0)
                    return;

                var runtime = new FrameGenRuntime();
                if (runtime.TryStart())
                    _runtime = runtime;
                else
                    runtime.Dispose();
            }
        }

        public static void Stop()
        {
            FrameGenRuntime? runtime;
            lock (_sync)
            {
                runtime = _runtime;
                _runtime = null;
            }
            runtime?.Dispose();
        }

        public static void Refresh()
        {
            if (FrameGenSettings.ModeIndex > 0)
                Start();
            else
                Stop();
        }
    }

    internal sealed class FrameGenRuntime : IDisposable
    {
        private const string LOG_IDENT = "FrameGenOverlay";
        private const string ClassName = "PhasmaStrapFrameGenOverlay";
        private const string WindowTitle = "PhasmaStrap Frame Generation";

        private Thread? _thread;
        private volatile bool _running;
        private readonly ManualResetEventSlim _stopped = new(false);

        // window
        private Interop.WndProcDelegate? _wndProc;
        private IntPtr _hwnd;
        private ushort _classAtom;
        private IntPtr _hInstance;
        private bool _windowVisible;

        // device / swapchain / composition
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGIFactory2? _factory;
        private IDXGISwapChain1? _swapChain;
        private ID3D11Texture2D? _backBufferTex;
        private ID3D11RenderTargetView? _backBufferRtv;
        private IDCompositionDevice? _dcompDevice;
        private IDCompositionTarget? _dcompTarget;
        private IDCompositionVisual? _dcompVisual;

        // capture
        private IDXGIOutputDuplication? _duplication;
        private int _outputLeft, _outputTop, _outputRight, _outputBottom;
        private int _captureFailures;

        // roblox window tracking
        private IntPtr _robloxHwnd;
        private int _rectLeft, _rectTop, _width, _height;
        private double _nextWindowProbeMs;

        // color history (double buffered)
        private readonly ID3D11Texture2D?[] _colorTex = new ID3D11Texture2D?[2];
        private readonly ID3D11ShaderResourceView?[] _colorSrv = new ID3D11ShaderResourceView?[2];
        private int _curSet;
        private int _capturedFrames;
        private double _prevCaptureMs;
        private double _currCaptureMs;
        private double _intervalMs;

        private readonly FrameGenPipeline _pipeline = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public bool TryStart()
        {
            _running = true;
            _thread = new Thread(RunSafe)
            {
                IsBackground = true,
                Name = "PhasmaStrap FrameGen",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            return true;
        }

        private void RunSafe()
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }
            finally
            {
                Cleanup();
                _stopped.Set();
            }
        }

        private void Run()
        {
            CreateWindow();
            CreateDevice();
            _pipeline.Attach(_device!, _context!);
            _pipeline.SetQuality(FrameGenSettings.QualityIndex);

            App.Logger.WriteLine(LOG_IDENT, "Frame generation overlay started");

            while (_running)
            {
                if (!ResolveRobloxRect(out var rect))
                {
                    SetWindowVisible(false);
                    Thread.Sleep(200);
                    continue;
                }

                bool sizeChanged = rect.Right - rect.Left != _width || rect.Bottom - rect.Top != _height;
                bool posChanged = rect.Left != _rectLeft || rect.Top != _rectTop;
                _rectLeft = rect.Left;
                _rectTop = rect.Top;

                if (sizeChanged || _swapChain == null)
                {
                    _width = Math.Max(16, rect.Right - rect.Left);
                    _height = Math.Max(16, rect.Bottom - rect.Top);
                    if (!EnsureSwapChain())
                    {
                        Thread.Sleep(200);
                        continue;
                    }
                    EnsureColorTextures();
                    _pipeline.EnsureSize(_width, _height);
                    _capturedFrames = 0;
                    _pipeline.ResetHistory();
                    EnsureDuplication(force: true);
                }
                else if (posChanged)
                {
                    Interop.SetWindowPos(_hwnd, IntPtr.Zero, _rectLeft, _rectTop, _width, _height, Interop.SWP_NOACTIVATE | Interop.SWP_NOZORDER);
                }

                SetWindowVisible(true);
                EnsureDuplication(force: false);

                bool gotFrame = TryCaptureFrame();
                if (gotFrame)
                    _captureFailures = 0;

                RenderAndPresent();

                // Desktop duplication AcquireNextFrame(0) is non-blocking, so pace the loop
                // a little; the actual cadence is ultimately capped by Present()'s vsync wait.
                if (!gotFrame)
                    Thread.Sleep(1);
            }
        }

        private void SetWindowVisible(bool visible)
        {
            if (_windowVisible == visible || _hwnd == IntPtr.Zero)
                return;
            _windowVisible = visible;
            Interop.ShowWindow(_hwnd, visible ? Interop.SW_SHOWNOACTIVATE : Interop.SW_HIDE);
        }

        private void CreateWindow()
        {
            _hInstance = Interop.GetModuleHandleW(null);
            _wndProc = (h, m, w, l) => Interop.DefWindowProcW(h, m, w, l);
            IntPtr classNamePtr = Marshal.StringToHGlobalUni(ClassName);
            try
            {
                var wc = new Interop.WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<Interop.WNDCLASSEXW>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = _hInstance,
                    lpszClassName = classNamePtr,
                };
                _classAtom = Interop.RegisterClassExW(ref wc);

                int exStyle = Interop.WS_EX_NOACTIVATE | Interop.WS_EX_TOOLWINDOW | Interop.WS_EX_TRANSPARENT | Interop.WS_EX_TOPMOST | Interop.WS_EX_LAYERED | Interop.WS_EX_NOREDIRECTIONBITMAP;
                _hwnd = Interop.CreateWindowExW(exStyle, new IntPtr(_classAtom), WindowTitle, Interop.WS_POPUP, 0, 0, 64, 64, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
                Interop.SetLayeredWindowAttributes(_hwnd, 0, 255, Interop.LWA_ALPHA);
                App.Logger.WriteLine(LOG_IDENT, "Overlay window created, click-through, topmost");
            }
            finally
            {
                Marshal.FreeHGlobal(classNamePtr);
            }
        }

        private static readonly FeatureLevel[] _featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };

        private void CreateDevice()
        {
            _factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
            try
            {
                D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, _featureLevels, out _device, out _context);
                App.Logger.WriteLine(LOG_IDENT, $"D3D11 device created, feature level {_device!.FeatureLevel}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Hardware device unavailable, falling back to WARP: " + ex.Message);
                D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, _featureLevels, out _device, out _context);
            }
        }

        private bool EnsureSwapChain()
        {
            try
            {
                DisposeSwapChain();

                var swapDesc = new SwapChainDescription1
                {
                    Width = _width,
                    Height = _height,
                    Format = Format.B8G8R8A8_UNorm,
                    Stereo = false,
                    SampleDescription = new SampleDescription(1, 0),
                    BufferUsage = Usage.RenderTargetOutput,
                    BufferCount = 2,
                    Scaling = Scaling.Stretch,
                    SwapEffect = SwapEffect.FlipDiscard,
                    AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
                    Flags = SwapChainFlags.None,
                };

                _swapChain = _factory!.CreateSwapChainForComposition(_device!, swapDesc, null);
                _backBufferTex = _swapChain.GetBuffer<ID3D11Texture2D>(0);
                _backBufferRtv = _device!.CreateRenderTargetView(_backBufferTex);

                using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
                _dcompDevice?.Dispose();
                DCompApi.DCompositionCreateDevice(dxgiDevice, out IDCompositionDevice? dcompDevice);
                _dcompDevice = dcompDevice;
                _dcompDevice!.CreateTargetForHwnd(_hwnd, true, out _dcompTarget);
                _dcompVisual = _dcompDevice.CreateVisual();
                _dcompVisual.SetContent(_swapChain);
                _dcompTarget!.SetRoot(_dcompVisual);
                _dcompDevice.Commit();

                Interop.SetWindowPos(_hwnd, Interop.HWND_TOPMOST, _rectLeft, _rectTop, _width, _height, Interop.SWP_NOACTIVATE | Interop.SWP_SHOWWINDOW);

                App.Logger.WriteLine(LOG_IDENT, $"Composition swapchain ({_width}x{_height}) attached to overlay window");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::EnsureSwapChain", ex);
                return false;
            }
        }

        private void EnsureColorTextures()
        {
            for (int i = 0; i < 2; i++)
            {
                _colorSrv[i]?.Dispose();
                _colorTex[i]?.Dispose();
                _colorTex[i] = _device!.CreateTexture2D(new Texture2DDescription
                {
                    Width = _width,
                    Height = _height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                });
                _colorSrv[i] = _device.CreateShaderResourceView(_colorTex[i]);
            }
            _curSet = 0;
        }

        private void EnsureDuplication(bool force)
        {
            if (!force && _duplication != null && _captureFailures < 8)
                return;

            try
            {
                _duplication?.Dispose();
                _duplication = null;

                using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
                dxgiDevice.GetAdapter(out var adapter);
                using (adapter)
                {
                    int cx = _rectLeft + _width / 2;
                    int cy = _rectTop + _height / 2;
                    for (int i = 0; ; i++)
                    {
                        var res = adapter.EnumOutputs(i, out var output);
                        if (res.Failure || output == null)
                            break;
                        try
                        {
                            var dc = output.Description.DesktopCoordinates;
                            bool contains = cx >= dc.Left && cx < dc.Right && cy >= dc.Top && cy < dc.Bottom;
                            if (contains)
                            {
                                _outputLeft = dc.Left;
                                _outputTop = dc.Top;
                                _outputRight = dc.Right;
                                _outputBottom = dc.Bottom;
                                using var output1 = output.QueryInterface<IDXGIOutput1>();
                                _duplication = output1.DuplicateOutput(_device!);
                                _captureFailures = 0;
                                App.Logger.WriteLine(LOG_IDENT, $"Desktop duplication bound to monitor {dc.Left},{dc.Top}..{dc.Right},{dc.Bottom}");
                                return;
                            }
                        }
                        finally
                        {
                            output.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not create desktop duplication: " + ex.Message);
            }
        }

        private bool TryCaptureFrame()
        {
            if (_duplication == null)
                return false;

            IDXGIResource? desktopResource = null;
            try
            {
                _duplication.AcquireNextFrame(0, out _, out desktopResource);
                if (desktopResource == null)
                    return false;
            }
            catch (Exception ex)
            {
                desktopResource?.Dispose();
                // DXGI_ERROR_WAIT_TIMEOUT just means no new frame is ready yet; anything else
                // counts toward triggering a duplication rebuild.
                if (unchecked((uint)ex.HResult) != 0x887A0027)
                    _captureFailures++;
                return false;
            }

            try
            {
                using var srcTex = desktopResource.QueryInterface<ID3D11Texture2D>();

                int nextSet = _curSet ^ 1;
                int left = Math.Clamp(_rectLeft - _outputLeft, 0, Math.Max(0, _outputRight - _outputLeft - 1));
                int top = Math.Clamp(_rectTop - _outputTop, 0, Math.Max(0, _outputBottom - _outputTop - 1));
                int right = Math.Clamp(left + _width, left + 1, _outputRight - _outputLeft);
                int bottom = Math.Clamp(top + _height, top + 1, _outputBottom - _outputTop);

                var box = new Box(left, top, 0, right, bottom, 1);
                _context!.CopySubresourceRegion(_colorTex[nextSet]!, 0, 0, 0, 0, srcTex, 0, box);

                _pipeline.BuildPyramid(nextSet, _colorSrv[nextSet]!);

                double now = _clock.Elapsed.TotalMilliseconds;
                if (_capturedFrames > 0)
                {
                    _pipeline.ComputeFlow(_curSet, nextSet, 12f);
                    _intervalMs = Math.Max(1.0, now - _currCaptureMs);
                }
                _prevCaptureMs = _currCaptureMs;
                _currCaptureMs = now;
                _curSet = nextSet;
                _capturedFrames++;
                return true;
            }
            finally
            {
                desktopResource.Dispose();
                try { _duplication.ReleaseFrame(); } catch { }
            }
        }

        private void RenderAndPresent()
        {
            if (_backBufferRtv == null || _colorSrv[_curSet] == null)
                return;

            if (_capturedFrames >= 2 && _intervalMs > 0.5)
            {
                double now = _clock.Elapsed.TotalMilliseconds;
                float t = (float)Math.Clamp((now - _currCaptureMs) / _intervalMs, 0.0, 1.0);
                int prevSet = _curSet ^ 1;
                _pipeline.Warp(_colorSrv[prevSet]!, _colorSrv[_curSet]!, t, _backBufferRtv);
            }
            else if (_capturedFrames >= 1)
            {
                _pipeline.Blit(_colorSrv[_curSet]!, _backBufferRtv);
            }
            else
            {
                return;
            }

            try
            {
                _swapChain!.Present(1, PresentFlags.None);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Present failed: " + ex.Message);
            }
        }

        private bool ResolveRobloxRect(out Interop.RECT rect)
        {
            rect = default;

            double nowMs = _clock.Elapsed.TotalMilliseconds;
            if (_robloxHwnd == IntPtr.Zero || !Interop.IsWindow(_robloxHwnd) || nowMs >= _nextWindowProbeMs)
            {
                _nextWindowProbeMs = nowMs + 500.0;
                _robloxHwnd = FindRobloxWindow();
            }

            if (_robloxHwnd == IntPtr.Zero || !Interop.IsWindow(_robloxHwnd))
                return false;

            if (!Interop.GetWindowRect(_robloxHwnd, out rect))
                return false;

            if (rect.Right - rect.Left < 32 || rect.Bottom - rect.Top < 32)
                return false;

            return true;
        }

        private static IntPtr FindRobloxWindow()
        {
            try
            {
                var candidates = Process.GetProcessesByName("RobloxPlayerBeta");
                try
                {
                    foreach (var proc in candidates)
                    {
                        using (proc)
                        {
                            IntPtr h = proc.MainWindowHandle;
                            if (h != IntPtr.Zero && Interop.IsWindow(h))
                                return h;
                        }
                    }
                }
                finally
                {
                    foreach (var p in candidates)
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch
            {
            }
            return IntPtr.Zero;
        }

        private void DisposeSwapChain()
        {
            _dcompVisual?.Dispose();
            _dcompVisual = null;
            _dcompTarget?.Dispose();
            _dcompTarget = null;
            _dcompDevice?.Dispose();
            _dcompDevice = null;
            _backBufferRtv?.Dispose();
            _backBufferRtv = null;
            _backBufferTex?.Dispose();
            _backBufferTex = null;
            _swapChain?.Dispose();
            _swapChain = null;
        }

        private void Cleanup()
        {
            try
            {
                _duplication?.Dispose();
                _duplication = null;

                for (int i = 0; i < 2; i++)
                {
                    _colorSrv[i]?.Dispose();
                    _colorSrv[i] = null;
                    _colorTex[i]?.Dispose();
                    _colorTex[i] = null;
                }

                _pipeline.Dispose();

                DisposeSwapChain();

                _context?.Dispose();
                _context = null;
                _device?.Dispose();
                _device = null;
                _factory?.Dispose();
                _factory = null;

                if (_hwnd != IntPtr.Zero)
                {
                    Interop.DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }
                if (_classAtom != 0)
                {
                    Interop.UnregisterClassW(new IntPtr(_classAtom), _hInstance);
                    _classAtom = 0;
                }

                App.Logger.WriteLine(LOG_IDENT, "Frame generation overlay stopped");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT + "::Cleanup", ex);
            }
        }

        public void Dispose()
        {
            if (!_running && _thread == null)
                return;
            _running = false;
            _stopped.Wait(5000);
            _thread = null;
        }

        private static class Interop
        {
            public const uint WS_POPUP = 0x80000000;
            public const int WS_EX_NOACTIVATE = 0x08000000;
            public const int WS_EX_TOOLWINDOW = 0x00000080;
            public const int WS_EX_TRANSPARENT = 0x00000020;
            public const int WS_EX_TOPMOST = 0x00000008;
            public const int WS_EX_LAYERED = 0x00080000;
            public const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
            public const uint LWA_ALPHA = 0x00000002;
            public const int SW_SHOWNOACTIVATE = 4;
            public const int SW_HIDE = 0;
            public const uint SWP_NOACTIVATE = 0x0010;
            public const uint SWP_NOZORDER = 0x0004;
            public const uint SWP_SHOWWINDOW = 0x0040;
            public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct WNDCLASSEXW
            {
                public uint cbSize;
                public uint style;
                public IntPtr lpfnWndProc;
                public int cbClsExtra;
                public int cbWndExtra;
                public IntPtr hInstance;
                public IntPtr hIcon;
                public IntPtr hCursor;
                public IntPtr hbrBackground;
                public IntPtr lpszMenuName;
                public IntPtr lpszClassName;
                public IntPtr hIconSm;
            }

            public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern bool UnregisterClassW(IntPtr lpClassName, IntPtr hInstance);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateWindowExW(int dwExStyle, IntPtr lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

            [DllImport("user32.dll")]
            public static extern bool DestroyWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

            [DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll")]
            public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWindow(IntPtr hWnd);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            public static extern IntPtr GetModuleHandleW(string? lpModuleName);
        }
    }
}
