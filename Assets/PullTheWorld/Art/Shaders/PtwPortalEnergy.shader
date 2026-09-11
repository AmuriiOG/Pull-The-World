// The doorway's interior, after the mockups: a golden field that is cream at the bottom and
// deepens to amber at the top, with a delicate MANDALA drawn over it - thin concentric rings and
// radial spokes around a bright core - and a soft pale rim where the light meets the frame.
//
// Alpha-blended, not additive. Additive light over a bright pastel sky can only go whiter,
// whatever colour it is given; that is how the first doorway became a blown-out white oval. This
// paints the gold, keeps the field under the bloom threshold, and lets only the core bloom - the
// warm spill around the arch is the additive halo quad's job.
//
// Mapped from OBJECT space via _Center/_Extents (the fill mesh is a procedural rectangle-plus-fan
// with no clean UVs). Ring and spoke geometry is in object units so the rings are round on screen.
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
        _LineStrength("Line Strength", Range(0, 1)) = 0.55
        _CoreSize    ("Core Radius (units)", Range(0.01, 0.3)) = 0.055
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _EdgeColor, _LineColor;
                float4 _Center, _Extents, _StarOffset;
                float  _Intensity, _RingSpacing, _RingCount, _Spokes, _LineWidth, _LineStrength, _CoreSize, _Pulse;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.units = IN.positionOS.xy - _Center.xy;
                OUT.local = OUT.units / max(float2(0.001, 0.001), _Extents.xy);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.local;
                float pulse = 1.0 + sin(_Time.y * 1.3) * _Pulse;

                // The field: cream low down, amber at the top, paler again along the frame.
                float v = saturate(p.y * 0.5 + 0.5);
                half3 col = lerp(_CoreColor.rgb, _EdgeColor.rgb, smoothstep(0.12, 0.95, v));
                float rim = smoothstep(0.72, 1.0, length(p));
                col = lerp(col, half3(1.0, 0.96, 0.88), rim * 0.18);
                // Light pooling at the threshold.
                col = lerp(col, half3(1.0, 0.94, 0.80), smoothstep(-0.55, -1.0, p.y) * 0.22);

                // The mandala, in object units round the core.
                float2 q = IN.units - _StarOffset.xy;
                float d = length(q);
                float reach = _RingSpacing * (_RingCount + 0.35);
                float fade = (1.0 - smoothstep(reach * 0.55, reach, d)) * pulse;

                float m = fmod(d, _RingSpacing);
                float toRing = min(m, _RingSpacing - m);
                float ring = 1.0 - smoothstep(_LineWidth * 0.5, _LineWidth * 1.4, toRing);
                ring *= step(_RingSpacing * 0.5, d);                       // no ring inside the core

                float a = atan2(q.y, q.x) + _Time.y * 0.035;                // spokes turn very slowly
                float sa = a * _Spokes / 6.2831853;
                float toSpoke = abs(frac(sa + 0.5) - 0.5) * (6.2831853 / max(1.0, _Spokes)) * d;
                float spoke = 1.0 - smoothstep(_LineWidth * 0.35, _LineWidth * 1.1, toSpoke);
                spoke *= smoothstep(_RingSpacing * 0.9, _RingSpacing * 1.6, d);

                float lines = saturate(ring * 0.9 + spoke * 0.5) * _LineStrength * fade;
                col = lerp(col, _LineColor.rgb, lines);

                // The core: a soft disc that is allowed to bloom, with a faint four-point glint.
                float core = exp(-(d * d) / (2.0 * _CoreSize * _CoreSize));
                float glint = pow(saturate(1.0 - abs(q.x) / (_CoreSize * 3.2)), 5.0) * pow(saturate(1.0 - abs(q.y) / (_CoreSize * 0.45)), 2.0)
                            + pow(saturate(1.0 - abs(q.y) / (_CoreSize * 3.2)), 5.0) * pow(saturate(1.0 - abs(q.x) / (_CoreSize * 0.45)), 2.0);
                float hot = saturate(core * 1.25 + glint * 0.6) * pulse;
                col = lerp(col, half3(1.0, 0.99, 0.95), hot);
                col += half3(1.0, 0.96, 0.85) * core * 0.35 * pulse;

                return half4(col * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
