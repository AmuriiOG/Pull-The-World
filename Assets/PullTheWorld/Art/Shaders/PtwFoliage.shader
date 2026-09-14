// Vegetation with wind.
//
// A soft wrapped-lambert lit surface (main light + ambient, no shadow receive) whose vertices sway
// in a slow sine driven by world position, weighted by height from a pivot so the base of a tuft
// stays planted and the tips move. _WeightSign is +1 for things that grow UP from their pivot
// (grass tufts, flowers) and -1 for things that hang DOWN from it (vines), so one shader serves
// both. Cheap enough for a hundred instances on a phone: one light, no textures. Takes the scene
// fog like URP Lit does, so the grass on a far level fades into the haze with its stone instead
// of staying a saturated green speck that gives the distance away.
Shader "PTW/Foliage"
{
    Properties
    {
        _BaseColor   ("Colour", Color) = (0.50, 0.70, 0.31, 1)
        _TipColor    ("Tip Colour", Color) = (0.62, 0.80, 0.40, 1)
        _WindAmount  ("Wind Amount", Range(0, 0.3)) = 0.05
        _WindSpeed   ("Wind Speed", Range(0, 5)) = 1.3
        _PivotY      ("Pivot Y (local)", Float) = 0
        _WeightSign  ("Weight Sign (+1 up, -1 down)", Range(-1, 1)) = 1
        _Length      ("Length for full weight", Range(0.05, 3)) = 0.4
        _Wrap        ("Light Wrap", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float  weight     : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _TipColor;
                float  _WindAmount, _WindSpeed, _PivotY, _WeightSign, _Length, _Wrap;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float w = saturate((IN.positionOS.y - _PivotY) * _WeightSign / max(0.01, _Length));
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float phase = posWS.x * 0.9 + posWS.y * 0.6 + posWS.z * 0.3;
                float sway = sin(_Time.y * _WindSpeed + phase) * 0.7
                           + sin(_Time.y * _WindSpeed * 2.3 + phase * 1.7) * 0.3;
                posWS.x += sway * _WindAmount * w * w;
                posWS.y += sway * _WindAmount * 0.25 * w * w * _WeightSign;

                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.weight = w;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                Light light = GetMainLight();
                // SafeNormalize, never normalize: a zero normal would become NaN, and one NaN pixel
                // is enough for bloom to paint a white disc the size of a block over the island.
                float3 n = SafeNormalize(IN.normalWS);
                // Two-sided: flip the normal to face the light if it points away (thin leaves).
                float ndl = dot(n, light.direction);
                ndl = abs(ndl) * 0.35 + max(ndl, 0.0) * 0.65;
                float lambert = saturate((ndl + _Wrap) / (1.0 + _Wrap));

                half3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, IN.weight);
                half3 ambient = SampleSH(n) * albedo;
                half3 lit = albedo * light.color * lambert;
                return half4(MixFog(ambient + lit, IN.fogFactor), 1.0);
            }
            ENDHLSL
        }

        // Depth-only so vegetation still occludes correctly in depth-based effects.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };

            V vertD(A IN)
            {
                V OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 fragD(V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
