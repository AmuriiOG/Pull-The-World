// Animated energy field for the exit doorway.
//
// The door is the focal point of every screenshot, and a flat emissive plane was selling it
// short. This swirls, breathes and has a hot core, and being additive it blooms hard through the
// post stack without needing any extra geometry.
//
// Mapped from OBJECT space via _Center/_Extents rather than UVs, because the arch fill mesh is a
// procedural rectangle-plus-fan whose UVs are not a clean 0..1 rect.
Shader "PTW/PortalEnergy"
{
    Properties
    {
        _CoreColor  ("Core Colour", Color) = (1, 0.92, 0.62, 1)
        _EdgeColor  ("Edge Colour", Color) = (1, 0.60, 0.16, 1)
        _Intensity  ("Intensity", Range(0, 8)) = 2.4
        _Center     ("Local Centre (xy)", Vector) = (0, 0.72, 0, 0)
        _Extents    ("Local Extents (xy)", Vector) = (0.32, 0.62, 0, 0)
        _Speed      ("Swirl Speed", Range(0, 4)) = 0.9
        _Swirl      ("Swirl Arms", Range(0, 10)) = 3
        _RingFreq   ("Ring Frequency", Range(0, 24)) = 9
        _Pulse      ("Pulse Depth", Range(0, 1)) = 0.16
        _EdgeSoft   ("Edge Softness", Range(0.01, 1)) = 0.42
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PortalEnergy"
            // Alpha-blended, not additive. Additive light over a bright pastel sky can only go
            // whiter - the doorway was a blown-out white oval. Painting gold over the sky is the
            // only way it can read GOLDEN like the mockup; the halo quad around it stays additive.
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
                float2 local      : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _EdgeColor;
                float4 _Center;
                float4 _Extents;
                float  _Intensity;
                float  _Speed;
                float  _Swirl;
                float  _RingFreq;
                float  _Pulse;
                float  _EdgeSoft;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.local = (IN.positionOS.xy - _Center.xy) / max(float2(0.001, 0.001), _Extents.xy);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.local;
                float r = length(p);
                float a = atan2(p.y, p.x);
                float t = _Time.y * _Speed;

                // Two overlapping motions: spiral arms turning, and rings travelling inward.
                float arms  = sin(a * _Swirl + t * 2.0 + r * 5.0) * 0.5 + 0.5;
                float rings = sin(r * _RingFreq - t * 3.0) * 0.5 + 0.5;
                float swirl = lerp(0.72, 1.0, arms * 0.6 + rings * 0.4);

                // Soft falloff to the arch edge so it never shows a hard rim.
                float body = smoothstep(1.0, 1.0 - _EdgeSoft, r);

                float pulse = 1.0 + sin(_Time.y * 2.3) * _Pulse;
                float e = body * swirl * pulse;

                half3 col = lerp(_EdgeColor.rgb, _CoreColor.rgb, saturate((1.0 - r) * 1.5));
                return half4(col * _Intensity * e, saturate(e));
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
