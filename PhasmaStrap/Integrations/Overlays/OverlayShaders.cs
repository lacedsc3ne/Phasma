namespace PhasmaStrap.Integrations.Overlays
{
    /// <summary>
    /// Minimal HLSL used by OverlayCompositor: a full-screen triangle vertex shader plus
    /// three pixel shaders (opaque pass-through blit, straight-alpha overlay blit for
    /// HUD/crosshair, and a crop+sRGB-encode blit used when desktop duplication hands
    /// back a non-8bpc-sRGB backbuffer format). This is a trimmed-down stand-in for
    /// Voidstrap's FrameGenShaders.cs, which additionally carried bicubic sampling,
    /// homepage-background composition and luma-difference passes needed only by
    /// RiShade/Frame Generation - none of which are part of this port.
    /// </summary>
    internal static class OverlayShaders
    {
        public const string Source = @"
struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

VSOut VSMain(uint id : SV_VertexID)
{
    VSOut o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return o;
}

cbuffer OverlayParams : register(b0)
{
    float4 dims;
    float4 srcRect;
};

Texture2D tex0 : register(t0);
SamplerState smp : register(s0);

float4 PSPass(VSOut inp) : SV_Target
{
    return float4(tex0.Sample(smp, inp.uv).rgb, 1.0);
}

float4 PSOverlay(VSOut inp) : SV_Target
{
    return tex0.Sample(smp, inp.uv);
}

float4 PSCropSrgb(VSOut inp) : SV_Target
{
    float3 c = tex0.Sample(smp, srcRect.xy + inp.uv * srcRect.zw).rgb;
    c = saturate(c);
    c = pow(c, 1.0 / 2.2);
    return float4(c, 1.0);
}
";
    }
}
