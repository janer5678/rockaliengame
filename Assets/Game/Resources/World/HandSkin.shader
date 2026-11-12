// The SMOOTH first-person hands (SmoothHand.cs builds the arm, HandColorHook colours it): matte, softly lit skin that
// reads as a solid shape from any side.
//  - the colour: the skin (the team's), lighter on the palm side, darker in the creases, a touch brighter on every
//    bump and darker in every hollow - all baked into the mesh's vertex colours (r: how open the point is, b: the palm
//    side, a: how the surface curves) - and a nail on the back of each fingertip (drawn here from where the point is
//    on the digit: uv, and g: how far it faces the back of the digit);
//  - the light: the sun with a soft wrap, the sky's ambient, the lights nearby, and a dim fill light that rides with
//    the camera (from its upper left), so the hands are never a black shape when the sun's behind you;
//  - the sun's shadow is read at a few points by the camera, not per pixel: the whole hand dims as you step into shade,
//    and there's never shadow acne or a stripe across a finger;
//  - a soft highlight and a faint lighter rim round the silhouette.
// No screen-space ambient occlusion on purpose (it crawls on small shapes right in front of the camera): the creases
// are the baked ones.
Shader "RockGame/HandSkin"
{
    Properties
    {
        [MainColor] _BaseColor ("Skin", Color) = (0.32, 0.5, 0.85, 1)
        [MainTexture] _BaseMap ("(none: so code that asks a material for its texture gets an answer)", 2D) = "white" {}
        _PalmColor ("Palm side", Color) = (0.5, 0.65, 0.9, 1)
        _CreaseColor ("Creases", Color) = (0.12, 0.2, 0.42, 1)
        _NailColor ("Nails", Color) = (0.1, 0.14, 0.3, 1)
        _RimColor ("Rim", Color) = (0.6, 0.78, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.4
        _Ambient ("Ambient", Range(0, 1.5)) = 0.78
        _Look ("x fill light, y rim, z highlight, w nails", Vector) = (0.3, 0.3, 0.22, 1)
        _Look2 ("x highlight tightness, y the nails' shine, z light wrap, w sun", Vector) = (14, 2, 0.38, 1)
        _Form ("Bumps brighter, hollows darker", Range(0, 0.6)) = 0.22
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _PalmColor;
            half4 _CreaseColor;
            half4 _NailColor;
            half4 _RimColor;
            float _Smoothness;
            float _Ambient;
            float4 _Look;
            float4 _Look2;
            float _Form;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 ws : TEXCOORD0;
            half3 n : TEXCOORD1;
            half4 color : TEXCOORD2;
            float2 uv : TEXCOORD3;
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.ws = TransformObjectToWorld(v.positionOS.xyz);
            o.n = TransformObjectToWorldNormal(v.normalOS);
            o.positionCS = TransformWorldToHClip(o.ws);
            o.color = v.color;
            o.uv = v.uv;
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            // the sun's shadow where the camera is: a few points in front of it, averaged
            half ViewShadow()
            {
                float3 cam = _WorldSpaceCameraPos;
                float3 fwd = -UNITY_MATRIX_V[2].xyz, right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                half s = MainLightRealtimeShadow(TransformWorldToShadowCoord(cam + fwd * 0.45));
                s += MainLightRealtimeShadow(TransformWorldToShadowCoord(cam + fwd * 0.35 + right * 0.3 - up * 0.25));
                s += MainLightRealtimeShadow(TransformWorldToShadowCoord(cam + fwd * 0.35 - right * 0.3 - up * 0.25));
                s += MainLightRealtimeShadow(TransformWorldToShadowCoord(cam - up * 0.5));
                return s * 0.25;
            }

            half3 Lit(half3 albedo, half3 n, float3 v, half3 dir, half3 col, half gloss)
            {
                half wrap = saturate(dot(n, dir) * (1.0 - _Look2.z) + _Look2.z);
                half3 c = albedo * col * (wrap * wrap);
                half nh = saturate(dot(n, normalize(dir + v)));
                c += col * (pow(nh, _Look2.x) * gloss * saturate(dot(n, dir) + 0.2));
                return c;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.n);
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                half ao = i.color.r, palm = i.color.b;

                // the nail: a rounded patch on the back of the last bone
                float2 q = float2(i.uv.x, i.uv.y * 2.0 - 1.0);
                float e = pow(abs(q.x), 3.0) + pow(abs(q.y), 3.0);
                half nail = (1 - smoothstep(1.0 - fwidth(e), 1.0 + fwidth(e), e)) * smoothstep(0.52, 0.66, i.color.g) * _Look.w;

                half3 albedo = lerp(_BaseColor.rgb, _PalmColor.rgb, palm);
                albedo = lerp(_CreaseColor.rgb, albedo, ao);
                albedo *= 1.0 + (i.color.a * 2.0 - 1.0) * _Form;
                albedo = lerp(albedo, _NailColor.rgb, nail);
                half gloss = _Look.z * (0.45 + _Smoothness) * lerp(1.0, _Look2.y, nail) * ao;

                Light l = GetMainLight();
                half shade = ViewShadow();
                half3 c = albedo * SampleSH(n) * (_Ambient * lerp(0.8, 1.0, ao));
                c += Lit(albedo, n, v, l.direction, l.color * (shade * _Look2.w), gloss);
                // the fill light that rides with the camera (from its upper left, a little in front of the hands)
                half3 kd = normalize(mul((float3x3)UNITY_MATRIX_I_V, float3(-0.5, 0.62, 0.6)));
                half fill = saturate(dot(n, kd) * 0.5 + 0.5);
                c += albedo * (fill * fill * _Look.x);
                c += pow(saturate(dot(n, normalize(kd + v))), _Look2.x) * gloss * 0.35;

            #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_FORWARD_PLUS
                for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                {
                    Light dl = GetAdditionalLight(li, i.ws);
                    c += Lit(albedo, n, v, dl.direction, dl.color * dl.distanceAttenuation, gloss);
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light al = GetAdditionalLight(lightIndex, i.ws);
                    c += Lit(albedo, n, v, al.direction, al.color * al.distanceAttenuation, gloss);
                LIGHT_LOOP_END
            #endif

                // a faint lighter rim, mostly on the side the sky's on
                half fr = 1 - saturate(dot(n, v));
                c += _RimColor.rgb * (fr * fr * fr * _Look.y * ao * (0.45 + 0.55 * saturate(n.y * 0.5 + 0.5)));
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
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(normalize(i.n)), 0); }
            ENDHLSL
        }
    }
}
