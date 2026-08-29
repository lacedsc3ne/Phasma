using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PhasmaStrap.Integrations.AntiAliasing
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct AntiAliasingParams
    {
        public Vector4 Dims;
        public Vector4 SrcRect;
    }

    /// <summary>
    /// Anti-Aliasing's shader pass(es) as a stage that plugs into
    /// <see cref="Overlays.OverlayCompositor"/>'s single shared D3D11 device/context, rather than
    /// owning its own device/window/swapchain/capture. This is the render-pipeline half of the
    /// original standalone AntiAliasingOverlay.cs (CreatePipeline/CreateSizedResources/
    /// RenderMethodInto/DrawPass) with the window/device/composition/capture/message-loop half
    /// removed, since the compositor already owns all of that.
    /// </summary>
    internal sealed class AntiAliasingStage : IDisposable
    {
        private const string LOG_IDENT = "AntiAliasing";

        private ID3D11Device _device = null!;
        private ID3D11DeviceContext _context = null!;

        private ID3D11Texture2D? _workTexA;
        private ID3D11ShaderResourceView? _workSrvA;
        private ID3D11RenderTargetView? _workRtvA;
        private ID3D11Texture2D? _workTexB;
        private ID3D11ShaderResourceView? _workSrvB;
        private ID3D11RenderTargetView? _workRtvB;

        private ID3D11VertexShader? _vs;
        private ID3D11PixelShader? _psPass;
        private ID3D11PixelShader? _psFxaa;
        private ID3D11PixelShader? _psFxaaUltra;
        private ID3D11PixelShader? _psSmaaEdge;
        private ID3D11PixelShader? _psSmaaWeights;
        private ID3D11PixelShader? _psSmaaWeightsUltra;
        private ID3D11PixelShader? _psSmaaBlend;
        private ID3D11PixelShader? _psDlaaMask;
        private ID3D11PixelShader? _psDlaa;
        private ID3D11PixelShader? _psNfaa;
        private ID3D11PixelShader? _psTsaa;
        private ID3D11PixelShader? _psTsaaSharpen;
        private ID3D11SamplerState? _sampler;
        private ID3D11Buffer? _cbuffer;

        private int _width;
        private int _height;
        private int _lastMethodRendered = -1;
        private int _tsaaHistory;
        private bool _tsaaSeeded;

        private static readonly ID3D11ShaderResourceView?[] _nullSrvs = new ID3D11ShaderResourceView?[2];

        public void CreatePipeline(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
        {
            _device = device;
            _context = context;
            _width = Math.Max(16, width);
            _height = Math.Max(16, height);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Vortice.D3DCompiler.Compiler.Compile(AntiAliasingShaders.Source, "VSMain", "AntiAliasing", "vs_5_0", out var vsBlob, out var vsErr);
            using (vsErr)
            {
                if (vsBlob == null)
                    throw new InvalidOperationException("AntiAliasing vertex shader compile failed");
            }
            using (vsBlob)
            {
                _vs = _device.CreateVertexShader(vsBlob.GetBytes());
            }
            _psPass = CompilePs("PSPass");
            _psFxaa = CompilePs("PSFxaa");
            _psFxaaUltra = CompilePs("PSFxaaUltra");
            _psSmaaEdge = CompilePs("PSSmaaEdge");
            _psSmaaWeights = CompilePs("PSSmaaWeights");
            _psSmaaWeightsUltra = CompilePs("PSSmaaWeightsUltra");
            _psSmaaBlend = CompilePs("PSSmaaBlend");
            _psDlaaMask = CompilePs("PSDlaaMask");
            _psDlaa = CompilePs("PSDlaa");
            _psNfaa = CompilePs("PSNfaa");
            _psTsaa = CompilePs("PSTsaa");
            _psTsaaSharpen = CompilePs("PSTsaaSharpen");
            sw.Stop();
            App.Logger.WriteLine(LOG_IDENT, $"Compiled shaders in {sw.ElapsedMilliseconds}ms");

            _sampler = _device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunction = ComparisonFunction.Never,
                MinLOD = 0,
                MaxLOD = float.MaxValue,
            });

            _cbuffer = _device.CreateBuffer(new BufferDescription
            {
                SizeInBytes = Marshal.SizeOf<AntiAliasingParams>(),
                BindFlags = BindFlags.ConstantBuffer,
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None,
            });

            RebuildSizedResources();
        }

        private ID3D11PixelShader CompilePs(string entry)
        {
            Vortice.D3DCompiler.Compiler.Compile(AntiAliasingShaders.Source, entry, "AntiAliasing", "ps_5_0", out var blob, out var err);
            using (err)
            {
                if (blob == null)
                {
                    string emsg = err != null ? err.ConvertToString() : "unknown";
                    throw new InvalidOperationException("AntiAliasing shader compile failed for " + entry + ": " + emsg);
                }
            }
            using (blob)
            {
                return _device.CreatePixelShader(blob.GetBytes());
            }
        }

        public void EnsureSize(int width, int height)
        {
            width = Math.Max(16, width);
            height = Math.Max(16, height);
            if (width == _width && height == _height && _workTexA != null)
                return;
            _width = width;
            _height = height;
            RebuildSizedResources();
        }

        private void RebuildSizedResources()
        {
            _workRtvA?.Dispose();
            _workSrvA?.Dispose();
            _workTexA?.Dispose();
            _workTexA = CreateTexture();
            _workSrvA = _device.CreateShaderResourceView(_workTexA);
            _workRtvA = _device.CreateRenderTargetView(_workTexA);

            _workRtvB?.Dispose();
            _workSrvB?.Dispose();
            _workTexB?.Dispose();
            _workTexB = CreateTexture();
            _workSrvB = _device.CreateShaderResourceView(_workTexB);
            _workRtvB = _device.CreateRenderTargetView(_workTexB);

            var initialParams = new AntiAliasingParams
            {
                Dims = new Vector4(_width, _height, 1f / Math.Max(_width, 1), 1f / Math.Max(_height, 1)),
                SrcRect = new Vector4(0f, 0f, 1f, 1f),
            };
            _context.UpdateSubresource(ref initialParams, _cbuffer!);
            _tsaaSeeded = false;
        }

        private ID3D11Texture2D CreateTexture()
        {
            return _device.CreateTexture2D(new Texture2DDescription
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

        private void DrawPass(ID3D11PixelShader ps, ID3D11RenderTargetView target, ID3D11ShaderResourceView? t0, ID3D11ShaderResourceView? t1 = null)
        {
            _context.PSSetShaderResources(0, _nullSrvs);
            _context.OMSetRenderTargets(target);
            _context.PSSetShader(ps);
            _context.PSSetShaderResource(0, t0);
            if (t1 != null) _context.PSSetShaderResource(1, t1);
            _context.Draw(3, 0);
        }

        /// <summary>
        /// Renders the selected AA technique from <paramref name="input"/> into
        /// <paramref name="output"/>, at the compositor's current display size. Call
        /// <see cref="EnsureSize"/> first if the size may have changed since the last frame.
        /// </summary>
        public void Render(int method, ID3D11ShaderResourceView input, ID3D11RenderTargetView output)
        {
            _context.VSSetShader(_vs);
            _context.PSSetConstantBuffer(0, _cbuffer);
            _context.PSSetSampler(0, _sampler);
            _context.IASetInputLayout(null);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.RSSetViewport(new Viewport(0, 0, _width, _height, 0, 1));

            if (method != _lastMethodRendered)
            {
                _lastMethodRendered = method;
                _tsaaSeeded = false;
                App.Logger.WriteLine(LOG_IDENT, "Method active: " + AntiAliasingSettings.MethodNames[method]);
            }

            switch (method)
            {
                case 1:
                    DrawPass(_psFxaa!, output, input);
                    break;
                case 2:
                    DrawPass(_psFxaaUltra!, output, input);
                    break;
                case 3:
                case 4:
                    DrawPass(_psSmaaEdge!, _workRtvA!, input);
                    DrawPass(method == 4 ? _psSmaaWeightsUltra! : _psSmaaWeights!, _workRtvB!, _workSrvA);
                    DrawPass(_psSmaaBlend!, output, input, _workSrvB);
                    break;
                case 5:
                    DrawPass(_psDlaaMask!, _workRtvA!, input);
                    DrawPass(_psDlaa!, output, _workSrvA);
                    break;
                case 6:
                    DrawPass(_psNfaa!, output, input);
                    break;
                case 7:
                    var histSrv = _tsaaHistory == 0 ? _workSrvA : _workSrvB;
                    var histRtv = _tsaaHistory == 0 ? _workRtvA : _workRtvB;
                    var nextSrv = _tsaaHistory == 0 ? _workSrvB : _workSrvA;
                    var nextRtv = _tsaaHistory == 0 ? _workRtvB : _workRtvA;
                    if (!_tsaaSeeded)
                    {
                        DrawPass(_psPass!, histRtv!, input);
                        _tsaaSeeded = true;
                    }
                    DrawPass(_psTsaa!, nextRtv!, input, histSrv);
                    DrawPass(_psTsaaSharpen!, output, nextSrv);
                    _tsaaHistory ^= 1;
                    break;
                default:
                    DrawPass(_psPass!, output, input);
                    break;
            }
            _context.PSSetShaderResources(0, _nullSrvs);
        }

        public void Dispose()
        {
            _workRtvA?.Dispose();
            _workSrvA?.Dispose();
            _workTexA?.Dispose();
            _workRtvB?.Dispose();
            _workSrvB?.Dispose();
            _workTexB?.Dispose();
            _cbuffer?.Dispose();
            _sampler?.Dispose();
            _psPass?.Dispose();
            _psFxaa?.Dispose();
            _psFxaaUltra?.Dispose();
            _psSmaaEdge?.Dispose();
            _psSmaaWeights?.Dispose();
            _psSmaaWeightsUltra?.Dispose();
            _psSmaaBlend?.Dispose();
            _psDlaaMask?.Dispose();
            _psDlaa?.Dispose();
            _psNfaa?.Dispose();
            _psTsaa?.Dispose();
            _psTsaaSharpen?.Dispose();
            _vs?.Dispose();
        }
    }
}
