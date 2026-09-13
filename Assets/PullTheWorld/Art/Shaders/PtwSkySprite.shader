// A painted sky sprite: the mountain ridges and cloud banks cut from the reference painting
// (Art/layered-background), drawn on camera-welded quads far behind the world.
//
// Unlit, alpha-blended, and deliberately WITHOUT scene fog: the layers stand 40-520 units from
// the lens, where the scene fog would tint the peach clouds lilac and flatten the ridges. The
// painting's own atmosphere is put back two ways instead, per layer: a HAZE that lerps the
// colour towards the local sky (the reference's lower ranges are all mist-washed), and a BASE
// FADE that dissolves the bottom of a ridge into the sky - the reconstructed pieces end in a
// straight horizontal base that the painting never shows, because every base there is lost in
// haze or a cloud bank. UVs come from object space (the quad spans -0.5..0.5) so the sprite is
// upright whichever way the generated quad's winding faces.
Shader "PTW/SkySprite"
{
    Properties
    {
        _BaseMap    ("Sprite", 2D) = "white" {}
        _BaseColor  ("Tint", Color) = (1, 1, 1, 1)
        _HazeColor  ("Haze Colour", Color) = (0.96, 0.87, 0.88, 1)
        _HazeAmount ("Haze Amount", Range(0, 1)) = 0
        _FadeStart  ("Base Fade Start (v)", Range(0, 1)) = 0
        _FadeEnd    ("Base Fade End (v)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SkySprite"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _HazeColor;
                float  _HazeAmount;
                float  _FadeStart, _FadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.positionOS.xy + 0.5;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                c.rgb = lerp(c.rgb, _HazeColor.rgb, _HazeAmount);
                if (_FadeEnd > _FadeStart)
                    c.a *= smoothstep(_FadeStart, _FadeEnd, IN.uv.y);
                return c;
            }
            ENDHLSL
        }
    }
}
