// The player: a glass orb.
//
// Not a sphere with an emissive material. The look is built from view-dependence: the body is
// nearly clear at the centre and thickens to a cyan rim at grazing angles (fresnel), a second,
// tighter fresnel band carries an iridescent pink-to-cyan tint that shifts with the view, a fixed
// upper-left specular lobe sells the glass, and a faint inner haze keeps the centre from reading
// as a hole. Alpha follows the rim so the star core inside stays visible. Front faces only.
//
// _Pulse and _Boost are driven per frame by OrbVisual: a slow breath while idle and a swell of
// rim brightness when the orb is moving fast or has just landed.
Shader "PTW/Orb"
{
    Properties
    {
        _BodyColor    ("Body Tint", Color) = (0.86, 0.97, 1.0, 0.16)
        _RimColor     ("Rim Colour", Color) = (0.50, 0.90, 1.0, 1)
        _RimPower     ("Rim Power", Range(0.5, 8)) = 2.8
        _RimStrength  ("Rim Strength", Range(0, 4)) = 1.6
        _IridA        ("Iridescence A", Color) = (0.98, 0.72, 0.92, 1)
        _IridB        ("Iridescence B", Color) = (0.62, 0.95, 1.0, 1)
        _IridStrength ("Iridescence Strength", Range(0, 2)) = 0.55
        _SpecDir      ("Specular Direction", Vector) = (-0.55, 0.7, -0.45, 0)
        _SpecPower    ("Specular Power", Range(4, 200)) = 60
        _SpecStrength ("Specular Strength", Range(0, 2)) = 0.8
        _HazeColor    ("Inner Haze", Color) = (0.80, 0.95, 1.0, 1)
        _HazeStrength ("Inner Haze Strength", Range(0, 1)) = 0.12
        _Pulse        ("Pulse (driven)", Range(0, 1)) = 0
        _Boost        ("Boost (driven)", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Orb"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BodyColor, _RimColor, _IridA, _IridB, _SpecDir, _HazeColor;
                float  _RimPower, _RimStrength, _IridStrength, _SpecPower, _SpecStrength;
                float  _HazeStrength, _Pulse, _Boost;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewWS = GetWorldSpaceNormalizeViewDir(posWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = SafeNormalize(IN.normalWS);
                float3 v = SafeNormalize(IN.viewWS);
                float ndv = saturate(dot(n, v));
                float fres = pow(1.0 - ndv, _RimPower);

                float glow = 1.0 + _Pulse * 0.25 + _Boost;

                // Rim: the cyan edge that says "glass with light in it".
                half3 rim = _RimColor.rgb * fres * _RimStrength * glow;

                // Iridescence: a band just inside the rim whose hue slides with the view angle.
                float band = smoothstep(0.15, 0.55, 1.0 - ndv) * (1.0 - smoothstep(0.75, 1.0, 1.0 - ndv));
                float hueT = frac(ndv * 2.2 + n.y * 0.35 + _Time.y * 0.05);
                half3 irid = lerp(_IridA.rgb, _IridB.rgb, abs(hueT * 2.0 - 1.0)) * band * _IridStrength;

                // Fixed specular lobe from the upper left.
                float3 l = normalize(_SpecDir.xyz);
                float3 h = SafeNormalize(l + v);
                float spec = pow(saturate(dot(n, h)), _SpecPower) * _SpecStrength;

                // Inner haze so the middle is not a perfect hole.
                half3 haze = _HazeColor.rgb * _HazeStrength * (1.0 - fres) * (0.6 + 0.4 * _Pulse);

                half3 col = _BodyColor.rgb * _BodyColor.a + rim + irid + haze + spec;
                float alpha = saturate(_BodyColor.a + fres * 0.85 + band * 0.25 * _IridStrength + spec + haze.g * 0.5);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
