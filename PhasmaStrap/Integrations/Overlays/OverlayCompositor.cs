using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using PhasmaStrap.Utility;
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
    internal sealed partial class OverlayCompositor
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

        private long _gameFrames, _gameFramesBase;
        private bool _countedGameFrames;

        private bool _overlayDirty = true;
        private double _lastOverlayPresentMs;
        private long _nextCountDuplicationMs;

        private ID3D11Texture2D? _sharedTex;
        private IDXGIKeyedMutex? _sharedMutex;
        private IntPtr _sharedHandle;
        private long _sharedVersion = -1;
        private bool _loggedShared;

        private ID3D11Texture2D? _rawTex;
        private ID3D11ShaderResourceView? _rawSrv;
        private ID3D11RenderTargetView? _rawRtv;
        private bool _rawValid;
        private int _rawWidth, _rawHeight;
        private Vector4 _dims;

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

                    SyncStreamWindow();

                    if (!UpdateVisibility(token))
                        continue;

                    SyncStreamView();
                    FollowRoblox();
                    ReloadSettingsIfChanged();
                    if (OverlaySettings.NeedsCapture)
                        RenderFrame(token);
                    else
                        RenderOverlayOnly(token);
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
            if (SharedGameFrame.RecorderActive || !OverlaySettings.NeedsCapture)
                return;

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
            _hud.Init(_device!, CountHudRows());
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
                    _overlayDirty = true;
                    App.Logger.WriteLine(LOG_IDENT, "Roblox is in the foreground again, the overlay is rendering");

                    if (!_overlayHiddenForStream)
                    {
                        Interop.ShowWindow(_hwnd, Interop.SW_SHOWNOACTIVATE);
                        AssertZOrder();
                    }
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
                PositionStreamView();
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
            _overlayDirty = true;

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
            PositionStreamView();
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

            if (SharedGameFrame.RecorderActive)
            {
                ReleaseOwnDuplication();
                return CaptureFromRecorder();
            }

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
                _gameFrames += frameInfo.AccumulatedFrames;
                _countedGameFrames = true;
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

        private void ReleaseOwnDuplication()
        {
            if (_duplication == null)
                return;
            _duplication.Dispose();
            _duplication = null;
            App.Logger.WriteLine(LOG_IDENT, "Instant Replay is capturing the screen - the overlay uses its frames instead of its own capture");
        }

        private bool CaptureFromRecorder()
        {
            if (!SharedGameFrame.TryGet(out IntPtr handle, out int width, out int height))
            {
                Thread.Sleep(4);
                return false;
            }

            try
            {
                if (handle != _sharedHandle || _sharedTex == null)
                {
                    _sharedMutex?.Dispose();
                    _sharedTex?.Dispose();
                    _sharedTex = _device!.OpenSharedResource<ID3D11Texture2D>(handle);
                    _sharedMutex = _sharedTex.QueryInterface<IDXGIKeyedMutex>();
                    _sharedHandle = handle;
                    _sharedVersion = -1;
                    if (!_loggedShared)
                    {
                        _loggedShared = true;
                        App.Logger.WriteLine(LOG_IDENT, $"Reading the game picture from Instant Replay ({width}x{height})");
                    }
                }

                long version = SharedGameFrame.Version;
                if (version == _sharedVersion)
                {
                    Thread.Sleep(1);
                    return false;
                }

                if (KeyedMutexLock.Acquire(_sharedMutex!, 0, 8) != KeyedMutexLock.Acquired)
                    return false;
                try
                {
                    int w = Math.Min(width, _width), h = Math.Min(height, _height);
                    _context!.CopySubresourceRegion(_rawTex!, 0, 0, 0, 0, _sharedTex, 0, new Box(0, 0, 0, w, h, 1));
                }
                finally
                {
                    _sharedMutex!.ReleaseSync(0);
                }

                _sharedVersion = version;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not read Instant Replay's picture: {ex.Message}");
                _sharedMutex?.Dispose();
                _sharedTex?.Dispose();
                _sharedMutex = null;
                _sharedTex = null;
                _sharedHandle = IntPtr.Zero;
                Thread.Sleep(50);
                return false;
            }
        }

        private void RenderOverlayOnly(CancellationToken token)
        {
            bool counted = CountGameFrames();

            double now = _clock.Elapsed.TotalMilliseconds;
            if (!_overlayDirty && now - _lastOverlayPresentMs < 1000)
            {
                if (!counted)
                    token.WaitHandle.WaitOne(8);
                return;
            }

            _context!.ClearRenderTargetView(_backBufferRtv!, new Color4(0, 0, 0, 0));
            DrawHud();
            DrawCrosshair();
            if (!Present())
                return;

            _overlayDirty = false;
            _lastOverlayPresentMs = now;
            _framesPresented++;
        }

        private bool CountGameFrames()
        {
            if (SharedGameFrame.RecorderActive)
            {
                ReleaseOwnDuplication();
                return false;
            }

            long nowMs = Environment.TickCount64;
            if (_duplication == null)
            {
                if (nowMs < _nextCountDuplicationMs)
                    return false;
                _nextCountDuplicationMs = nowMs + 2000;
                if (!CreateDuplicationForRect(_rectLeft, _rectTop))
                    return false;
            }

            IDXGIResource? resource = null;
            bool acquired = false;
            try
            {
                _duplication!.AcquireNextFrame(8, out OutduplFrameInfo info, out resource);
                acquired = true;
                _gameFrames += info.AccumulatedFrames;
                _countedGameFrames = true;
                return true;
            }
            catch (SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.WaitTimeout)
            {
                _countedGameFrames = true;
                return true;
            }
            catch (Exception)
            {
                _duplication?.Dispose();
                _duplication = null;
                return false;
            }
            finally
            {
                resource?.Dispose();
                if (acquired)
                {
                    try { _duplication?.ReleaseFrame(); } catch { }
                }
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
                token.WaitHandle.WaitOne(4);
                return;
            }

            if (!fresh && !frameGenOn)
            {
                token.WaitHandle.WaitOne(4);
                return;
            }

            if (!_firstCaptureLogged)
            {
                _firstCaptureLogged = true;
                App.Logger.WriteLine(LOG_IDENT, $"First frame captured at {_width}x{_height} via desktop duplication, compositor is live");
            }

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

            bool overlayShown = OverlaySettings.OverlayWindowNeeded;
            if (StreamLive)
                RenderStream(finalSrv, overlayShown ? 0 : 1);

            if (!overlayShown)
            {
                if (!StreamLive)
                    Thread.Sleep(16);
                _framesPresented++;
                return;
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

        private static int CountHudRows()
        {
            int rows = 1;
            if (App.Settings.Prop.OverlayHudShowFrameTime) rows++;
            if (App.Settings.Prop.OverlayHudShowCpu) rows++;
            if (App.Settings.Prop.OverlayHudShowRam) rows++;
            if (App.Settings.Prop.OverlayHudShowPing) rows++;
            if (App.Settings.Prop.OverlayHudShowRegion) rows++;
            if (App.Settings.Prop.OverlayHudShowGame) rows++;
            if (App.Settings.Prop.OverlayHudShowSessionTime) rows++;
            return rows;
        }

        private void UpdateHudIfDue()
        {
            bool enabled = OverlaySettings.HudEnabled;
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
            long gameFrames = _gameFrames - _gameFramesBase;
            bool counted = _countedGameFrames;
            _hudLastMs = now;
            _hudFramesBase = _framesPresented;
            _gameFramesBase = _gameFrames;
            _countedGameFrames = false;
            if (window <= 0.0)
                return;

            double fps = counted ? gameFrames / window : FpsFeed.Get(FpsFeed.Source.Recorder);
            if (counted)
                FpsFeed.Report(FpsFeed.Source.Hud, fps);

            try
            {
                var labels = new List<string> { "FPS" };
                var values = new List<string> { $"{fps:0}/s" };

                if (App.Settings.Prop.OverlayHudShowFrameTime)
                {
                    double frameMs = fps > 0 ? 1000.0 / fps : 0;
                    labels.Add("FRAME");
                    values.Add($"{frameMs:0.0}ms");
                }

                if (App.Settings.Prop.OverlayHudShowCpu)
                {
                    labels.Add("CPU");
                    values.Add($"{SystemStatsSampler.SampleCpuPercent():0}%");
                }

                if (App.Settings.Prop.OverlayHudShowRam)
                {
                    labels.Add("RAM");
                    values.Add($"{SystemStatsSampler.SampleRamPercent():0}%");
                }

                if (App.Settings.Prop.OverlayHudShowPing)
                {
                    int ping = ServerPingMonitor.LatestMs;
                    labels.Add("PING");
                    values.Add(ping >= 0 ? $"{ping}ms" : "--");
                }

                if (App.Settings.Prop.OverlayHudShowRegion)
                {
                    string region = ServerRegion.Current;
                    labels.Add("REGION");
                    values.Add(region.Length > 0 ? ServerRegion.Shorten(region, 18) : "--");
                }

                if (App.Settings.Prop.OverlayHudShowGame)
                {
                    labels.Add("GAME");
                    values.Add(NowPlaying.GameName());
                }

                if (App.Settings.Prop.OverlayHudShowSessionTime)
                {
                    labels.Add("TIME");
                    values.Add(NowPlaying.SessionLength());
                }

                _hud.Update(_context!, labels.ToArray(), values.ToArray());
                _hudPainted = true;
                _overlayDirty = true;
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
            (int hudX, int hudY) = HudStyle.Current.Place(_width, _height, _hud.TexWidth, _hud.TexHeight);
            _context.RSSetViewport(new Viewport(hudX, hudY, _hud.TexWidth, _hud.TexHeight, 0, 1));
            _context.PSSetShaderResources(0, _nullSrvs);
            _context.PSSetShaderResource(0, _hud.Srv);
            _context.Draw(3, 0);
            _context.OMSetBlendState(null);
            _context.PSSetShaderResources(0, _nullSrvs);
        }

        private void DrawCrosshair(ID3D11RenderTargetView? target = null)
        {
            target ??= _backBufferRtv;
            if (target == null || !OverlayCrosshair.IsEnabled())
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
                _context.OMSetRenderTargets(target);
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
                    _overlayDirty = true;
                    App.Logger.WriteLine(LOG_IDENT, "Settings changed on disk, reloaded so overlay toggles apply live");
                }
            }
            catch
            {
            }
        }

        private void Cleanup()
        {
            DestroyStreamView();

            _sharedMutex?.Dispose();
            _sharedTex?.Dispose();
            _sharedMutex = null;
            _sharedTex = null;

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
