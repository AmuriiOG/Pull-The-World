// Pastel dawn sky, after the art-direction mockups in Art/Mockup.
//
// Three-stop vertical gradient (peach at the top through blush to lilac at the horizon), a soft
// pale sun with a wide warm halo in the upper right, and drifting cloud puffs: layered value noise
// thresholded into soft-edged shapes, lit cream on top and blushed underneath, so the sky has the
// depth the mockups have without a single texture.
//
// Everything is derived from OBJECT space, not UVs. The backdrop quad is generated procedurally
// and its winding gets flipped to face the camera, which rotates the UVs - object space is
// immune to that. The quad spans -0.5..0.5 in both axes and is scaled to fill the frustum.
Shader "PTW/Backdrop"
{
    Properties
    {
        _TopColor        ("Top Colour", Color) = (0.97, 0.85, 0.77, 1)
        _MidColor        ("Middle Colour", Color) = (0.95, 0.83, 0.86, 1)
        _BottomColor     ("Bottom Colour", Color) = (0.90, 0.86, 0.91, 1)
        _MidPoint        ("Middle Point", Range(0.1, 0.9)) = 0.55

        _GlowColor       ("Sun Colour", Color) = (1, 0.95, 0.80, 1)
        _HaloColor       ("Halo Colour", Color) = (1, 0.84, 0.70, 1)
        _GlowCenter      ("Sun Position (xy)", Vector) = (0.24, 0.30, 0, 0)
        _SunRadius       ("Sun Disc Radius", Range(0.005, 0.2)) = 0.045
        _SunSoft         ("Sun Disc Softness", Range(0.001, 0.1)) = 0.02
        _GlowRadius      ("Sun Halo Radius", Range(0.05, 2)) = 0.55
        _GlowStrength    ("Sun Halo Strength", Range(0, 1)) = 0.30
        _GlowAspect      ("Sun Halo Aspect", Range(0.2, 3)) = 1.0

        _CloudColor      ("Cloud Colour", Color) = (0.99, 0.95, 0.92, 1)
        _CloudShade      ("Cloud Underside", Color) = (0.93, 0.80, 0.84, 1)
        _CloudStrength   ("Cloud Opacity", Range(0, 1)) = 0.85
        _CloudCover      ("Cloud Cover", Range(0, 1)) = 0.46
        _CloudScale      ("Cloud Scale", Range(0.5, 10)) = 2.2
        _CloudSpeed      ("Cloud Speed", Range(0, 0.3)) = 0.008
        _CloudBand       ("Cloud Band (bottom, top)", Vector) = (-0.5, 0.35, 0, 0)
        _EdgeDarken      ("Edge Darken", Range(0, 1)) = 0.0
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
                float4 _TopColor, _MidColor, _BottomColor;
                float  _MidPoint;
                float4 _GlowColor, _HaloColor, _GlowCenter;
                float  _SunRadius, _SunSoft, _GlowRadius, _GlowStrength, _GlowAspect;
                float4 _CloudColor, _CloudShade, _CloudBand;
                float  _CloudStrength, _CloudCover, _CloudScale, _CloudSpeed, _EdgeDarken;
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
                return vnoise(p) * 0.5 + vnoise(p * 2.03 + 7.1) * 0.27
                     + vnoise(p * 4.07 + 3.3) * 0.15 + vnoise(p * 8.1 + 1.7) * 0.08;
            }

            // Cloud puffs: fbm squashed horizontally, thresholded, softened. Returns coverage 0..1.
            float clouds(float2 p, float t, float scale, float offset)
            {
                float2 q = p * float2(scale, scale * 1.9) + float2(t, offset);
                float n = fbm(q);
                float cover = _CloudCover;
                return smoothstep(1.0 - cover, 1.0 - cover + 0.32, n);
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

                // Three-stop gradient.
                half3 col = v < _MidPoint
                    ? lerp(_BottomColor.rgb, _MidColor.rgb, smoothstep(0.0, 1.0, v / max(0.01, _MidPoint)))
                    : lerp(_MidColor.rgb, _TopColor.rgb, smoothstep(0.0, 1.0, (v - _MidPoint) / max(0.01, 1.0 - _MidPoint)));

                // Sun: a pale disc and a wide soft halo. The quad is portrait, so local y has to be
                // stretched by the screen aspect for the disc to be round on screen (distances are
                // in units of the quad's WIDTH).
                float2 g = p - _GlowCenter.xy;
                g.y *= _ScreenParams.y / max(1.0, _ScreenParams.x);
                g.x /= max(0.01, _GlowAspect);
                float dist = length(g);
                float halo = saturate(1.0 - dist / max(0.01, _GlowRadius));
                col = lerp(col, _HaloColor.rgb, (halo * halo) * _GlowStrength);
                float disc = 1.0 - smoothstep(_SunRadius - _SunSoft, _SunRadius + _SunSoft, dist);
                col = lerp(col, _GlowColor.rgb * 1.05, disc * 0.95);

                // Clouds: two drifting layers confined to a band, the far layer smaller and fainter.
                float t = _Time.y * _CloudSpeed;
                float band = smoothstep(_CloudBand.x, _CloudBand.x + 0.15, p.y)
                           * (1.0 - smoothstep(_CloudBand.y - 0.2, _CloudBand.y, p.y));
                float c1 = clouds(p, t, _CloudScale, 0.0) * band;
                float c2 = clouds(p + float2(0.13, 0.07), t * 0.6, _CloudScale * 1.7, 4.2) * band * 0.6;

                // Underside shading: sample the cloud a little higher; where the cloud above is
                // thicker than here, we are on its underside.
                float above = clouds(p + float2(0.0, 0.03), t, _CloudScale, 0.0) * band;
                float shade = saturate((above - c1) * 2.0);
                half3 cloudCol = lerp(_CloudColor.rgb, _CloudShade.rgb, shade);

                float cov = saturate(c1 + c2) * _CloudStrength;
                col = lerp(col, cloudCol, cov);

                // Gentle corner falloff if wanted (off by default: the mockup sky is even).
                float e = saturate(length(p * float2(1.05, 0.95)) * 1.55);
                col *= 1.0 - e * e * _EdgeDarken;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
