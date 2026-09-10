// Animated sky backdrop.
//
// Replaces the flat gradient texture with something that actually has depth: a vertical gradient,
// a soft glow bloom behind the play area so the islands sit in a pool of light, and very slow
// drifting cloud noise so the frame is never completely static.
//
// Everything is derived from OBJECT space, not UVs. The backdrop quad is generated procedurally
// and its winding gets flipped to face the camera, which rotates the UVs - object space is
// immune to that.
Shader "PTW/Backdrop"
{
    Properties
    {
        _TopColor        ("Top Colour", Color) = (0.78, 0.84, 0.90, 1)
        _BottomColor     ("Bottom Colour", Color) = (0.63, 0.70, 0.79, 1)
        _GlowColor       ("Glow Colour", Color) = (1, 1, 1, 1)
        _GlowStrength    ("Glow Strength", Range(0, 1)) = 0.30
        _GlowCenter      ("Glow Centre (xy)", Vector) = (0, 0.10, 0, 0)
        _GlowRadius      ("Glow Radius", Range(0.05, 2)) = 0.62
        _GlowAspect      ("Glow Aspect", Range(0.2, 3)) = 1.35
        _CloudStrength   ("Cloud Strength", Range(0, 0.4)) = 0.055
        _CloudScale      ("Cloud Scale", Range(0.5, 10)) = 2.6
        _CloudSpeed      ("Cloud Speed", Range(0, 0.3)) = 0.012
        _EdgeDarken      ("Edge Darken", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Backdrop"
            ZWrite On
            ZTest LEqual
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
                float4 _TopColor;
                float4 _BottomColor;
                float4 _GlowColor;
                float4 _GlowCenter;
                float  _GlowStrength;
                float  _GlowRadius;
                float  _GlowAspect;
                float  _CloudStrength;
                float  _CloudScale;
                float  _CloudSpeed;
                float  _EdgeDarken;
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
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                return vnoise(p) * 0.6 + vnoise(p * 2.03) * 0.3 + vnoise(p * 4.01) * 0.1;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.local = IN.positionOS.xy;      // quad spans -0.5 .. 0.5
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.local;                       // -0.5 .. 0.5
                float v = saturate(p.y + 0.5);             // 0 at bottom, 1 at top

                half3 col = lerp(_BottomColor.rgb, _TopColor.rgb, smoothstep(0.0, 1.0, v));

                // Pool of light behind the islands.
                float2 g = p - _GlowCenter.xy;
                g.x /= max(0.01, _GlowAspect);
                float glow = saturate(1.0 - length(g) / max(0.01, _GlowRadius));
                col += _GlowColor.rgb * (glow * glow) * _GlowStrength;

                // Slow drifting cloud banding, kept subtle enough to read as atmosphere.
                float t = _Time.y * _CloudSpeed;
                float n = fbm(p * _CloudScale + float2(t, t * 0.55));
                col += (n - 0.5) * _CloudStrength;

                // Gentle corner falloff so the frame reads as a lit stage.
                float e = saturate(length(p * float2(1.05, 0.95)) * 1.55);
                col *= 1.0 - e * e * _EdgeDarken;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
