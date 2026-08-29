using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

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
    /// RiShade's shader post-processing pipeline as a stage that plugs into
    /// <see cref="Overlays.OverlayCompositor"/>'s single shared D3D11 device/context, rather than
    /// owning its own device/window/swapchain/capture. This is the render-pipeline half of the
    /// original standalone RiShadeOverlay.cs (CreatePipeline/CreateSizedResources/RenderPasses/
    /// DrawPass/LoadCustomEffects) with the window/device/composition/capture/message-loop half
    /// removed, since the compositor already owns all of that.
    /// </summary>
    internal sealed class RiShadeStage : IDisposable
    {
        private const string LOG_IDENT = "RiShade";

        private ID3D11Device _device = null!;
        private ID3D11DeviceContext _context = null!;

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
        private int _builtRenderScaleIndex = -1;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        private static readonly byte[] _shaderSourceBytes = System.Text.Encoding.ASCII.GetBytes(RiShadeShaders.Source);
        private static readonly ID3D11ShaderResourceView?[] _nullSrvs = new ID3D11ShaderResourceView?[4];

        public void CreatePipeline(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
        {
            _device = device;
            _context = context;
            _width = Math.Max(16, width);
            _height = Math.Max(16, height);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Vortice.D3DCompiler.Compiler.Compile(_shaderSourceBytes, "VSMain", "RiShade", "vs_5_0", out var vsBlob, out var vsErr);
            using (vsErr)
            {
                if (vsBlob == null)
                    throw new InvalidOperationException("RiShade vertex shader compile failed");
            }
            using (vsBlob)
            {
                _vs = _device.CreateVertexShader(vsBlob);
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
                SizeInBytes = Marshal.SizeOf<RiShadeParams>(),
                BindFlags = BindFlags.ConstantBuffer,
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None,
            });

            _passCbuffer = _device.CreateBuffer(new BufferDescription
            {
                SizeInBytes = Marshal.SizeOf<Vector4>(),
                BindFlags = BindFlags.ConstantBuffer,
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None,
            });

            LoadCustomEffects();
            RebuildSizedResources();
        }

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
                return _device.CreatePixelShader(blob);
            }
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
                            _customEffects.Add(_device.CreatePixelShader(blob));
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

        /// <summary>
        /// Called by the compositor whenever the Roblox window size (or the live render-scale
        /// setting) changes. No-ops if neither actually changed since the last call.
        /// </summary>
        public void EnsureSize(int width, int height)
        {
            width = Math.Max(16, width);
            height = Math.Max(16, height);
            int scaleIndex = App.Settings.Prop.RiShade.RenderScaleIndex;
            if (width == _width && height == _height && scaleIndex == _builtRenderScaleIndex)
                return;
            _width = width;
            _height = height;
            RebuildSizedResources();
        }

        private void RebuildSizedResources()
        {
            _builtRenderScaleIndex = App.Settings.Prop.RiShade.RenderScaleIndex;
            float renderScale = App.Settings.Prop.RiShade.ResolveRenderScale();
            _rw = Math.Max(64, (int)Math.Round(_width * renderScale));
            _rh = Math.Max(64, (int)Math.Round(_height * renderScale));

            for (int i = 0; i < _workTex.Length; i++)
            {
                _workRtv[i]?.Dispose();
                _workSrv[i]?.Dispose();
                _workTex[i]?.Dispose();
                var (w, h) = WorkTexSize(i);
                _workTex[i] = _device.CreateTexture2D(new Texture2DDescription
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
                _workSrv[i] = _device.CreateShaderResourceView(_workTex[i]!);
                _workRtv[i] = _device.CreateRenderTargetView(_workTex[i]!);
            }
            App.Logger.WriteLine(LOG_IDENT, $"Render resolution set to {_rw}x{_rh} (display {_width}x{_height})");
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
            _context.UpdateSubresource(ref v, _passCbuffer!);
        }

        private void SetVp(int w, int h)
        {
            _context.RSSetViewport(new Viewport(0, 0, w, h, 0, 1));
        }

        /// <summary>
        /// Renders RiShade's full effect chain from <paramref name="inputSrv"/> into
        /// <paramref name="dst"/>, at the compositor's current display size. Call
        /// <see cref="EnsureSize"/> first if the size may have changed since the last frame.
        /// </summary>
        public void Render(ID3D11ShaderResourceView inputSrv, ID3D11RenderTargetView dst)
        {
            var s = App.Settings.Prop.RiShade;

            _context.VSSetShader(_vs);
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
            _context.UpdateSubresource(ref p, _cbuffer!);
        }

        private void DrawPass(ID3D11PixelShader ps, ID3D11RenderTargetView target, ID3D11ShaderResourceView? t0, ID3D11ShaderResourceView? t1 = null, ID3D11ShaderResourceView? t2 = null, ID3D11ShaderResourceView? t3 = null)
        {
            _context.PSSetShaderResources(0, _nullSrvs);
            _context.OMSetRenderTargets(target);
            _context.PSSetShader(ps);
            _context.PSSetShaderResource(0, t0);
            if (t1 != null) _context.PSSetShaderResource(1, t1);
            if (t2 != null) _context.PSSetShaderResource(2, t2);
            if (t3 != null) _context.PSSetShaderResource(3, t3);
            _context.Draw(3, 0);
        }

        public void Dispose()
        {
            for (int i = 0; i < _workTex.Length; i++)
            {
                _workRtv[i]?.Dispose();
                _workSrv[i]?.Dispose();
                _workTex[i]?.Dispose();
            }
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
        }
    }
}
