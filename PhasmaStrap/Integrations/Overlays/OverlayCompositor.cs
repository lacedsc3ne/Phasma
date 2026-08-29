using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.DirectComposition;
using Vortice.Mathematics;
using SharpGen.Runtime;
using D3D11 = Vortice.Direct3D11.D3D11;
using DCompApi = Vortice.DirectComposition.DComp;
using Interop = PhasmaStrap.Integrations.Overlays.OverlayInterop;

namespace PhasmaStrap.Integrations.Overlays
{
    /// <summary>
    /// Composites PhasmaStrap's own overlay content (a small FPS HUD and an optional
    /// crosshair) on top of the live Roblox window, using a click-through, topmost,
    /// DirectComposition-backed window plus a D3D11 device/DXGI swapchain-for-composition.
    ///
    /// This is a heavily trimmed port of Voidstrap's OverlayCompositor.cs (~3.5k lines), which
    /// interleaved core compositor plumbing (window/device/swapchain/DirectComposition setup,
    /// capture, resize, present, cleanup) with three additional subsystems: RiShade (a
    /// post-processing shader pipeline), Anti Aliasing (a second shader pass), and Frame
    /// Generation (frame interpolation). Those three now run as stages inside RenderFrame below
    /// (RiShadeStage, AntiAliasingStage, FrameGenPipeline), lazily created and sized on first use
    /// and chained through two ping-pong buffers - none of them own a device, window, or capture
    /// of their own; this compositor is the only place any of that exists. Frame Generation's
    /// presentation pacing/quality-auto-tuning/split-screen-compare and its own status HUD were
    /// not ported (see FrameGenManager.cs's doc comments for the reasoning).
    ///
    /// Additional simplifications made for this port (documented in the porting agent's
    /// final report, not just here):
    ///  - Capture is desktop-duplication only. Voidstrap primarily used Windows.Graphics.Capture
    ///    (window capture) with desktop duplication as a fallback; WGC needs the CsWinRT
    ///    projection toolchain, which isn't wired into PhasmaStrap's net6.0-windows build and
    ///    would be a substantial new dependency surface just for this port. Desktop duplication
    ///    alone still delivers robust, resize/monitor-change-aware capture of whatever is on
    ///    screen under the Roblox window.
    ///  - No homepage-background compositing (HomepageBackgroundMedia.cs) - that drew a themed
    ///    background (solid/gradient/video) behind Roblox's own loading/menu screens. It isn't
    ///    part of the "overlay content the compositor renders on its own" (HUD/crosshair/
    ///    diagnostics/display) and pulls in image/video decode dependencies PhasmaStrap doesn't
    ///    have, so it was dropped rather than ported.
    ///  - No Roblox-FPS-cap-aware capture pacing (RobloxFpsCap.cs) or present-statistics-based
    ///    "actual FPS" tracking (RobloxPresentTracer.cs) - both existed almost entirely to feed
    ///    Frame Generation's capture cadence and quality decisions. Capture here is simply
    ///    paced by desktop duplication's own AcquireNextFrame wait, and the HUD's FPS figure is
    ///    the compositor's own local present rate.
    /// </summary>
    internal sealed class OverlayCompositor
    {
        private const string ClassName = "PhasmaStrapOverlayCompositor";
        private const string CaptureWindowName = "PhasmaStrap Overlay";
        private const string LOG_IDENT = "Overlays";

        private Interop.WndProcDelegate? _wndProc;
        private IntPtr _hwnd;
        private ushort _classAtom;
        private IntPtr _hInstance;

        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGIFactory2? _factory;
        private IDXGISwapChain1? _swapChain;
        private IDXGISwapChain2? _swapChain2;
        private ID3D11Texture2D? _backBufferTex;
        private ID3D11RenderTargetView? _backBufferRtv;
        private IDCompositionDevice? _dcompDevice;
        private IDCompositionTarget? _dcompTarget;
        private IDCompositionVisual? _dcompVisual;
        private IntPtr _frameLatencyHandle;
        private SwapChainFlags _swapChainFlags;

        private IDXGIOutputDuplication? _duplication;
        private int _outputLeft, _outputTop, _outputRight, _outputBottom;
        private int _captureFailures;
        private bool _deviceLost;
        private int _stableCaptureFrames;
        private long _captureUnstableSinceMs;
        private long _lastRecreateMs;

        private ID3D11VertexShader? _vs;
        private ID3D11PixelShader? _psPass;
        private ID3D11PixelShader? _psCropSrgb;
        private ID3D11PixelShader? _psOverlay;
        private ID3D11SamplerState? _sampler;
        private ID3D11Buffer? _cbuffer;
        private ID3D11BlendState? _hudBlend;

        private readonly OverlayHud _hud = new OverlayHud();
        private readonly OverlayCrosshair _crosshair = new OverlayCrosshair();
        private const double OverlayRefreshIntervalMs = 250.0;
        private double _crosshairRefreshMs;
        private bool _hudPainted;
        private double _hudLastMs;
        private long _hudFramesBase;
        private static readonly int HudX = 18;
        private static readonly int HudY = 18;

        private ID3D11Texture2D? _rawTex;
        private ID3D11ShaderResourceView? _rawSrv;
        private ID3D11RenderTargetView? _rawRtv;
        private bool _rawValid;
        private int _rawWidth, _rawHeight;
        private Vector4 _dims;

        // Ping-pong chain buffers RiShade/AntiAliasing/FrameGen render through in sequence -
        // see the "RiShade/FrameGen/AntiAliasing integration point" in RenderFrame below.
        private ID3D11Texture2D? _chainTexA;
        private ID3D11ShaderResourceView? _chainSrvA;
        private ID3D11RenderTargetView? _chainRtvA;
        private ID3D11Texture2D? _chainTexB;
        private ID3D11ShaderResourceView? _chainSrvB;
        private ID3D11RenderTargetView? _chainRtvB;

        private RiShade.RiShadeStage? _riShadeStage;
        private AntiAliasing.AntiAliasingStage? _antiAliasingStage;
        private FrameGeneration.FrameGenPipeline? _frameGenPipeline;
        private readonly ID3D11Texture2D?[] _fgColorTex = new ID3D11Texture2D?[2];
        private readonly ID3D11ShaderResourceView?[] _fgColorSrv = new ID3D11ShaderResourceView?[2];
        private int _fgCurSet;
        private int _fgCapturedFrames;
        private double _fgPrevCaptureMs, _fgCurrCaptureMs, _fgIntervalMs;

        private int _width;
        private int _height;
        private int _rectLeft;
        private int _rectTop;
        private int _pendingW;
        private int _pendingH;

        private IntPtr _robloxHwnd;
        private bool _hiddenByFocus;
        private double _refreshHz = 60.0;
        private IntPtr _displayMonitor;
        private long _nextVisibilityCheckMs;
        private long _nextFollowMs;
        private double _lastHwndResolve;
        private long _framesPresented;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _firstCaptureLogged;

        private IDisposable? _trackerLease;

        // RiShade (RiShadeStage), Anti-Aliasing (AntiAliasingStage), and Frame Generation
        // (FrameGenPipeline) each run as a stage here, lazily created and sized on first use -
        // see RenderFrame below for how they're chained between capture and the final blit.

        public void Run(CancellationToken token)
        {
            try
            {
                _trackerLease = RobloxWindowTracker.Acquire();
                ResolveRobloxHwnd();
                if (!TryGetRobloxRect(out int left, out int top, out int width, out int height))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Roblox window disappeared before compositor start");
                    return;
                }
                _rectLeft = left;
                _rectTop = top;
                _width = Math.Max(16, width);
                _height = Math.Max(16, height);
                App.Logger.WriteLine(LOG_IDENT, $"Starting compositor for Roblox at {_rectLeft},{_rectTop} size {_width}x{_height}");

                CreateWindow();
                CreateDevice();
                _refreshHz = QueryRefreshHz();
                CreateCapture();
                CreateComposition();
                CreatePipeline();

                App.Logger.WriteLine(LOG_IDENT, $"Compositor started, display {_refreshHz:0}Hz");
                OverlayHub.SetCompositorLive(true);

                var msg = default(Interop.MSG);
                while (!token.IsCancellationRequested)
                {
                    while (Interop.PeekMessageW(out msg, IntPtr.Zero, 0, 0, Interop.PM_REMOVE))
                    {
                        Interop.TranslateMessage(ref msg);
                        Interop.DispatchMessageW(ref msg);
                    }

                    if (_deviceLost)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Restarting the compositor session to recover");
                        break;
                    }

                    if (!OverlaySettings.AnyEnabled)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "All overlays turned off, closing the compositor");
                        break;
                    }

                    if (!UpdateVisibility(token))
                        continue;

                    FollowRoblox();
                    ReloadSettingsIfChanged();
                    RenderFrame(token);
                    UpdateHudIfDue();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("OverlayCompositor::Run", ex);
            }
            finally
            {
                Cleanup();
            }
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
                _hwnd = Interop.CreateWindowExW(exStyle, new IntPtr(_classAtom), CaptureWindowName, Interop.WS_POPUP, _rectLeft, _rectTop, _width, _height, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

                Interop.SetLayeredWindowAttributes(_hwnd, 0, 255, Interop.LWA_ALPHA);
                // Excluded from screen/desktop capture: since capture is desktop-duplication
                // based, without this our own composited window would feed back into itself.
                Interop.SetWindowDisplayAffinity(_hwnd, Interop.WDA_EXCLUDEFROMCAPTURE);
                Interop.SetWindowPos(_hwnd, Interop.HWND_TOPMOST, _rectLeft, _rectTop, _width, _height, Interop.SWP_NOACTIVATE | Interop.SWP_SHOWWINDOW);
                Interop.ShowWindow(_hwnd, Interop.SW_SHOWNOACTIVATE);
                OverlayDiagnostics.RaiseOverlayWindows();
                App.Logger.WriteLine(LOG_IDENT, "Compositor window created, click through");
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

            IDXGIAdapter1? chosen = null;
            try
            {
                int cx = _rectLeft + _width / 2;
                int cy = _rectTop + _height / 2;
                for (int i = 0; chosen == null; i++)
                {
                    var res = _factory.EnumAdapters1(i, out var adapter);
                    if (res.Failure || adapter == null)
                        break;
                    bool owns = false;
                    for (int j = 0; !owns; j++)
                    {
                        var ores = adapter.EnumOutputs(j, out var output);
                        if (ores.Failure || output == null)
                            break;
                        try
                        {
                            var dc = output.Description.DesktopCoordinates;
                            owns = cx >= dc.Left && cx < dc.Right && cy >= dc.Top && cy < dc.Bottom;
                        }
                        finally
                        {
                            output.Dispose();
                        }
                    }
                    if (owns)
                        chosen = adapter;
                    else
                        adapter.Dispose();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Adapter probe failed, using the default adapter: " + ex.Message);
            }

            try
            {
                if (chosen != null)
                {
                    D3D11.D3D11CreateDevice(chosen, DriverType.Unknown, DeviceCreationFlags.BgraSupport, _featureLevels, out _device, out _context).CheckError();
                    App.Logger.WriteLine(LOG_IDENT, $"D3D11 device created on {chosen.Description1.Description}, feature level {_device!.FeatureLevel}");
                    return;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Device creation on the display adapter failed: " + ex.Message);
                _context?.Dispose();
                _device?.Dispose();
                _context = null;
                _device = null;
            }
            finally
            {
                chosen?.Dispose();
            }

            try
            {
                D3D11.D3D11CreateDevice((IDXGIAdapter)null!, DriverType.Hardware, DeviceCreationFlags.BgraSupport, _featureLevels, out _device, out _context).CheckError();
                App.Logger.WriteLine(LOG_IDENT, $"D3D11 device created on the default adapter, feature level {_device!.FeatureLevel}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Hardware device unavailable, using the software rasterizer: " + ex.Message);
                _context?.Dispose();
                _device?.Dispose();
                _context = null;
                _device = null;
                D3D11.D3D11CreateDevice((IDXGIAdapter)null!, DriverType.Warp, DeviceCreationFlags.BgraSupport, _featureLevels, out _device, out _context).CheckError();
                App.Logger.WriteLine(LOG_IDENT, $"WARP device created, feature level {_device!.FeatureLevel}");
            }
        }

        private void CreateCapture()
        {
            if (!CreateDuplicationForRect(_rectLeft, _rectTop))
                App.Logger.WriteLine(LOG_IDENT, "Could not create desktop duplication, will retry while running");
        }

        private void CreateComposition()
        {
            _swapChainFlags = SwapChainFlags.FrameLatencyWaitableObject;
            var swapDesc = new SwapChainDescription1
            {
                Width = _width,
                Height = _height,
                Format = Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 3,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
                Flags = _swapChainFlags,
            };

            try
            {
                _swapChain = _factory!.CreateSwapChainForComposition(_device!, swapDesc, null);
            }
            catch (Exception ex)
            {
                _swapChainFlags = SwapChainFlags.None;
                swapDesc.Flags = _swapChainFlags;
                _swapChain = _factory!.CreateSwapChainForComposition(_device!, swapDesc, null);
                App.Logger.WriteLine(LOG_IDENT, "Frame latency waitable swapchain unavailable, using the standard composition queue: " + ex.Message);
            }
            CreateBackBufferRtv();
            if ((_swapChainFlags & SwapChainFlags.FrameLatencyWaitableObject) != 0)
            {
                try
                {
                    _swapChain2 = _swapChain.QueryInterfaceOrNull<IDXGISwapChain2>();
                    if (_swapChain2 != null)
                    {
                        _swapChain2.MaximumFrameLatency = 1;
                        _frameLatencyHandle = _swapChain2.FrameLatencyWaitableObject;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Frame latency synchronization unavailable, continuing with queue depth one: " + ex.Message);
                }
            }
            if (_frameLatencyHandle == IntPtr.Zero)
                RaiseFrameLatencyLimit(1);

            using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
            DCompApi.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice, out _dcompDevice).CheckError();
            _dcompDevice!.CreateTargetForHwnd(_hwnd, true, out _dcompTarget);
            _dcompVisual = _dcompDevice.CreateVisual();
            _dcompVisual.SetContent(_swapChain);
            _dcompTarget!.SetRoot(_dcompVisual);
            _dcompDevice.Commit();
            App.Logger.WriteLine(LOG_IDENT, "DirectComposition swapchain attached to the compositor window");
        }

        private void RaiseFrameLatencyLimit(int frames)
        {
            try
            {
                using var dxgiDevice1 = _device!.QueryInterface<IDXGIDevice1>();
                dxgiDevice1.MaximumFrameLatency = Math.Max(1, frames);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not set the frame latency limit: " + ex.Message);
            }
        }

        private void CreateBackBufferRtv()
        {
            _backBufferTex?.Dispose();
            _backBufferTex = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
            _backBufferRtv = _device!.CreateRenderTargetView(_backBufferTex);
        }

        private ID3D11PixelShader CompilePs(string entry)
        {
            Vortice.D3DCompiler.Compiler.Compile(OverlayShaders.Source, entry, "Overlays", "ps_5_0", out var blob, out var err);
            using (err)
            {
                if (blob == null)
                {
                    string msg = err != null ? err.ConvertToString() : "unknown";
                    throw new InvalidOperationException("Compositor shader compile failed for " + entry + ": " + msg);
                }
            }
            using (blob)
            {
                return _device!.CreatePixelShader(blob.GetBytes());
            }
        }

        private void CreatePipeline()
        {
            Vortice.D3DCompiler.Compiler.Compile(OverlayShaders.Source, "VSMain", "Overlays", "vs_5_0", out var vsBlob, out var vsErr);
            using (vsErr)
            {
                if (vsBlob == null)
                    throw new InvalidOperationException("Compositor vertex shader compile failed");
            }
            using (vsBlob)
            {
                _vs = _device!.CreateVertexShader(vsBlob.GetBytes());
            }
            _psPass = CompilePs("PSPass");
            _psCropSrgb = CompilePs("PSCropSrgb");
            _psOverlay = CompilePs("PSOverlay");

            _hudBlend = _device!.CreateBlendState(new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha));
            _hud.Init(_device!);
            _crosshair.Init(_device!);

            _sampler = _device!.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunction = ComparisonFunction.Never,
                MinLOD = 0,
                MaxLOD = float.MaxValue,
            });

            _cbuffer = _device!.CreateBuffer(new BufferDescription
            {
                SizeInBytes = Marshal.SizeOf<OverlayParams>(),
                BindFlags = BindFlags.ConstantBuffer,
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None,
            });

            CreateSizedResources();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OverlayParams
        {
            public Vector4 Dims;
            public Vector4 SrcRect;
        }

        private void CreateSizedResources()
        {
            ReleaseSizedResources();
            _rawValid = false;
            _rawTex = CreateTex();
            _rawSrv = _device!.CreateShaderResourceView(_rawTex);
            _rawRtv = _device!.CreateRenderTargetView(_rawTex);
            _rawWidth = _width;
            _rawHeight = _height;
            _dims = new Vector4(_width, _height, 1f / Math.Max(_width, 1), 1f / Math.Max(_height, 1));

            _chainTexA = CreateTex();
            _chainSrvA = _device!.CreateShaderResourceView(_chainTexA);
            _chainRtvA = _device!.CreateRenderTargetView(_chainTexA);
            _chainTexB = CreateTex();
            _chainSrvB = _device!.CreateShaderResourceView(_chainTexB);
            _chainRtvB = _device!.CreateRenderTargetView(_chainTexB);

            for (int i = 0; i < 2; i++)
            {
                _fgColorSrv[i]?.Dispose();
                _fgColorTex[i]?.Dispose();
                _fgColorTex[i] = CreateTex();
                _fgColorSrv[i] = _device!.CreateShaderResourceView(_fgColorTex[i]);
            }
            _fgCapturedFrames = 0;

            _riShadeStage?.EnsureSize(_width, _height);
            _antiAliasingStage?.EnsureSize(_width, _height);
            _frameGenPipeline?.EnsureSize(_width, _height);
        }

        private ID3D11Texture2D CreateTex()
        {
            return _device!.CreateTexture2D(new Texture2DDescription
            {
                Width = _width,
                Height = _height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                CpuAccessFlags = CpuAccessFlags.None,
            });
        }

        private void ReleaseSizedResources()
        {
            _rawRtv?.Dispose();
            _rawSrv?.Dispose();
            _rawTex?.Dispose();
            _rawRtv = null;
            _rawSrv = null;
            _rawTex = null;

            _chainRtvA?.Dispose();
            _chainSrvA?.Dispose();
            _chainTexA?.Dispose();
            _chainRtvA = null;
            _chainSrvA = null;
            _chainTexA = null;
            _chainRtvB?.Dispose();
            _chainSrvB?.Dispose();
            _chainTexB?.Dispose();
            _chainRtvB = null;
            _chainSrvB = null;
            _chainTexB = null;

            for (int i = 0; i < 2; i++)
            {
                _fgColorSrv[i]?.Dispose();
                _fgColorTex[i]?.Dispose();
                _fgColorSrv[i] = null;
                _fgColorTex[i] = null;
            }
        }

        private bool CreateDuplicationForRect(int left, int top)
        {
            try
            {
                _duplication?.Dispose();
                _duplication = null;
                using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
                dxgiDevice.GetAdapter(out var adapter).CheckError();
                try
                {
                    int cx = left + _width / 2;
                    int cy = top + _height / 2;
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
                                App.Logger.WriteLine(LOG_IDENT, $"Screen capture active on monitor at {dc.Left},{dc.Top} to {dc.Right},{dc.Bottom}");
                                return true;
                            }
                        }
                        finally
                        {
                            output.Dispose();
                        }
                    }
                    return false;
                }
                finally
                {
                    adapter.Dispose();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("OverlayCompositor::CreateDuplication", ex);
                return false;
            }
        }

        private void ResolveRobloxHwnd()
        {
            IntPtr handle = RobloxWindowTracker.Current.Hwnd;
            if (handle != IntPtr.Zero)
                _robloxHwnd = handle;
        }

        private bool TryGetRobloxRect(out int left, out int top, out int width, out int height)
        {
            RobloxWindowRect rect = RobloxWindowTracker.Current;
            if (rect.Valid && rect.Hwnd != IntPtr.Zero)
            {
                left = rect.Left;
                top = rect.Top;
                width = rect.Width;
                height = rect.Height;
                _robloxHwnd = rect.Hwnd;
                return true;
            }
            left = top = width = height = 0;
            return false;
        }

        private bool UpdateVisibility(CancellationToken token)
        {
            long tick = Environment.TickCount64;
            if (tick < _nextVisibilityCheckMs)
                return !_hiddenByFocus;
            _nextVisibilityCheckMs = tick + 250;

            RobloxWindowRect rect = RobloxWindowTracker.Current;
            IntPtr fg = Interop.GetForegroundWindow();
            bool robloxActive = rect.Valid && (rect.Foreground || fg == rect.Hwnd || OverlayDiagnostics.IsOverlayHandle(fg));
            if (robloxActive)
            {
                if (_hiddenByFocus)
                {
                    _hiddenByFocus = false;
                    App.Logger.WriteLine(LOG_IDENT, "Roblox is in the foreground again, the overlay is rendering");
                    Interop.ShowWindow(_hwnd, Interop.SW_SHOWNOACTIVATE);
                    AssertZOrder();
                }
                return true;
            }
            if (!_hiddenByFocus)
            {
                _hiddenByFocus = true;
                App.Logger.WriteLine(LOG_IDENT, $"Idle, Roblox is not the foreground window, nothing renders until it comes back");
                Interop.ShowWindow(_hwnd, Interop.SW_HIDE);
            }
            double now = _clock.Elapsed.TotalSeconds;
            if (now - _lastHwndResolve > 5.0)
            {
                _lastHwndResolve = now;
                ResolveRobloxHwnd();
            }
            token.WaitHandle.WaitOne(250);
            return false;
        }

        private void FollowRoblox()
        {
            long tick = Environment.TickCount64;
            if (tick < _nextFollowMs)
                return;
            _nextFollowMs = tick + 250;
            if (!TryGetRobloxRect(out int left, out int top, out int width, out int height))
                return;
            if (left <= -30000 || top <= -30000)
                return;
            int w = Math.Max(16, width);
            int h = Math.Max(16, height);
            if (left == _rectLeft && top == _rectTop && w == _width && h == _height)
            {
                _pendingW = 0;
                _pendingH = 0;
                return;
            }

            bool sizeChanged = w != _width || h != _height;
            IntPtr currentMonitor = Interop.MonitorFromWindow(_robloxHwnd, Interop.MONITOR_DEFAULTTONEAREST);
            bool monitorChanged = currentMonitor != IntPtr.Zero && currentMonitor != _displayMonitor;
            if (sizeChanged && (w != _pendingW || h != _pendingH))
            {
                _pendingW = w;
                _pendingH = h;
                _rectLeft = left;
                _rectTop = top;
                Interop.SetWindowPos(_hwnd, IntPtr.Zero, _rectLeft, _rectTop, w, h, Interop.SWP_NOACTIVATE | Interop.SWP_NOZORDER);
                return;
            }

            _rectLeft = left;
            _rectTop = top;
            _width = w;
            _height = h;
            _pendingW = 0;
            _pendingH = 0;

            if (sizeChanged)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Roblox window resized, rebuilding targets at {_width}x{_height}");
                _backBufferRtv?.Dispose();
                _backBufferRtv = null;
                _backBufferTex?.Dispose();
                _backBufferTex = null;
                _swapChain!.ResizeBuffers(3, _width, _height, Format.B8G8R8A8_UNorm, _swapChainFlags);
                CreateBackBufferRtv();
                CreateSizedResources();
                _refreshHz = QueryRefreshHz();
                CreateDuplicationForRect(_rectLeft, _rectTop);
            }
            else if (monitorChanged)
            {
                App.Logger.WriteLine(LOG_IDENT, "Roblox moved to another monitor, refreshing capture");
                _refreshHz = QueryRefreshHz();
                CreateDuplicationForRect(_rectLeft, _rectTop);
            }
            else
            {
                int cx = _rectLeft + _width / 2;
                int cy = _rectTop + _height / 2;
                bool sameOutput = cx >= _outputLeft && cx < _outputRight && cy >= _outputTop && cy < _outputBottom;
                if (!sameOutput)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Roblox moved to another monitor, reacquiring capture");
                    _refreshHz = QueryRefreshHz();
                    CreateDuplicationForRect(_rectLeft, _rectTop);
                }
            }
            AssertZOrder();
        }

        private void AssertZOrder()
        {
            Interop.SetWindowPos(_hwnd, IntPtr.Zero, _rectLeft, _rectTop, _width, _height, Interop.SWP_NOACTIVATE | Interop.SWP_NOZORDER);
        }

        private double QueryRefreshHz()
        {
            try
            {
                IntPtr target = _robloxHwnd != IntPtr.Zero ? _robloxHwnd : _hwnd;
                IntPtr mon = Interop.MonitorFromWindow(target, Interop.MONITOR_DEFAULTTONEAREST);
                if (mon == IntPtr.Zero)
                    return 60.0;
                _displayMonitor = mon;
                var mi = new Interop.MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<Interop.MONITORINFOEXW>() };
                if (!Interop.GetMonitorInfoW(mon, ref mi))
                    return 60.0;
                var dm = new Interop.DEVMODEW { dmSize = (ushort)Marshal.SizeOf<Interop.DEVMODEW>() };
                if (Interop.EnumDisplaySettingsW(mi.szDevice, Interop.ENUM_CURRENT_SETTINGS, ref dm) && dm.dmDisplayFrequency > 1)
                    return dm.dmDisplayFrequency;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not read the display refresh rate, assuming 60Hz: " + ex.Message);
            }
            return 60.0;
        }

        private bool HandleCaptureUnstable(string reason)
        {
            _stableCaptureFrames = 0;
            long now = Environment.TickCount64;
            if (_captureUnstableSinceMs == 0)
                _captureUnstableSinceMs = now;
            _captureFailures++;
            if (_captureFailures == 1 || _captureFailures % 30 == 0)
                App.Logger.WriteLine(LOG_IDENT, $"{reason}, reacquiring the monitor");
            if (now - _captureUnstableSinceMs > 20000)
            {
                App.Logger.WriteLine(LOG_IDENT, "Screen capture stayed unstable, ending this compositor session");
                _deviceLost = true;
                return false;
            }
            if (now - _lastRecreateMs >= 500)
            {
                _lastRecreateMs = now;
                CreateDuplicationForRect(_rectLeft, _rectTop);
            }
            Thread.Sleep(_captureFailures < 4 ? 1 : 8);
            return false;
        }

        private static readonly ID3D11ShaderResourceView[] _nullSrvs = new ID3D11ShaderResourceView[1];

        private bool CaptureFrame()
        {
            if (_rawTex == null)
                return false;

            if (_duplication == null)
                return HandleCaptureUnstable("Screen capture not available");

            IDXGIResource? desktopResource = null;
            bool acquired = false;
            try
            {
                OutduplFrameInfo frameInfo;
                try
                {
                    _duplication.AcquireNextFrame(16, out frameInfo, out desktopResource);
                }
                catch (SharpGenException sgEx) when (sgEx.ResultCode == Vortice.DXGI.ResultCode.WaitTimeout)
                {
                    return false;
                }
                catch (SharpGenException sgEx) when (sgEx.ResultCode == Vortice.DXGI.ResultCode.AccessLost)
                {
                    return HandleCaptureUnstable("Capture access lost");
                }
                if (desktopResource == null)
                    return HandleCaptureUnstable("Capture access lost");
                acquired = true;
                _stableCaptureFrames++;
                if (_stableCaptureFrames >= 15)
                {
                    _captureUnstableSinceMs = 0;
                    _captureFailures = 0;
                }
                if (frameInfo.LastPresentTime == 0 && _rawValid)
                    return false;

                using var desktopTex = desktopResource.QueryInterface<ID3D11Texture2D>();
                int srcLeft = _rectLeft - _outputLeft;
                int srcTop = _rectTop - _outputTop;
                var desc = desktopTex.Description;
                int right = Math.Min(srcLeft + _width, (int)desc.Width);
                int bottom = Math.Min(srcTop + _height, (int)desc.Height);
                srcLeft = Math.Max(0, srcLeft);
                srcTop = Math.Max(0, srcTop);
                if (right <= srcLeft || bottom <= srcTop)
                    return false;

                if (desc.Format == Format.B8G8R8A8_UNorm)
                {
                    var box = new Box(srcLeft, srcTop, 0, right, bottom, 1);
                    _context!.CopySubresourceRegion(_rawTex, 0, 0, 0, 0, desktopTex, 0, box);
                }
                else
                {
                    using var desktopSrv = _device!.CreateShaderResourceView(desktopTex);
                    var cbufferData = new OverlayParams
                    {
                        Dims = _dims,
                        SrcRect = new Vector4(
                            (float)srcLeft / desc.Width,
                            (float)srcTop / desc.Height,
                            (float)(right - srcLeft) / desc.Width,
                            (float)(bottom - srcTop) / desc.Height),
                    };
                    _context!.UpdateSubresource(ref cbufferData, _cbuffer!, 0, 0, 0, null);
                    DrawBlit(_psCropSrgb!, _rawRtv!, desktopSrv);
                }
                return true;
            }
            finally
            {
                desktopResource?.Dispose();
                if (acquired)
                    _duplication.ReleaseFrame();
            }
        }

        private void DrawBlit(ID3D11PixelShader ps, ID3D11RenderTargetView target, ID3D11ShaderResourceView input)
        {
            _context!.VSSetShader(_vs!);
            _context.PSSetConstantBuffer(0, _cbuffer);
            _context.PSSetSampler(0, _sampler);
            _context.IASetInputLayout(null);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.RSSetViewport(new Viewport(0, 0, _width, _height, 0, 1));
            _context.PSSetShaderResources(0, _nullSrvs);
            _context.OMSetRenderTargets(target);
            _context.PSSetShader(ps);
            _context.PSSetShaderResource(0, input);
            _context.Draw(3, 0);
            _context.PSSetShaderResources(0, _nullSrvs);
        }

        private void RenderFrame(CancellationToken token)
        {
            bool fresh = CaptureFrame();
            if (fresh)
                _rawValid = true;

            bool frameGenOn = FrameGeneration.FrameGenSettings.ModeIndex > 0;

            if (!_rawValid)
            {
                // nothing captured yet at all - nothing to show
                token.WaitHandle.WaitOne(4);
                return;
            }

            if (!fresh && !frameGenOn)
            {
                // nothing changed since last frame and we're not interpolating between
                // captures, so there's no point redrawing/presenting again
                token.WaitHandle.WaitOne(4);
                return;
            }

            if (!_firstCaptureLogged)
            {
                _firstCaptureLogged = true;
                App.Logger.WriteLine(LOG_IDENT, $"First frame captured at {_width}x{_height} via desktop duplication, compositor is live");
            }

            // RiShade (RiShadeStage), Anti-Aliasing (AntiAliasingStage), and Frame Generation
            // (FrameGenPipeline) each run as a stage here, lazily created and sized on first
            // use, chained through the two ping-pong _chain buffers: whichever stage runs last
            // hands its output SRV to the final pass-through blit onto the back buffer.
            ID3D11ShaderResourceView finalSrv = _rawSrv!;
            bool nextIsA = true;

            if (frameGenOn)
            {
                var fgOut = RenderFrameGenStage(fresh);
                if (fgOut != null)
                {
                    finalSrv = fgOut;
                    nextIsA = false;
                }
            }

            if (App.Settings.Prop.RiShadeEnabled)
            {
                finalSrv = RenderRiShadeStage(finalSrv, nextIsA);
                nextIsA = !nextIsA;
            }

            if (App.Settings.Prop.AntiAliasingEnabled && AntiAliasing.AntiAliasingSettings.MethodIndex > 0)
            {
                finalSrv = RenderAntiAliasingStage(finalSrv, nextIsA);
                nextIsA = !nextIsA;
            }

            DrawBlit(_psPass!, _backBufferRtv!, finalSrv);

            DrawHud();
            DrawCrosshair();

            if (!Present())
                return;
            _framesPresented++;
        }

        private ID3D11ShaderResourceView? RenderFrameGenStage(bool fresh)
        {
            if (_frameGenPipeline == null)
            {
                _frameGenPipeline = new FrameGeneration.FrameGenPipeline();
                _frameGenPipeline.Attach(_device!, _context!);
                _frameGenPipeline.EnsureSize(_width, _height);
                _fgCapturedFrames = 0;
            }
            _frameGenPipeline.SetQuality(FrameGeneration.FrameGenSettings.QualityIndex);
            if (_frameGenPipeline.EnsureSize(_width, _height))
                _fgCapturedFrames = 0;

            if (fresh)
            {
                int nextSet = _fgCurSet ^ 1;
                _context!.CopyResource(_fgColorTex[nextSet]!, _rawTex!);
                _frameGenPipeline.BuildPyramid(nextSet, _fgColorSrv[nextSet]!);

                double now = _clock.Elapsed.TotalMilliseconds;
                if (_fgCapturedFrames > 0)
                {
                    _frameGenPipeline.ComputeFlow(_fgCurSet, nextSet, 12f);
                    _fgIntervalMs = Math.Max(1.0, now - _fgCurrCaptureMs);
                }
                _fgPrevCaptureMs = _fgCurrCaptureMs;
                _fgCurrCaptureMs = now;
                _fgCurSet = nextSet;
                _fgCapturedFrames++;
            }

            if (_chainRtvA == null || _chainSrvA == null)
                return null;

            if (_fgCapturedFrames >= 2 && _fgIntervalMs > 0.5)
            {
                double now = _clock.Elapsed.TotalMilliseconds;
                float t = (float)Math.Clamp((now - _fgCurrCaptureMs) / _fgIntervalMs, 0.0, 1.0);
                int prevSet = _fgCurSet ^ 1;
                _frameGenPipeline.Warp(_fgColorSrv[prevSet]!, _fgColorSrv[_fgCurSet]!, t, _chainRtvA);
            }
            else if (_fgCapturedFrames >= 1)
            {
                _frameGenPipeline.Blit(_fgColorSrv[_fgCurSet]!, _chainRtvA);
            }
            else
            {
                return null;
            }

            return _chainSrvA;
        }

        private ID3D11ShaderResourceView RenderRiShadeStage(ID3D11ShaderResourceView input, bool writeToA)
        {
            if (_riShadeStage == null)
            {
                _riShadeStage = new RiShade.RiShadeStage();
                _riShadeStage.CreatePipeline(_device!, _context!, _width, _height);
            }
            _riShadeStage.EnsureSize(_width, _height);

            var targetRtv = writeToA ? _chainRtvA! : _chainRtvB!;
            var targetSrv = writeToA ? _chainSrvA! : _chainSrvB!;
            _riShadeStage.Render(input, targetRtv);
            return targetSrv;
        }

        private ID3D11ShaderResourceView RenderAntiAliasingStage(ID3D11ShaderResourceView input, bool writeToA)
        {
            if (_antiAliasingStage == null)
            {
                _antiAliasingStage = new AntiAliasing.AntiAliasingStage();
                _antiAliasingStage.CreatePipeline(_device!, _context!, _width, _height);
            }
            _antiAliasingStage.EnsureSize(_width, _height);

            var targetRtv = writeToA ? _chainRtvA! : _chainRtvB!;
            var targetSrv = writeToA ? _chainSrvA! : _chainSrvB!;
            _antiAliasingStage.Render(AntiAliasing.AntiAliasingSettings.MethodIndex, input, targetRtv);
            return targetSrv;
        }

        private void UpdateHudIfDue()
        {
            bool enabled = App.Settings.Prop.OverlayHudEnabled;
            if (!enabled)
            {
                _hudPainted = false;
                return;
            }
            double now = _clock.Elapsed.TotalMilliseconds;
            if (_hudLastMs == 0)
            {
                _hudLastMs = now;
                _hudFramesBase = _framesPresented;
                return;
            }
            if (now - _hudLastMs < 1000.0)
                return;
            double window = (now - _hudLastMs) / 1000.0;
            long frames = _framesPresented - _hudFramesBase;
            _hudLastMs = now;
            _hudFramesBase = _framesPresented;
            if (window <= 0.0)
                return;
            double fps = frames / window;
            try
            {
                _hud.Update(_context!, new[] { "FPS" }, new[] { $"{fps:0}/s" });
                _hudPainted = true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("OverlayCompositor::UpdateHud", ex);
            }
        }

        private void DrawHud()
        {
            if (!_hudPainted || _hud.Srv == null || _backBufferRtv == null)
                return;
            _context!.OMSetBlendState(_hudBlend);
            _context.OMSetRenderTargets(_backBufferRtv);
            _context.VSSetShader(_vs!);
            _context.PSSetShader(_psOverlay!);
            _context.PSSetSampler(0, _sampler);
            _context.IASetInputLayout(null);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.RSSetViewport(new Viewport(HudX, HudY, OverlayHud.TexWidth, OverlayHud.TexHeight, 0, 1));
            _context.PSSetShaderResources(0, _nullSrvs);
            _context.PSSetShaderResource(0, _hud.Srv);
            _context.Draw(3, 0);
            _context.OMSetBlendState(null);
            _context.PSSetShaderResources(0, _nullSrvs);
        }

        private void DrawCrosshair()
        {
            if (_backBufferRtv == null || !OverlayCrosshair.IsEnabled())
                return;
            try
            {
                double nowMs = _clock.Elapsed.TotalMilliseconds;
                if (_crosshair.Srv == null || nowMs - _crosshairRefreshMs >= OverlayRefreshIntervalMs)
                {
                    _crosshairRefreshMs = nowMs;
                    _crosshair.Update(_context!);
                }
                if (_crosshair.Srv == null)
                    return;
                float x = (_width - OverlayCrosshair.TexWidth) * 0.5f;
                float y = (_height - OverlayCrosshair.TexHeight) * 0.5f;
                _context!.OMSetBlendState(_hudBlend);
                _context.OMSetRenderTargets(_backBufferRtv);
                _context.VSSetShader(_vs!);
                _context.PSSetShader(_psOverlay!);
                _context.PSSetSampler(0, _sampler);
                _context.IASetInputLayout(null);
                _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                _context.RSSetViewport(new Viewport(x, y, OverlayCrosshair.TexWidth, OverlayCrosshair.TexHeight, 0, 1));
                _context.PSSetShaderResources(0, _nullSrvs);
                _context.PSSetShaderResource(0, _crosshair.Srv);
                _context.Draw(3, 0);
                _context.OMSetBlendState(null);
                _context.PSSetShaderResources(0, _nullSrvs);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("OverlayCompositor::DrawCrosshair", ex);
            }
        }

        private bool Present()
        {
            var presentResult = _swapChain!.Present(1, PresentFlags.None);
            if (presentResult == Vortice.DXGI.ResultCode.DeviceRemoved || presentResult == Vortice.DXGI.ResultCode.DeviceReset)
            {
                App.Logger.WriteLine(LOG_IDENT, "Graphics device was lost, the session will restart");
                _deviceLost = true;
                return false;
            }
            return true;
        }

        private double _lastSettingsCheckSec;
        private DateTime _settingsFileTimeUtc;

        private void ReloadSettingsIfChanged()
        {
            double nowSec = _clock.Elapsed.TotalSeconds;
            if (nowSec - _lastSettingsCheckSec < 2.0)
                return;
            _lastSettingsCheckSec = nowSec;
            try
            {
                string path = App.Settings.FileLocation;
                if (!System.IO.File.Exists(path))
                    return;
                DateTime stamp = System.IO.File.GetLastWriteTimeUtc(path);
                if (_settingsFileTimeUtc == default)
                {
                    _settingsFileTimeUtc = stamp;
                    return;
                }
                if (stamp != _settingsFileTimeUtc)
                {
                    _settingsFileTimeUtc = stamp;
                    App.Settings.Load();
                    App.Logger.WriteLine(LOG_IDENT, "Settings changed on disk, reloaded so overlay toggles apply live");
                }
            }
            catch
            {
            }
        }

        private void Cleanup()
        {
            try
            {
                _duplication?.Dispose();
                ReleaseSizedResources();
                _riShadeStage?.Dispose();
                _riShadeStage = null;
                _antiAliasingStage?.Dispose();
                _antiAliasingStage = null;
                _frameGenPipeline?.Dispose();
                _frameGenPipeline = null;
                _hud.Dispose();
                _crosshair.Dispose();
                _hudBlend?.Dispose();
                _backBufferTex?.Dispose();
                _cbuffer?.Dispose();
                _sampler?.Dispose();
                _psPass?.Dispose();
                _psCropSrgb?.Dispose();
                _psOverlay?.Dispose();
                _vs?.Dispose();
                _dcompVisual?.Dispose();
                _dcompTarget?.Dispose();
                _dcompDevice?.Dispose();
                _backBufferRtv?.Dispose();
                if (_frameLatencyHandle != IntPtr.Zero)
                {
                    CloseHandle(_frameLatencyHandle);
                    _frameLatencyHandle = IntPtr.Zero;
                }
                _swapChain2?.Dispose();
                _swapChain?.Dispose();
                _factory?.Dispose();
                _context?.Dispose();
                _device?.Dispose();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("OverlayCompositor::Cleanup", ex);
            }
            try
            {
                if (_hwnd != IntPtr.Zero)
                    Interop.DestroyWindow(_hwnd);
                if (_classAtom != 0)
                    Interop.UnregisterClassW(new IntPtr(_classAtom), _hInstance);
            }
            catch
            {
            }
            _hwnd = IntPtr.Zero;
            _classAtom = 0;
            _trackerLease?.Dispose();
            _trackerLease = null;
            OverlayHub.SetCompositorLive(false);
            App.Logger.WriteLine(LOG_IDENT, "Compositor session cleaned up");
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
