// The first-person alien hands (ViewModel.Hands.cs, HandColorHook): a stylised, softly glossy dark blue skin.
//  - a soft two-tone wrap of the sun (with its shadows), plus a key light fixed to the camera (upper left, in front) so
//    the hands always have a lit side and a highlight whichever way you face;
//  - a gentle gradient: a touch brighter on top, deeper and more violet underneath;
//  - a soft specular sheen from both lights and a cool blue fresnel rim round the silhouette.
// _Ghost (the Invisibility Potion, your own hands only): a screen-door see-through (the middle of each part thinned out,
// its edges kept) with a brighter shimmering rim - clearly still your hands, clearly invisible.
Shader "RockGame/AlienSkin"
{
    Properties
    {
        [MainColor] _BaseColor ("Colour", Color) = (0.1, 0.17, 0.46, 1)
        _Smoothness ("Smoothness (unused, for Lit compatibility)", Float) = 0.45
        _RimColor ("Rim colour", Color) = (0.32, 0.6, 1, 1)
        _Shine ("Specular colour", Color) = (0.7, 0.82, 1, 1)
        _Ghost ("Ghost (0 off, 1 on)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Smoothness;
            half4 _RimColor;
            half4 _Shine;
            float _Ghost;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 ws : TEXCOORD0;
            half3 n : TEXCOORD1;
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.ws = TransformObjectToWorld(v.positionOS.xyz);
            o.n = TransformObjectToWorldNormal(v.normalOS);
            o.positionCS = TransformWorldToHClip(o.ws);
            return o;
        }

        // the ghost's screen door: keeps a part's edges (high fresnel) and thins out its middle
        void GhostClip(float4 positionCS, half fresnel)
        {
            if (_Ghost < 0.5) return;
            uint2 p = (uint2)positionCS.xy % 4;
            static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            float th = (bayer[p.y * 4 + p.x] + 0.5) / 16.0;
            clip(0.5 + fresnel * 1.2 - th);
        }
        half Fresnel(float3 ws, half3 n) { return 1 - saturate(dot(n, normalize(_WorldSpaceCameraPos - ws))); }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.n);
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                half fr = 1 - saturate(dot(n, v));
                GhostClip(i.positionCS, fr * fr);

                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws), i.ws, half4(1, 1, 1, 1));
                // the gradient: brighter on top, deeper and a little violet underneath
                half up = n.y * 0.5 + 0.5;
                half3 albedo = _BaseColor.rgb * lerp(half3(0.72, 0.66, 1.0), half3(1.2, 1.2, 1.15), up);
                // the sun: a soft two-tone wrap
                half sun = smoothstep(-0.3, 0.6, dot(n, l.direction)) * l.shadowAttenuation;
                // the key light that rides with the camera (upper left, in front)
                half3 kd = normalize(mul((float3x3)UNITY_MATRIX_I_V, float3(-0.35, 0.55, 0.75)));
                half key = smoothstep(-0.25, 0.85, dot(n, kd));
                half3 amb = SampleSH(n);
                half3 c = albedo * (amb * 1.1 + l.color * sun * 0.8 + key * 0.45);
                // a soft sheen from both
                half s1 = pow(saturate(dot(n, normalize(l.direction + v))), 56) * sun;
                half s2 = pow(saturate(dot(n, normalize(kd + v))), 36);
                c += _Shine.rgb * (s1 * 0.5 * l.color + smoothstep(0.08, 0.7, s2) * 0.28);
                // the fresnel rim
                half rim = fr * fr * fr;
                if (_Ghost > 0.5)
                {
                    rim = fr * fr * (2.2 + 0.6 * sin(_Time.y * 5.0 + i.ws.y * 60.0));
                    c = lerp(c, _RimColor.rgb * 0.35, 0.35);
                }
                c += _RimColor.rgb * rim * (0.5 + 0.4 * key);
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half frag(Varyings i) : SV_Target
            {
                half fr = Fresnel(i.ws, normalize(i.n));
                GhostClip(i.positionCS, fr * fr);
                return i.positionCS.z;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.n);
                GhostClip(i.positionCS, Fresnel(i.ws, n) * Fresnel(i.ws, n));
                return half4(NormalizeNormalPerPixel(n), 0);
            }
            ENDHLSL
        }
    }
}
