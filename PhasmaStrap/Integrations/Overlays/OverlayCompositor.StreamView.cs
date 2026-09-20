using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using Interop = PhasmaStrap.Integrations.Overlays.OverlayInterop;

namespace PhasmaStrap.Integrations.Overlays
{
    internal sealed partial class OverlayCompositor
    {
        private const string StreamShaderSource = @"
struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

cbuffer StreamSafeParams : register(b0)
{
    float4 dims;
    float4 params;
    float4 regions[16];
};

Texture2D tex0 : register(t0);

float3 At(float2 p)
{
    int2 q = int2(clamp(p, float2(0, 0), dims.xy - 1));
    return tex0.Load(int3(q, 0)).rgb;
}

float4 PSStreamSafe(VSOut inp) : SV_Target
{
    float2 p = inp.pos.xy;
    int count = (int)params.x;

    [loop]
    for (int i = 0; i < 16; i++)
    {
        if (i >= count)
            break;

        float4 r = regions[i] * dims.xyxy;
        if (p.x >= r.x && p.x < r.z && p.y >= r.y && p.y < r.w)
        {
            if (params.z > 0.5)
                return float4(0.07, 0.07, 0.08, 1.0);

            float b = params.y;
            float2 c = (floor(p / b) + 0.5) * b;
            float q = b * 0.25;
            float3 avg = (At(c + float2(-q, -q)) + At(c + float2(q, -q)) + At(c + float2(-q, q)) + At(c + float2(q, q))) * 0.25;
            return float4(avg, 1.0);
        }
    }

    return float4(At(p), 1.0);
}
";

        [StructLayout(LayoutKind.Sequential)]
        private struct StreamSafeParams
        {
            public Vector4 Dims;
            public Vector4 Params;
            public Vector4 R0, R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11, R12, R13, R14, R15;

            public void SetRegion(int index, Vector4 value)
            {
                switch (index)
                {
                    case 0: R0 = value; break;
                    case 1: R1 = value; break;
                    case 2: R2 = value; break;
                    case 3: R3 = value; break;
                    case 4: R4 = value; break;
                    case 5: R5 = value; break;
                    case 6: R6 = value; break;
                    case 7: R7 = value; break;
                    case 8: R8 = value; break;
                    case 9: R9 = value; break;
                    case 10: R10 = value; break;
                    case 11: R11 = value; break;
                    case 12: R12 = value; break;
                    case 13: R13 = value; break;
                    case 14: R14 = value; break;
                    case 15: R15 = value; break;
                }
            }
        }

        private IntPtr _streamHwnd;
        private ushort _streamClassAtom;
        private Interop.WndProcDelegate? _streamWndProc;
        private IDXGISwapChain1? _streamSwapChain;
        private ID3D11Texture2D? _streamBackBuffer;
        private ID3D11RenderTargetView? _streamRtv;
        private IDCompositionTarget? _streamTarget;
        private IDCompositionVisual? _streamVisual;
        private ID3D11PixelShader? _psStreamSafe;
        private ID3D11Buffer? _streamCbuffer;
        private int _streamWidth;
        private int _streamHeight;
        private bool _overlayHiddenForStream;
        private bool _streamFailed;

        private bool StreamLive => _streamHwnd != IntPtr.Zero && _streamSwapChain != null;

        private void SyncStreamWindow()
        {
            bool wanted = App.Settings.Prop.StreamSafeEnabled;

            if (wanted && !StreamLive && !_streamFailed)
            {
                try
                {
                    CreateStreamView();
                    App.Logger.WriteLine("OverlayCompositor::StreamView", $"Stream view window is up as \"{StreamSafe.WindowTitle}\" ({_streamHwnd.ToInt64():X}), {_width}x{_height}");
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("OverlayCompositor::CreateStreamView", ex);
                    DestroyStreamView();

                    _streamFailed = true;
                }
            }
            else if (!wanted && StreamLive)
            {
                App.Logger.WriteLine("OverlayCompositor::StreamView", "Stream-safe mode is off, closing the stream view window");
                DestroyStreamView();
            }
        }

        private void SyncStreamView()
        {
            bool overlayNeeded = OverlaySettings.OverlayWindowNeeded;
            if (!overlayNeeded && !_overlayHiddenForStream)
            {
                _overlayHiddenForStream = true;
                Interop.ShowWindow(_hwnd, Interop.SW_HIDE);
            }
            else if (overlayNeeded && _overlayHiddenForStream)
            {
                _overlayHiddenForStream = false;
                Interop.ShowWindow(_hwnd, Interop.SW_SHOWNOACTIVATE);
                AssertZOrder();
            }
        }

        private void CreateStreamView()
        {
            _hInstance = _hInstance == IntPtr.Zero ? Interop.GetModuleHandleW(null) : _hInstance;
            _streamWndProc = (h, m, w, l) => Interop.DefWindowProcW(h, m, w, l);

            IntPtr classNamePtr = Marshal.StringToHGlobalUni(StreamSafe.WindowClass);
            try
            {
                var wc = new Interop.WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<Interop.WNDCLASSEXW>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_streamWndProc),
                    hInstance = _hInstance,
                    lpszClassName = classNamePtr,
                };
                _streamClassAtom = Interop.RegisterClassExW(ref wc);
            }
            finally
            {
                Marshal.FreeHGlobal(classNamePtr);
            }

            int exStyle = Interop.WS_EX_NOACTIVATE | Interop.WS_EX_NOREDIRECTIONBITMAP;
            _streamHwnd = Interop.CreateWindowExW(exStyle, new IntPtr(_streamClassAtom), StreamSafe.WindowTitle, Interop.WS_POPUP, _rectLeft, _rectTop, _width, _height, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
            if (_streamHwnd == IntPtr.Zero)
                throw new InvalidOperationException("Could not create the stream view window");

            Interop.SetWindowPos(_streamHwnd, HwndBottom, _rectLeft, _rectTop, _width, _height, Interop.SWP_NOACTIVATE | Interop.SWP_SHOWWINDOW);
            Interop.ShowWindow(_streamHwnd, Interop.SW_SHOWNOACTIVATE);

            _streamSwapChain = _factory!.CreateSwapChainForComposition(_device!, new SwapChainDescription1
            {
                Width = _width,
                Height = _height,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
            }, null);
            _streamWidth = _width;
            _streamHeight = _height;
            CreateStreamRtv();

            _dcompDevice!.CreateTargetForHwnd(_streamHwnd, true, out _streamTarget);
            _streamVisual = _dcompDevice.CreateVisual();
            _streamVisual.SetContent(_streamSwapChain);
            _streamTarget!.SetRoot(_streamVisual);
            _dcompDevice.Commit();

            if (_psStreamSafe == null)
            {
                Vortice.D3DCompiler.Compiler.Compile(StreamShaderSource, "PSStreamSafe", "StreamSafe", "ps_5_0", out var blob, out var err);
                using (err)
                {
                    if (blob == null)
                        throw new InvalidOperationException("Stream-safe shader compile failed: " + (err?.ConvertToString() ?? "unknown"));
                }
                using (blob)
                    _psStreamSafe = _device!.CreatePixelShader(blob.GetBytes());

                _streamCbuffer = _device!.CreateBuffer(new BufferDescription
                {
                    SizeInBytes = Marshal.SizeOf<StreamSafeParams>(),
                    BindFlags = BindFlags.ConstantBuffer,
                    Usage = ResourceUsage.Default,
                    CpuAccessFlags = CpuAccessFlags.None,
                });
            }

            App.Logger.WriteLine(LOG_IDENT, $"Stream view created at {_width}x{_height} ({StreamSafe.Regions.Count} hidden area(s))");
        }

        private static readonly IntPtr HwndBottom = new(1);

        private void CreateStreamRtv()
        {
            _streamRtv?.Dispose();
            _streamBackBuffer?.Dispose();
            _streamBackBuffer = _streamSwapChain!.GetBuffer<ID3D11Texture2D>(0);
            _streamRtv = _device!.CreateRenderTargetView(_streamBackBuffer);
        }

        private void PositionStreamView()
        {
            if (!StreamLive)
                return;

            if (_streamWidth != _width || _streamHeight != _height)
            {
                _streamRtv?.Dispose();
                _streamRtv = null;
                _streamBackBuffer?.Dispose();
                _streamBackBuffer = null;
                _streamSwapChain!.ResizeBuffers(2, _width, _height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
                _streamWidth = _width;
                _streamHeight = _height;
                CreateStreamRtv();
            }

            Interop.SetWindowPos(_streamHwnd, HwndBottom, _rectLeft, _rectTop, _width, _height, Interop.SWP_NOACTIVATE);
        }

        private void RenderStream(ID3D11ShaderResourceView finalSrv, int syncInterval)
        {
            if (!StreamLive || _streamRtv == null || _psStreamSafe == null)
                return;

            IReadOnlyList<StreamSafeRegion> regions = StreamSafe.Regions;

            var data = new StreamSafeParams
            {
                Dims = _dims,

                Params = new Vector4(regions.Count, Math.Max(8, _height / 30), App.Settings.Prop.StreamSafeStyle == StreamSafe.StyleBlack ? 1 : 0, 0),
            };
            for (int i = 0; i < regions.Count; i++)
                data.SetRegion(i, new Vector4((float)regions[i].X, (float)regions[i].Y, (float)(regions[i].X + regions[i].W), (float)(regions[i].Y + regions[i].H)));

            _context!.UpdateSubresource(ref data, _streamCbuffer!);

            _context.VSSetShader(_vs!);
            _context.PSSetConstantBuffer(0, _streamCbuffer);
            _context.IASetInputLayout(null);
            _context.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
            _context.RSSetViewport(new Viewport(0, 0, _width, _height, 0, 1));
            _context.PSSetShaderResources(0, _nullSrvs);
            _context.OMSetRenderTargets(_streamRtv);
            _context.PSSetShader(_psStreamSafe);
            _context.PSSetShaderResource(0, finalSrv);
            _context.Draw(3, 0);
            _context.PSSetShaderResources(0, _nullSrvs);

            if (App.Settings.Prop.StreamSafeCrosshair)
                DrawCrosshair(_streamRtv);

            var result = _streamSwapChain!.Present(syncInterval, PresentFlags.None);
            if (result == Vortice.DXGI.ResultCode.DeviceRemoved || result == Vortice.DXGI.ResultCode.DeviceReset)
                _deviceLost = true;
        }

        private void DestroyStreamView()
        {
            try
            {
                _streamVisual?.Dispose();
                _streamVisual = null;
                _streamTarget?.Dispose();
                _streamTarget = null;
                _dcompDevice?.Commit();
                _streamRtv?.Dispose();
                _streamRtv = null;
                _streamBackBuffer?.Dispose();
                _streamBackBuffer = null;
                _streamSwapChain?.Dispose();
                _streamSwapChain = null;
                _psStreamSafe?.Dispose();
                _psStreamSafe = null;
                _streamCbuffer?.Dispose();
                _streamCbuffer = null;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("OverlayCompositor::DestroyStreamView", ex);
            }

            if (_streamHwnd != IntPtr.Zero)
            {
                Interop.DestroyWindow(_streamHwnd);
                _streamHwnd = IntPtr.Zero;
                App.Logger.WriteLine(LOG_IDENT, "Stream view closed");
            }

            if (_streamClassAtom != 0)
            {
                Interop.UnregisterClassW(new IntPtr(_streamClassAtom), _hInstance);
                _streamClassAtom = 0;
            }
        }
    }
}
