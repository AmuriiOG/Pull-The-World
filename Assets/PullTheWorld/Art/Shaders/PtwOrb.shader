// The player: an opalescent pearl, after the paintings.
//
// Not clear glass with a hard cyan rim (the first version), which read as a bubble outline with a
// flare in it. The painted orb is a milky, mostly opaque sphere whose hue drifts across its
// surface - pale cyan upper-left, pink to the right, mint below - with two glass highlights (a
// broad soft blob upper-right and a crescent along the lower-left edge), a soft luminous centre
// round the star, and only a thin, pastel, translucent edge. Everything here is view- and
// normal-based, so it holds up as the ball rolls (the visual is kept upright by OrbVisual).
//
// _Pulse and _Boost are driven per frame by OrbVisual: a slow breath while idle and a swell of
// rim brightness when the orb is moving fast or has just landed.
Shader "PTW/Orb"
{
    Properties
    {
        _BodyColor        ("Body (milk)", Color) = (0.92, 0.97, 1.0, 0.78)
        _TintCyan         ("Tint upper-left", Color) = (0.72, 0.93, 0.99, 1)
        _TintPink         ("Tint right", Color) = (0.96, 0.86, 0.95, 1)
        _TintMint         ("Tint below", Color) = (0.80, 0.97, 0.88, 1)
        _CentreGlow       ("Centre Glow", Range(0, 1)) = 0.35
        _RimColor         ("Rim Colour", Color) = (0.62, 0.91, 0.89, 1)
        _RimPower         ("Rim Power", Range(0.5, 8)) = 3.0
        _RimStrength      ("Rim Strength", Range(0, 4)) = 0.6
        _IridA            ("Iridescence A", Color) = (0.96, 0.75, 0.92, 1)
        _IridB            ("Iridescence B", Color) = (0.62, 0.94, 1.0, 1)
        _IridStrength     ("Iridescence Strength", Range(0, 2)) = 0.35
        _SpecDir          ("Highlight Direction", Vector) = (0.55, 0.65, -0.5, 0)
        _SpecPower        ("Highlight Power", Range(4, 200)) = 16
        _SpecStrength     ("Highlight Strength", Range(0, 2)) = 0.85
        _CrescentStrength ("Crescent Strength", Range(0, 2)) = 0.7
        _Pulse            ("Pulse (driven)", Range(0, 1)) = 0
        _Boost            ("Boost (driven)", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Orb"
            Blend SrcAlpha OneMinusSrcAlpha
            // Writes depth: the body is nearly opaque now, and the star / ring / bead are drawn after
            // it (queue +3) so the sphere's depth hides the ring's back half and anything behind.
            ZWrite On
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
                float4 _BodyColor, _TintCyan, _TintPink, _TintMint, _RimColor, _IridA, _IridB, _SpecDir;
                float  _CentreGlow, _RimPower, _RimStrength, _IridStrength, _SpecPower, _SpecStrength, _CrescentStrength;
                float  _Pulse, _Boost;
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
                float edge = 1.0 - ndv;
                float fres = pow(edge, _RimPower);
                float glow = 1.0 + _Pulse * 0.2 + _Boost;

                // Pearl body, tinted by where on the sphere this is.
                float wCyan = saturate(-n.x * 0.55 + n.y * 0.45 + 0.35);
                float wPink = saturate(n.x * 0.75 + 0.15);
                float wMint = saturate(-n.y * 0.9 - 0.05);
                half3 body = _BodyColor.rgb;
                body = lerp(body, _TintCyan.rgb, wCyan * 0.75);
                body = lerp(body, _TintPink.rgb, wPink * 0.70);
                body = lerp(body, _TintMint.rgb, wMint * 0.70);

                // Soft luminous centre round the star.
                body = lerp(body, half3(1.0, 1.0, 1.0), pow(ndv, 5.0) * _CentreGlow * (0.8 + 0.2 * _Pulse));

                // A quiet iridescent band inside the edge, hue sliding with the view.
                float band = smoothstep(0.25, 0.6, edge) * (1.0 - smoothstep(0.8, 1.0, edge));
                float hueT = frac(ndv * 2.2 + n.y * 0.35 + _Time.y * 0.05);
                half3 irid = lerp(_IridA.rgb, _IridB.rgb, abs(hueT * 2.0 - 1.0));
                body = lerp(body, irid, band * _IridStrength);

                // Thin pastel edge; swells with speed and on impact.
                half3 rim = _RimColor.rgb * fres * _RimStrength * glow;

                // The two glass highlights from the painting.
                float3 l = normalize(_SpecDir.xyz);
                float3 h = SafeNormalize(l + v);
                float spec = pow(saturate(dot(n, h)), _SpecPower) * _SpecStrength;
                float3 cdir = normalize(float3(-0.75, -0.55, -0.35));
                float crescent = smoothstep(0.35, 0.75, edge) * pow(saturate(dot(n, cdir)), 3.0) * _CrescentStrength;

                half3 col = body + rim + (spec + crescent) * half3(1.0, 1.0, 1.0);
                float alpha = saturate(_BodyColor.a + fres * 0.6 + spec * 0.5 + crescent * 0.3);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
