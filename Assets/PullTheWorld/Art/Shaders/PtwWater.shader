// Stylised but physically-cued water.
//
// The first version was a flat blue plane with tiny waves and it read as painted card. What
// actually makes a surface look like water is four things, and this does all four:
//
//   1. DEPTH. Sampling the scene depth buffer gives how much water sits between the surface and
//      whatever is underneath, so it shades shallow-to-deep for real and draws a foam line
//      exactly where geometry pierces the surface. This is the single biggest cue.
//   2. REFRACTION. Sampling the opaque colour texture with a normal-driven offset makes what is
//      under the water visibly wobble. Nothing says "liquid" faster.
//   3. ANIMATED NORMALS. Per-pixel normals from the wave field drive a real specular highlight,
//      so the surface glitters and rolls instead of sliding a texture.
//   4. FRESNEL. Water is nearly a mirror at grazing angles and clear looking straight down.
//
// Requires Depth Texture and Opaque Texture on the URP asset (PtwScene.ConfigureUrp sets both).
Shader "PTW/Water"
{
    Properties
    {
        _ShallowColor   ("Shallow Colour", Color) = (0.12, 0.61, 0.77, 1)
        _DeepColor      ("Deep Colour", Color) = (0.04, 0.24, 0.42, 1)
        _FoamColor      ("Foam Colour", Color) = (0.85, 0.97, 1.0, 1)
        _SpecColor2     ("Specular Colour", Color) = (1, 1, 1, 1)

        _DepthFade      ("Depth Fade (m)", Range(0.05, 6)) = 1.1
        _Opacity        ("Max Opacity", Range(0, 1)) = 0.86
        _RefractStrength("Refraction", Range(0, 0.12)) = 0.035

        _FoamDepth      ("Shore Foam Depth (m)", Range(0.01, 2)) = 0.28
        _FoamNoiseScale ("Foam Noise Scale", Range(1, 40)) = 12
        _FoamSpeed      ("Foam Speed", Range(0, 4)) = 0.9

        _WaveAmp        ("Wave Amplitude", Range(0, 0.4)) = 0.055
        _WaveFreq       ("Wave Frequency", Range(0, 20)) = 3.2
        _WaveSpeed      ("Wave Speed", Range(0, 6)) = 1.1
        _NormalStrength ("Normal Strength", Range(0, 4)) = 1.4

        _Gloss          ("Gloss", Range(4, 512)) = 120
        _SpecAmount     ("Specular Amount", Range(0, 4)) = 1.4
        _FresnelPower   ("Fresnel Power", Range(0.5, 8)) = 4
        _FresnelAmount  ("Fresnel Amount", Range(0, 1)) = 0.35

        _CrestAmount    ("Crest Highlight", Range(0, 0.6)) = 0.08

        _Tilt           ("Tilt 0..1 (script)", Range(0, 1)) = 0
        _TileOffset     ("Tile Offset in level (script)", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Water"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Receiving the island shadows on the ocean is a big part of reading the world as
            // a solid object floating above a static sea.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            // Scene fog, like URP Lit: the pools on a far level fade into the haze with its stone.
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
                float2 pattern     : TEXCOORD2;
                float2 local       : TEXCOORD3;
                float  fogFactor   : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float4 _SpecColor2;
                float4 _TileOffset;
                float  _DepthFade;
                float  _Opacity;
                float  _RefractStrength;
                float  _FoamDepth;
                float  _FoamNoiseScale;
                float  _FoamSpeed;
                float  _WaveAmp;
                float  _WaveFreq;
                float  _WaveSpeed;
                float  _NormalStrength;
                float  _Gloss;
                float  _SpecAmount;
                float  _FresnelPower;
                float  _FresnelAmount;
                float  _CrestAmount;
                float  _Tilt;
            CBUFFER_END

            // Sum of three crossing waves at incommensurate angles, so it never visibly loops.
            float Waves(float2 p, float t)
            {
                float w = sin(p.x * _WaveFreq + t * _WaveSpeed);
                w += sin((p.x * 0.7 + p.y * 1.3) * _WaveFreq * 0.83 - t * _WaveSpeed * 1.19);
                w += sin((p.y * 1.1 - p.x * 0.4) * _WaveFreq * 1.41 + t * _WaveSpeed * 0.71);
                return w / 3.0;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            // This game uses an ORTHOGRAPHIC camera, where LinearEyeDepth() and screenPos.w are
            // both meaningless - depth is linear in [near, far] and w is always 1. Getting this
            // wrong silently breaks the depth gradient, the shoreline foam and the refraction
            // rejection all at once, so both projections are handled explicitly.
            float SceneEyeDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        raw = 1.0 - raw;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 pos = IN.positionOS.xyz;
                float2 patternP = pos.xz + _TileOffset.xy;   // continuous across neighbouring tiles

                pos.y += Waves(patternP, _Time.y) * _WaveAmp;
                pos.y -= _Tilt * 0.3;                        // drains as the world is tipped

                OUT.positionWS = TransformObjectToWorld(pos);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.pattern = patternP;
                OUT.local = pos.xz;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float2 screenUV = IN.screenPos.xy / max(1e-5, IN.screenPos.w);

                // ---- animated per-pixel normal from the wave field ----
                const float e = 0.06;
                float h0 = Waves(IN.pattern, t);
                float hx = Waves(IN.pattern + float2(e, 0), t);
                float hz = Waves(IN.pattern + float2(0, e), t);
                float3 nOS = normalize(float3(-(hx - h0) / e * _NormalStrength * _WaveAmp, 1.0,
                                              -(hz - h0) / e * _NormalStrength * _WaveAmp));
                float3 N = normalize(TransformObjectToWorldNormal(nOS));
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // ---- how deep is the water here ----
                float sceneEye = SceneEyeDepth(screenUV);
                float surfaceEye = -TransformWorldToView(IN.positionWS).z;   // valid in both projections
                float waterDepth = max(0.0, sceneEye - surfaceEye);

                float depth01 = saturate(waterDepth / max(0.01, _DepthFade));
                half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);

                // ---- refraction: bend what is behind the surface ----
                float2 refractUV = screenUV + N.xz * _RefractStrength;
                // Reject the offset if it would pull in something IN FRONT of the water, which
                // would smear foreground geometry across the surface.
                if (SceneEyeDepth(refractUV) < surfaceEye) refractUV = screenUV;
                half3 behind = SampleSceneColor(refractUV);

                float bodyOpacity = saturate(depth01 * _Opacity);
                half3 col = lerp(behind, body, bodyOpacity);

                // ---- shoreline foam where geometry pierces the surface ----
                float foamEdge = 1.0 - saturate(waterDepth / max(0.001, _FoamDepth));
                float foamNoise = VNoise(IN.pattern * _FoamNoiseScale + float2(t * _FoamSpeed, -t * _FoamSpeed * 0.7));
                float foam = saturate(foamEdge * 1.25 - foamNoise * 0.45);
                foam = smoothstep(0.05, 0.5, foam);
                col = lerp(col, _FoamColor.rgb, foam);

                // ---- real specular from the main light + fresnel sheen ----
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float shade = lerp(0.66, 1.0, mainLight.shadowAttenuation);
                col *= shade;
                float3 H = normalize(mainLight.direction + V);
                float spec = pow(saturate(dot(N, H)), _Gloss) * _SpecAmount * mainLight.shadowAttenuation;
                col += _SpecColor2.rgb * mainLight.color * spec;

                float fres = pow(1.0 - saturate(dot(N, V)), _FresnelPower) * _FresnelAmount;
                col += _ShallowColor.rgb * fres;

                // Brighten the wave crests. On the open sea there is nothing underneath, so the
                // depth gradient is flat and the surface would otherwise be a dead wash - this is
                // what actually makes the swell visible, and a visible swell is the static
                // reference the moving world is read against.
                col += _FoamColor.rgb * saturate(h0) * _CrestAmount;

                float alpha = saturate(max(bodyOpacity, foam) + fres * 0.5);
                return half4(MixFog(col, IN.fogFactor), alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
