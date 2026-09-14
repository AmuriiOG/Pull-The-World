// The doorway's interior, after the mockups: a deep golden cavity with light coming out of it.
//
// Layered from the back: an amber-to-cream field that deepens into the upper corners (so the
// opening reads as a hollow, not a painted panel); two octaves of slowly drifting haze that give
// it body; a MANDALA of thin concentric rings and radial spokes; then, over all of that, a wide
// soft glow from the core that dissolves the rings where the light is strongest - which is what
// puts the rings INSIDE the light rather than on top of it - and finally the hot core itself with
// a faint four-point glint. The sill is paler where the light pools on the threshold.
//
// Alpha-blended, not additive. Additive light over a bright pastel sky can only go whiter,
// whatever colour it is given; that is how the first doorway became a blown-out white oval. Only
// the core is allowed past the bloom threshold; the spill round the arch is the halo quads' and
// the point light's job.
//
// Mapped from OBJECT space via _Center/_Extents (the fill mesh is a procedural rectangle-plus-fan
// with no clean UVs). Ring, spoke, glow and haze geometry is in object units so it is round on
// screen.
//
// Takes the scene fog: the doorway of the next level, 300-odd units out, dims into the haze with
// the rest of that island (and drops under the bloom threshold) instead of burning through it as
// the one bright, sharp thing in the distance.
Shader "PTW/PortalEnergy"
{
    Properties
    {
        _CoreColor   ("Field Colour, bottom / centre", Color) = (1, 0.90, 0.75, 1)
        _EdgeColor   ("Field Colour, top", Color) = (0.96, 0.72, 0.37, 1)
        _LineColor   ("Mandala Line Colour", Color) = (1, 0.97, 0.90, 1)
        _Intensity   ("Intensity", Range(0, 3)) = 1.0
        _Center      ("Local Centre (xy)", Vector) = (0, 0.75, 0, 0)
        _Extents     ("Local Extents (xy)", Vector) = (0.40, 0.73, 0, 0)
        _StarOffset  ("Core Offset from Centre (xy)", Vector) = (0, -0.09, 0, 0)
        _RingSpacing ("Ring Spacing (units)", Range(0.02, 0.5)) = 0.115
        _RingCount   ("Ring Count", Range(1, 12)) = 5
        _Spokes      ("Spokes", Range(0, 24)) = 12
        _LineWidth   ("Line Width (units)", Range(0.002, 0.03)) = 0.009
        _LineStrength("Line Strength", Range(0, 1)) = 0.42
        _CoreSize    ("Core Radius (units)", Range(0.01, 0.3)) = 0.06
        _GlowRadius  ("Glow Radius (units)", Range(0.05, 1.0)) = 0.30
        _Pulse       ("Pulse Depth", Range(0, 1)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PortalEnergy"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local      : TEXCOORD0;   // -1..1 across the opening
                float2 units      : TEXCOORD1;   // object units from the fill centre
                float  fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _EdgeColor, _LineColor;
                float4 _Center, _Extents, _StarOffset;
                float  _Intensity, _RingSpacing, _RingCount, _Spokes, _LineWidth, _LineStrength, _CoreSize, _GlowRadius, _Pulse;
            CBUFFER_END

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.units = IN.positionOS.xy - _Center.xy;
                OUT.local = OUT.units / max(float2(0.001, 0.001), _Extents.xy);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.local;
                float pulse = 1.0 + sin(_Time.y * 1.3) * _Pulse;
                float2 q = IN.units - _StarOffset.xy;
                float d = length(q);

                // 1. The field: cream low down, amber at the top, deeper still into the upper
                //    corners - the cavity - and a little paler along the sill where light pools.
                float v = saturate(p.y * 0.5 + 0.5);
                half3 col = lerp(_CoreColor.rgb, _EdgeColor.rgb, smoothstep(0.10, 0.95, v));
                float corner = smoothstep(0.55, 1.05, length(p * float2(1.0, 0.85))) * saturate(p.y + 0.3);
                col = lerp(col, _EdgeColor.rgb * 0.80, corner * 0.55);
                col = lerp(col, half3(1.0, 0.94, 0.80), smoothstep(-0.55, -1.0, p.y) * 0.16);

                // 2. Haze: two octaves of drifting value noise. Body, not paint.
                float n = vnoise(q * 3.1 + float2(_Time.y * 0.05, -_Time.y * 0.03)) * 0.65
                        + vnoise(q * 6.7 - float2(_Time.y * 0.04, _Time.y * 0.06)) * 0.35;
                col *= 0.93 + 0.14 * n;

                // 3. The mandala, soft-edged, thinning with distance and with the haze.
                float reach = _RingSpacing * (_RingCount + 0.35);
                float fade = (1.0 - smoothstep(reach * 0.5, reach, d)) * pulse;
                float m = fmod(d, _RingSpacing);
                float toRing = min(m, _RingSpacing - m);
                float ring = 1.0 - smoothstep(_LineWidth * 0.4, _LineWidth * 1.8, toRing);
                ring *= step(_RingSpacing * 0.5, d);
                float a = atan2(q.y, q.x) + _Time.y * 0.035;
                float sa = a * _Spokes / 6.2831853;
                float toSpoke = abs(frac(sa + 0.5) - 0.5) * (6.2831853 / max(1.0, _Spokes)) * d;
                float spoke = 1.0 - smoothstep(_LineWidth * 0.3, _LineWidth * 1.3, toSpoke);
                spoke *= smoothstep(_RingSpacing * 0.9, _RingSpacing * 1.6, d);
                float lines = saturate(ring * 0.85 + spoke * 0.45) * _LineStrength * fade * (0.75 + 0.5 * n);
                col = lerp(col, _LineColor.rgb, lines);

                // 4. The light: a wide soft glow through the haze that swallows the rings near the
                //    core, then the hot core with a faint four-point glint. The core alone may bloom.
                // The glow stays GOLDEN - only the small core goes white. A white glow this wide
                // read as a flat disc and swallowed the lower half of the field.
                float glow = exp(-(d * d) / (2.0 * _GlowRadius * _GlowRadius));
                col = lerp(col, half3(1.0, 0.93, 0.76), glow * 0.38 * pulse);
                float core = exp(-(d * d) / (2.0 * _CoreSize * _CoreSize));
                float glint = pow(saturate(1.0 - abs(q.x) / (_CoreSize * 3.4)), 5.0) * pow(saturate(1.0 - abs(q.y) / (_CoreSize * 0.45)), 2.0)
                            + pow(saturate(1.0 - abs(q.y) / (_CoreSize * 3.4)), 5.0) * pow(saturate(1.0 - abs(q.x) / (_CoreSize * 0.45)), 2.0);
                float hot = saturate(core * 1.2 + glint * 0.6) * pulse;
                col = lerp(col, half3(1.0, 0.99, 0.95), hot);
                col += half3(1.0, 0.95, 0.82) * (core * 0.50 + glow * 0.06) * pulse;

                return half4(MixFog(col * _Intensity, IN.fogFactor), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
