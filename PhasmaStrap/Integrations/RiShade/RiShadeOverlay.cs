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
using D3D11 = Vortice.Direct3D11.D3D11;
using DCompApi = Vortice.DirectComposition.DComp;

namespace PhasmaStrap.Integrations.RiShade
{
    // Cbuffer layout must exactly match "Params : register(b0)" in RiShadeShaders.cs.
    [StructLayout(LayoutKind.Sequential)]
    internal struct RiShadeParams
    {
        public Vector4 PA;
        public Vector4 PB;
        public Vector4 PC;
        public Vector4 PD;
        public Vector4 PE;
        public Vector4 PF;
        public Vector4 PG;
        public Vector4 PH;
        public Vector4 PI;
        public Vector4 PJ;
        public Vector4 PK;
        public Vector4 PL;
        public Vector4 PM;
        public Vector4 PN;
        public Vector4 PO;
        public Vector4 PP;
        public Vector4 PQ;
        public Vector4 PR;
        public Vector4 PS;
        public Vector4 PT;
    }

    /// <summary>
    /// Self-contained D3D11/DXGI/DirectComposition shader post-processing overlay, ported from
    /// Voidstrap's RiShadeOverlay.cs. It owns its own device, its own topmost click-through window,
    /// and its own DirectComposition swapchain - it doesn't depend on any other overlay compositor,
    /// matching how Voidstrap built it upstream.
    /// <para/>
    /// What's NOT ported (see RiShadeSettings.cs for the reasoning): Windows.Graphics.Capture
    /// per-window capture (needs the CsWinRT projection toolchain, which isn't wired into this
    /// project - falls back to desktop-duplication capture, same as this fork's other overlay
    /// work), the AI depth estimation pipeline and every effect that depended on it (DOF, SSR, AO,
    /// GI, fog, eye adaptation, debug depth/normal views), the in-game F8 tweak panel, and
    /// ReShade .fx preset importing. Raw HLSL custom effects (Effects/*.hlsl) are still supported.
    /// </summary>
    internal sealed class RiShadeOverlay
    {
        private const string ClassName = "PhasmaStrapRiShadeOverlay";
        private const string LOG_IDENT = "RiShade";

        private RiShadeInterop.WndProcDelegate? _wndProc;
        private IntPtr _hwnd;
        private ushort _classAtom;
        private IntPtr _hInstance;

        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGIFactory2? _factory;
        private IDXGISwapChain1? _swapChain;
        private ID3D11RenderTargetView? _backBufferRtv;
        private IDCompositionDevice? _dcompDevice;
        private IDCompositionTarget? _dcompTarget;
        private IDCompositionVisual? _dcompVisual;

        private IDXGIOutputDuplication? _duplication;
        private bool _hasFirstCapture;
        private int _outputLeft;
        private int _outputTop;
        private int _outputRight;
        private int _outputBottom;
        private int _captureFailures;
        private bool _deviceLost;
        private int _stableCaptureFrames;
        private long _captureUnstableSinceMs;
        private long _lastRecreateMs;

        private ID3D11Texture2D? _inputTex;
        private ID3D11ShaderResourceView? _inputSrv;
        private readonly ID3D11Texture2D?[] _workTex = new ID3D11Texture2D?[13];
        private readonly ID3D11ShaderResourceView?[] _workSrv = new ID3D11ShaderResourceView?[13];
        private readonly ID3D11RenderTargetView?[] _workRtv = new ID3D11RenderTargetView?[13];
        private const int RtA = 0;
        private const int RtB = 1;
        private const int RtDown0 = 2; // 5 slots: 2..6
        private const int RtUp0 = 7;   // 4 slots: 7..10
        private const int RtSceneBlurA = 11;
        private const int RtSceneBlurB = 12;

        private ID3D11VertexShader? _vs;
        private ID3D11PixelShader? _psMain;
        private ID3D11PixelShader? _psDownPrefilter;
        private ID3D11PixelShader? _psDown;
        private ID3D11PixelShader? _psUpTent;
        private ID3D11PixelShader? _psBlurH;
        private ID3D11PixelShader? _psBlurV;
        private ID3D11PixelShader? _psBloomCombine;
        private ID3D11PixelShader? _psPassthrough;
        private ID3D11SamplerState? _sampler;
        private ID3D11Buffer? _cbuffer;
        private ID3D11Buffer? _passCbuffer;

        private readonly System.Collections.Generic.List<ID3D11PixelShader> _customEffects = new();

        private int _width;
        private int _height;
        private int _rw;
        private int _rh;
        private int _rectLeft;
        private int _rectTop;

        private IntPtr _robloxHwnd;
        private bool _hiddenByFocus;
        private int _lastSettingsVersion = -1;
        private bool _firstFrameLogged;
        private long _framesPresented;
        private long _captureTimeouts;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastStatsLog;
        private long _framesAtLastLog;
        private long _nextVisibilityCheckMs;
        private long _nextFollowCheckMs;
        private long _nextZOrderCheckMs;
        private int _cleanedUp;
        private CancellationToken _runToken;
        private int _builtRenderScaleIndex;

        public void Run(IntPtr robloxHwnd, CancellationToken token)
        {
            _runToken = token;
            _robloxHwnd = robloxHwnd;
            try
            {
                if (!TryGetWindowRect(robloxHwnd, out int left, out int top, out int width, out int height))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Roblox window disappeared before overlay start");
                    return;
                }
                _rectLeft = left;
                _rectTop = top;
                _width = Math.Max(16, width);
                _height = Math.Max(16, height);
                App.Logger.WriteLine(LOG_IDENT, $"Starting overlay for Roblox at {_rectLeft},{_rectTop} size {_width}x{_height}");

                CreateWindow();
                CreateDevice();
                RiShadeInterop.SetWindowDisplayAffinity(_hwnd, RiShadeInterop.WDA_EXCLUDEFROMCAPTURE);
                App.Logger.WriteLine(LOG_IDENT, "Using monitor capture, the overlay stays hidden from recordings");
                if (!CreateDuplicationForRect(_rectLeft, _rectTop))
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not create desktop duplication for the Roblox monitor, overlay aborted");
                    return;
                }
                CreateComposition();
                CreatePipeline();
                LoadCustomEffects();
                _ = RiShadeInterop.timeBeginPeriod(1);
                _captureFailures = 0;
                _stableCaptureFrames = 0;
                _captureUnstableSinceMs = 0;
                _lastRecreateMs = 0;

                var msg = default(RiShadeInterop.MSG);
                while (!token.IsCancellationRequested)
                {
                    while (RiShadeInterop.PeekMessageW(out msg, IntPtr.Zero, 0, 0, RiShadeInterop.PM_REMOVE))
                    {
                        RiShadeInterop.TranslateMessage(ref msg);
                        RiShadeInterop.DispatchMessageW(ref msg);
                    }

                    if (_deviceLost)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Restarting the overlay session to recover");
                        break;
                    }

                    if (!UpdateVisibility(token))
                        continue;

                    long now = Environment.TickCount64;
                    if (now >= _nextFollowCheckMs)
                    {
                        _nextFollowCheckMs = now + 500;
                        FollowRoblox();
                    }
                    if (now >= _nextZOrderCheckMs)
                    {
                        _nextZOrderCheckMs = now + 1000;
                        AssertZOrder();
                    }
                    RenderFrame();
                    LogStatsIfDue();
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("RiShadeOverlay::Run", ex);
            }
            finally
            {
                Cleanup();
            }
        }

        private static bool TryGetWindowRect(IntPtr hwnd, out int left, out int top, out int width, out int height)
        {
            left = top = width = height = 0;
            if (hwnd == IntPtr.Zero)
                return false;
            if (!RiShadeInterop.GetWindowRect(hwnd, out var rect))
                return false;
            left = rect.Left;
            top = rect.Top;
            width = Math.Max(16, rect.Right - rect.Left);
            height = Math.Max(16, rect.Bottom - rect.Top);
            return true;
        }

        private void CreateWindow()
        {
            _hInstance = RiShadeInterop.GetModuleHandleW(null);
            _wndProc = (h, m, w, l) => RiShadeInterop.DefWindowProcW(h, m, w, l);
            IntPtr classNamePtr = Marshal.StringToHGlobalUni(ClassName);
            try
            {
                var wc = new RiShadeInterop.WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<RiShadeInterop.WNDCLASSEXW>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = _hInstance,
                    lpszClassName = classNamePtr,
                };
                _classAtom = RiShadeInterop.RegisterClassExW(ref wc);

                int exStyle = RiShadeInterop.WS_EX_NOACTIVATE | RiShadeInterop.WS_EX_TOOLWINDOW | RiShadeInterop.WS_EX_TRANSPARENT | RiShadeInterop.WS_EX_TOPMOST | RiShadeInterop.WS_EX_LAYERED | RiShadeInterop.WS_EX_NOREDIRECTIONBITMAP;
                _hwnd = RiShadeInterop.CreateWindowExW(exStyle, new IntPtr(_classAtom), ClassName, RiShadeInterop.WS_POPUP, _rectLeft, _rectTop, _width, _height, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

                RiShadeInterop.SetLayeredWindowAttributes(_hwnd, 0, 255, RiShadeInterop.LWA_ALPHA);
                RiShadeInterop.SetWindowPos(_hwnd, RiShadeInterop.HWND_TOPMOST, _rectLeft, _rectTop, _width, _height, RiShadeInterop.SWP_NOACTIVATE | RiShadeInterop.SWP_SHOWWINDOW);
                RiShadeInterop.ShowWindow(_hwnd, RiShadeInterop.SW_SHOWNOACTIVATE);
                App.Logger.WriteLine(LOG_IDENT, "Overlay window created, click through and capture excluded");
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
                D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, _featureLevels, out _device, out _context).CheckError();
                App.Logger.WriteLine(LOG_IDENT, $"D3D11 device created on the default adapter, feature level {_device!.FeatureLevel}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Hardware device unavailable, using the software rasterizer: " + ex.Message);
                _context?.Dispose();
                _device?.Dispose();
                _context = null;
                _device = null;
                D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Warp, DeviceCreationFlags.BgraSupport, _featureLevels, out _device, out _context).CheckError();
                App.Logger.WriteLine(LOG_IDENT, $"WARP device created, feature level {_device!.FeatureLevel}");
            }
        }

        private void CreateComposition()
        {
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
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
                Flags = SwapChainFlags.None,
            };
            _swapChain = _factory!.CreateSwapChainForComposition(_device!, swapDesc, null);
            CreateBackBufferRtv();

            using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
            DCompApi.DCompositionCreateDevice(dxgiDevice, out _dcompDevice).CheckError();
            _dcompDevice!.CreateTargetForHwnd(_hwnd, true, out _dcompTarget);
            _dcompVisual = _dcompDevice.CreateVisual();
            _dcompVisual.SetContent(_swapChain);
            _dcompTarget!.SetRoot(_dcompVisual);
            _dcompDevice.Commit();
            App.Logger.WriteLine(LOG_IDENT, "DirectComposition swapchain attached to the overlay window");
        }

        private void CreateBackBufferRtv()
        {
            using var backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
            _backBufferRtv = _device!.CreateRenderTargetView(backBuffer);
        }

        private void LoadCustomEffects()
        {
            try
            {
                string dir = System.IO.Path.Combine(Paths.Integrations, "RiShade", "Effects");
                System.IO.Directory.CreateDirectory(dir);
                foreach (string file in System.IO.Directory.GetFiles(dir, "*.hlsl"))
                {
                    string name = System.IO.Path.GetFileName(file);
                    try
                    {
                        string source = RiShadeShaders.Source + "\n" + System.IO.File.ReadAllText(file);
                        byte[] sourceBytes = System.Text.Encoding.ASCII.GetBytes(source);
                        Vortice.D3DCompiler.Compiler.Compile(sourceBytes, "PSCustom", name, "ps_5_0", out var blob, out var err);
                        using (err)
                        {
                            if (blob == null)
                            {
                                string emsg = err != null ? err.ConvertToString() : "unknown";
                                App.Logger.WriteLine(LOG_IDENT, $"Custom effect {name} failed to compile: {emsg}");
                                continue;
                            }
                        }
                        using (blob)
                        {
                            _customEffects.Add(_device!.CreatePixelShader(blob));
                        }
                        App.Logger.WriteLine(LOG_IDENT, $"Custom effect {name} loaded, entry PSCustom, scene on t0 (no AI depth on t1 in this port)");
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Custom effect {name} failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Custom effects folder scan failed: " + ex.Message);
            }
        }

        private static readonly byte[] _shaderSourceBytes = System.Text.Encoding.ASCII.GetBytes(RiShadeShaders.Source);

        private ID3D11PixelShader CompilePs(string entry)
        {
            Vortice.D3DCompiler.Compiler.Compile(_shaderSourceBytes, entry, "RiShade", "ps_5_0", out var blob, out var err);
            using (err)
            {
                if (blob == null)
                {
                    string emsg = err != null ? err.ConvertToString() : "unknown";
                    throw new InvalidOperationException("RiShade shader compile failed for " + entry + ": " + emsg);
                }
            }
            using (blob)
            {
                return _device!.CreatePixelShader(blob);
            }
        }

        private void CreatePipeline()
        {
            _builtRenderScaleIndex = App.Settings.Prop.RiShade.RenderScaleIndex;
            float renderScale = App.Settings.Prop.RiShade.ResolveRenderScale();
            _rw = Math.Max(64, (int)Math.Round(_width * renderScale));
            _rh = Math.Max(64, (int)Math.Round(_height * renderScale));
            var sw = Stopwatch.StartNew();
            Vortice.D3DCompiler.Compiler.Compile(_shaderSourceBytes, "VSMain", "RiShade", "vs_5_0", out var vsBlob, out var vsErr);
            using (vsErr)
            {
                if (vsBlob == null)
                    throw new InvalidOperationException("RiShade vertex shader compile failed");
            }
            using (vsBlob)
            {
                _vs = _device!.CreateVertexShader(vsBlob);
            }
            _psMain = CompilePs("PSMain");
            _psDownPrefilter = CompilePs("PSDownsamplePrefilter");
            _psDown = CompilePs("PSDownsample");
            _psUpTent = CompilePs("PSUpsampleTent");
            _psBlurH = CompilePs("PSBlurH");
            _psBlurV = CompilePs("PSBlurV");
            _psBloomCombine = CompilePs("PSBloomCombine");
            _psPassthrough = CompilePs("PSPassthrough");
            sw.Stop();
            App.Logger.WriteLine(LOG_IDENT, $"Compiled shaders in {sw.ElapsedMilliseconds}ms");

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
                SizeInBytes = Marshal.SizeOf<RiShadeParams>(),
                BindFlags = BindFlags.ConstantBuffer,
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None,
            });

            _passCbuffer = _device!.CreateBuffer(new BufferDescription
            {
                SizeInBytes = Marshal.SizeOf<Vector4>(),
                BindFlags = BindFlags.ConstantBuffer,
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None,
            });

            CreateSizedResources();
        }

        private void CreateSizedResources()
        {
            _inputSrv?.Dispose();
            _inputTex?.Dispose();
            _inputTex = _device!.CreateTexture2D(new Texture2DDescription
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
            _inputSrv = _device!.CreateShaderResourceView(_inputTex);

            for (int i = 0; i < _workTex.Length; i++)
            {
                _workRtv[i]?.Dispose();
                _workSrv[i]?.Dispose();
                _workTex[i]?.Dispose();
                var (w, h) = WorkTexSize(i);
                _workTex[i] = _device!.CreateTexture2D(new Texture2DDescription
                {
                    Width = w,
                    Height = h,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R16G16B16A16_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                    CpuAccessFlags = CpuAccessFlags.None,
                });
                _workSrv[i] = _device!.CreateShaderResourceView(_workTex[i]!);
                _workRtv[i] = _device!.CreateRenderTargetView(_workTex[i]!);
            }
        }

        private int LvlW(int level) => Math.Max(8, _rw >> level);

        private int LvlH(int level) => Math.Max(8, _rh >> level);

        private (int, int) WorkTexSize(int index)
        {
            if (index >= RtDown0 && index < RtUp0)
            {
                int level = index - RtDown0 + 1;
                return (LvlW(level), LvlH(level));
            }
            if (index >= RtUp0 && index < RtSceneBlurA)
            {
                int level = index - RtUp0 + 1;
                return (LvlW(level), LvlH(level));
            }
            if (index == RtSceneBlurA || index == RtSceneBlurB)
                return (LvlW(1), LvlH(1));
            return (_rw, _rh);
        }

        private void SetPassPx(int srcW, int srcH, bool upscale = false)
        {
            var v = new Vector4(1f / Math.Max(srcW, 1), 1f / Math.Max(srcH, 1), upscale ? 1f : 0f, 0f);
            _context!.UpdateSubresource(ref v, _passCbuffer!);
        }

        private void SetVp(int w, int h)
        {
            _context!.RSSetViewport(new Viewport(0, 0, w, h, 0, 1));
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
                App.Logger.WriteException("RiShadeOverlay::CreateDuplication", ex);
                return false;
            }
        }

        private bool UpdateVisibility(CancellationToken token)
        {
            long tick = Environment.TickCount64;
            if (tick < _nextVisibilityCheckMs)
                return !_hiddenByFocus;
            _nextVisibilityCheckMs = tick + 500;
            IntPtr fg = RiShadeInterop.GetForegroundWindow();
            bool robloxActive = _robloxHwnd != IntPtr.Zero && fg == _robloxHwnd;
            if (robloxActive)
            {
                if (_hiddenByFocus)
                {
                    _hiddenByFocus = false;
                    RiShadeInterop.ShowWindow(_hwnd, RiShadeInterop.SW_SHOWNOACTIVATE);
                    AssertZOrder();
                    App.Logger.WriteLine(LOG_IDENT, "Roblox focused, overlay visible again");
                }
                return true;
            }
            if (!_hiddenByFocus)
            {
                _hiddenByFocus = true;
                RiShadeInterop.ShowWindow(_hwnd, RiShadeInterop.SW_HIDE);
                App.Logger.WriteLine(LOG_IDENT, "Roblox lost focus, overlay hidden");
            }
            token.WaitHandle.WaitOne(500);
            return false;
        }

        private void FollowRoblox()
        {
            if (!TryGetWindowRect(_robloxHwnd, out int left, out int top, out int w, out int h))
                return;
            if (left <= -30000 || top <= -30000)
                return;
            if (left == _rectLeft && top == _rectTop && w == _width && h == _height)
                return;

            bool sizeChanged = w != _width || h != _height;
            _rectLeft = left;
            _rectTop = top;
            _width = w;
            _height = h;

            if (sizeChanged)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Roblox window resized, rebuilding targets at {_width}x{_height}");
                _backBufferRtv?.Dispose();
                _backBufferRtv = null;
                _swapChain!.ResizeBuffers(3, _width, _height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
                CreateBackBufferRtv();
                float scale = App.Settings.Prop.RiShade.ResolveRenderScale();
                _rw = Math.Max(64, (int)Math.Round(_width * scale));
                _rh = Math.Max(64, (int)Math.Round(_height * scale));
                CreateSizedResources();
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
                    CreateDuplicationForRect(_rectLeft, _rectTop);
                }
            }
            RiShadeInterop.SetWindowPos(_hwnd, RiShadeInterop.HWND_TOPMOST, _rectLeft, _rectTop, _width, _height, RiShadeInterop.SWP_NOACTIVATE | RiShadeInterop.SWP_SHOWWINDOW);
            AssertZOrder();
        }

        private void AssertZOrder()
        {
            if (_hwnd == IntPtr.Zero || _hiddenByFocus)
                return;
            RiShadeInterop.SetWindowPos(_hwnd, RiShadeInterop.HWND_TOPMOST, _rectLeft, _rectTop, _width, _height, RiShadeInterop.SWP_NOACTIVATE);
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
            if (now - _captureUnstableSinceMs > 6000)
            {
                App.Logger.WriteLine(LOG_IDENT, "Screen capture stayed unstable, ending this overlay session");
                _deviceLost = true;
                return false;
            }
            if (now - _lastRecreateMs >= 500)
            {
                _lastRecreateMs = now;
                CreateDuplicationForRect(_rectLeft, _rectTop);
            }
            _runToken.WaitHandle.WaitOne(100);
            return false;
        }

        private bool CaptureFrame()
        {
            if (_inputTex == null || _duplication == null)
                return false;

            IDXGIResource? desktopResource = null;
            bool acquired = false;
            try
            {
                // AcquireNextFrame doesn't return a Result in this Vortice version - it throws a
                // SharpGenException carrying the HRESULT on failure/timeout instead.
                try
                {
                    _duplication.AcquireNextFrame(16, out _, out desktopResource);
                }
                catch (SharpGen.Runtime.SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.WaitTimeout)
                {
                    _captureTimeouts++;
                    return false;
                }
                catch (SharpGen.Runtime.SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.AccessLost)
                {
                    return HandleCaptureUnstable("Capture access lost");
                }
                if (desktopResource == null)
                    return HandleCaptureUnstable("Capture returned no frame");
                acquired = true;
                _stableCaptureFrames++;
                if (_stableCaptureFrames >= 15)
                {
                    _captureUnstableSinceMs = 0;
                    _captureFailures = 0;
                }

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

                var box = new Box(srcLeft, srcTop, 0, right, bottom, 1);
                _context!.CopySubresourceRegion(_inputTex, 0, 0, 0, 0, desktopTex, 0, box);
                _hasFirstCapture = true;
                return true;
            }
            finally
            {
                desktopResource?.Dispose();
                if (acquired)
                    _duplication.ReleaseFrame();
            }
        }

        private void RenderFrame()
        {
            RebuildForScaleIfNeeded();
            bool fresh = CaptureFrame();
            if (!fresh)
            {
                if (!_hasFirstCapture || (_duplication == null))
                {
                    _runToken.WaitHandle.WaitOne(15);
                    return;
                }
            }

            RenderPasses(App.Settings.Prop.RiShade, _inputSrv!, _backBufferRtv!);

            var presentResult = _swapChain!.Present(0, PresentFlags.None);
            if (presentResult == Vortice.DXGI.ResultCode.DeviceRemoved || presentResult == Vortice.DXGI.ResultCode.DeviceReset)
            {
                App.Logger.WriteLine(LOG_IDENT, "Graphics device was lost, the session will restart");
                _deviceLost = true;
                return;
            }
            _framesPresented++;

            if (!_firstFrameLogged)
            {
                _firstFrameLogged = true;
                App.Logger.WriteLine(LOG_IDENT, $"RiShade is live, first frame presented at {_width}x{_height}");
            }
        }

        private static readonly ID3D11ShaderResourceView?[] _nullSrvs = new ID3D11ShaderResourceView?[4];

        private void RenderPasses(Models.Persistable.RiShadeSettings s, ID3D11ShaderResourceView inputSrv, ID3D11RenderTargetView dst)
        {
            _context!.VSSetShader(_vs);
            _context.PSSetConstantBuffer(0, _cbuffer);
            _context.PSSetConstantBuffer(1, _passCbuffer);
            _context.PSSetSampler(0, _sampler);
            _context.IASetInputLayout(null);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            if (!s.HasVisibleEffects)
            {
                SetPassPx(_width, _height);
                SetVp(_width, _height);
                DrawPass(_psPassthrough!, dst, inputSrv);
                _context.PSSetShaderResources(0, _nullSrvs);
                return;
            }
            UpdateParamsIfNeeded(s);

            SetVp(_rw, _rh);
            var srcSrv = inputSrv;
            bool wantSoft = s.ClarityStrength > 0f || s.AmbientStrength > 0f;
            if (wantSoft)
            {
                SetPassPx(_width, _height);
                SetVp(LvlW(1), LvlH(1));
                DrawPass(_psDown!, _workRtv[RtDown0]!, srcSrv);
                SetPassPx(LvlW(1), LvlH(1));
                DrawPass(_psBlurH!, _workRtv[RtSceneBlurB]!, _workSrv[RtDown0]);
                DrawPass(_psBlurV!, _workRtv[RtSceneBlurA]!, _workSrv[RtSceneBlurB]);
                SetVp(_rw, _rh);
            }
            DrawPass(_psMain!, _workRtv[RtA]!, srcSrv, null, _workSrv[RtSceneBlurA]);
            int scene = RtA;

            if (s.BloomEnabled)
            {
                int levels = Math.Clamp(s.BloomPasses + 1, 2, 5);
                SetPassPx(_rw, _rh);
                SetVp(LvlW(1), LvlH(1));
                DrawPass(_psDownPrefilter!, _workRtv[RtDown0]!, _workSrv[scene]);
                for (int i = 1; i < levels; i++)
                {
                    SetPassPx(LvlW(i), LvlH(i));
                    SetVp(LvlW(i + 1), LvlH(i + 1));
                    DrawPass(_psDown!, _workRtv[RtDown0 + i]!, _workSrv[RtDown0 + i - 1]);
                }
                int src = RtDown0 + levels - 1;
                int srcLevel = levels;
                for (int i = levels - 2; i >= 0; i--)
                {
                    SetPassPx(LvlW(srcLevel), LvlH(srcLevel));
                    SetVp(LvlW(i + 1), LvlH(i + 1));
                    DrawPass(_psUpTent!, _workRtv[RtUp0 + i]!, _workSrv[src], _workSrv[RtDown0 + i]);
                    src = RtUp0 + i;
                    srcLevel = i + 1;
                }
                SetVp(_rw, _rh);
                DrawPass(_psBloomCombine!, _workRtv[RtB]!, _workSrv[scene], _workSrv[src]);
                scene = RtB;
            }

            foreach (var custom in _customEffects)
            {
                int next = scene == RtA ? RtB : RtA;
                DrawPass(custom, _workRtv[next]!, _workSrv[scene]);
                scene = next;
            }

            SetPassPx(_rw, _rh, _rw < _width || _rh < _height);
            SetVp(_width, _height);
            DrawPass(_psPassthrough!, dst, _workSrv[scene]);
            _context.PSSetShaderResources(0, _nullSrvs);
        }

        private void UpdateParamsIfNeeded(Models.Persistable.RiShadeSettings s)
        {
            // Recomputed every frame: settings are cheap to pack and this keeps live edits from the
            // settings UI (running in a different process) reflected immediately, without needing
            // Voidstrap's file-watcher + version-counter scheme (see RiShadeManager.cs).
            float[] temp = s.ResolveColorTemp();
            var p = new RiShadeParams
            {
                PA = new Vector4(s.GradeEnabled ? 1f : 0f, 1f, 1f, s.Brightness),
                PB = new Vector4(s.Gamma, s.HueShift, (float)_clock.Elapsed.TotalSeconds, s.ChromaEnabled ? 1f : 0f),
                PC = new Vector4(s.Lift[0], s.Lift[1], s.Lift[2], s.TonemapEnabled ? 1f : 0f),
                PD = new Vector4(s.Gain[0], s.Gain[1], s.Gain[2], s.TonemapMode),
                PE = new Vector4(s.ColorBalance[0], s.ColorBalance[1], s.ColorBalance[2], s.TonemapExposure),
                PF = new Vector4(temp[0], temp[1], temp[2], s.TonemapWhitepoint),
                PG = new Vector4(s.VignetteEnabled ? 1f : 0f, s.VignetteStrength, s.VignetteFeather, s.VignetteCenterX),
                PH = new Vector4(s.VignetteCenterY, s.SharpenEnabled ? 1f : 0f, s.SharpenStrength, s.SharpenRadius),
                PI = new Vector4(s.SharpenClamp, s.ChromaStrength, s.ChromaRadial ? 1f : 0f, s.GrainEnabled ? 1f : 0f),
                PJ = new Vector4(s.GrainStrength, s.GrainSize, s.GrainColored ? 1f : 0f, 0f),
                PK = new Vector4(0f, 0f, 0f, 0f),
                PL = new Vector4(0f, 0f, 0f, _rw),
                PM = new Vector4(_rh, s.BloomStrength, s.BloomThreshold, s.BloomRadius),
                PN = new Vector4(1f, 1f, 1f, 0f),
                PO = new Vector4(0f, 0f, 0f, s.ClarityStrength),
                PP = new Vector4(s.DebandEnabled ? 1f : 0f, s.DebandStrength, 0f, 0f),
                PQ = new Vector4(0f, 0f, 0f, s.AmbientStrength),
                PR = new Vector4(1f, 0f, 1f, 0f),
                PS = new Vector4(0f, 0f, 0f, 0f),
                PT = new Vector4(0f, 0f, 0f, 0f),
            };
            _context!.UpdateSubresource(ref p, _cbuffer!);
        }

        private void DrawPass(ID3D11PixelShader ps, ID3D11RenderTargetView target, ID3D11ShaderResourceView? t0, ID3D11ShaderResourceView? t1 = null, ID3D11ShaderResourceView? t2 = null, ID3D11ShaderResourceView? t3 = null)
        {
            _context!.PSSetShaderResources(0, _nullSrvs);
            _context.OMSetRenderTargets(target);
            _context.PSSetShader(ps);
            _context.PSSetShaderResource(0, t0);
            if (t1 != null) _context.PSSetShaderResource(1, t1);
            if (t2 != null) _context.PSSetShaderResource(2, t2);
            if (t3 != null) _context.PSSetShaderResource(3, t3);
            _context.Draw(3, 0);
        }

        private void RebuildForScaleIfNeeded()
        {
            if (App.Settings.Prop.RiShade.RenderScaleIndex == _builtRenderScaleIndex)
                return;
            _builtRenderScaleIndex = App.Settings.Prop.RiShade.RenderScaleIndex;
            float scale = App.Settings.Prop.RiShade.ResolveRenderScale();
            _rw = Math.Max(64, (int)Math.Round(_width * scale));
            _rh = Math.Max(64, (int)Math.Round(_height * scale));
            try
            {
                CreateSizedResources();
                _lastSettingsVersion = -1;
                App.Logger.WriteLine(LOG_IDENT, $"Render resolution changed live to {_rw}x{_rh}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("RiShadeOverlay::RebuildForScale", ex);
            }
        }

        private void LogStatsIfDue()
        {
            double now = _clock.Elapsed.TotalSeconds;
            if (_lastStatsLog == 0)
            {
                _lastStatsLog = now;
                return;
            }
            if (now - _lastStatsLog < 60.0)
                return;
            long frames = _framesPresented - _framesAtLastLog;
            double fps = frames / (now - _lastStatsLog);
            App.Logger.WriteLine(LOG_IDENT, $"Running at {fps:0} fps, {_framesPresented} frames total, {_captureTimeouts} idle waits");
            _lastStatsLog = now;
            _framesAtLastLog = _framesPresented;
        }

        private void Cleanup()
        {
            if (Interlocked.Exchange(ref _cleanedUp, 1) != 0)
                return;
            try
            {
                _hasFirstCapture = false;
                _context?.ClearState();
                _context?.Flush();
                _duplication?.Dispose();
                for (int i = 0; i < _workTex.Length; i++)
                {
                    _workRtv[i]?.Dispose();
                    _workSrv[i]?.Dispose();
                    _workTex[i]?.Dispose();
                }
                _inputSrv?.Dispose();
                _inputTex?.Dispose();
                _cbuffer?.Dispose();
                _passCbuffer?.Dispose();
                _sampler?.Dispose();
                _psMain?.Dispose();
                _psDownPrefilter?.Dispose();
                _psDown?.Dispose();
                _psUpTent?.Dispose();
                _psBlurH?.Dispose();
                _psBlurV?.Dispose();
                _psBloomCombine?.Dispose();
                foreach (var custom in _customEffects)
                    custom.Dispose();
                _customEffects.Clear();
                _psPassthrough?.Dispose();
                _vs?.Dispose();
                _dcompVisual?.Dispose();
                _dcompTarget?.Dispose();
                _dcompDevice?.Dispose();
                _backBufferRtv?.Dispose();
                _swapChain?.Dispose();
                _factory?.Dispose();
                _context?.Dispose();
                _device?.Dispose();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("RiShadeOverlay::Cleanup", ex);
            }
            try
            {
                if (_hwnd != IntPtr.Zero)
                    RiShadeInterop.DestroyWindow(_hwnd);
                if (_classAtom != 0)
                    RiShadeInterop.UnregisterClassW(new IntPtr(_classAtom), _hInstance);
            }
            catch
            {
            }
            _ = RiShadeInterop.timeEndPeriod(1);
            _hwnd = IntPtr.Zero;
            _classAtom = 0;
            _wndProc = null;
            App.Logger.WriteLine(LOG_IDENT, "Overlay stopped and all GPU resources released");
        }
    }
}
