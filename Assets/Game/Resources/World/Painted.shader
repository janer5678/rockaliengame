// Normal graphics: vertex-coloured, flat-shaded world meshes merged into a few big ones (castle walls, pine foliage,
// team flags, clouds). Colour = vertex colour (linear) x _Tint, lit like the grass and the ground (GrassLight.hlsl).
// _Wind picks how the mesh moves (all in the vertex shader, so it costs nothing on the CPU):
//   0 still;  1 pine foliage: sways a little in the same slow gusts as the grass, more towards the top
//   (uv0.x = how much this vertex sways);  2 a flag: the cloth waves out from the pole (uv0.x = 0 at the pole .. 1 at
//   the free end).  _Glow lifts the shadowed side (clouds).
Shader "RockGame/Painted"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _Wind ("Wind mode (0 still, 1 foliage, 2 flag)", Float) = 0
        _Glow ("Glow", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "../Grass/GrassLight.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            float _Wind;
            float _Glow;
            float _Cull;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv0 : TEXCOORD0;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 ws : TEXCOORD0;
            half3 n : TEXCOORD1;
            half3 color : TEXCOORD2;
        };

        // where this vertex is after the wind (world space), and its normal
        void Deform(Attributes v, out float3 ws, out float3 n)
        {
            float3 p = v.positionOS.xyz;
            float3 nrm = v.normalOS;
            float t = _Time.y;
            float3 root = TransformObjectToWorld(float3(0, 0, 0));
            if (_Wind > 1.5)
            {
                // flag: travelling waves along the cloth, bigger towards the free end
                float u = v.uv0.x;
                float ph = root.x * 0.37 + root.z * 0.23;
                float k = 5.5, w = 7.0;
                float a = 0.13 * u;
                float s = sin(t * w - p.x * k + ph), c = cos(t * w - p.x * k + ph);
                p.z += s * a + sin(t * 3.1 + p.y * 2.0 + ph) * 0.03 * u;
                p.y -= (1 - c) * 0.025 * u;
                // slope of the cloth along x -> tilt the normal
                float dzdx = -c * a * k + s * 0.13;
                nrm = normalize(float3(-dzdx * sign(nrm.z + 1e-4), 0, nrm.z));
            }
            ws = TransformObjectToWorld(p);
            n = TransformObjectToWorldNormal(nrm);
            if (_Wind > 0.5 && _Wind < 1.5)
            {
                // pine: the same slow gusts that roll over the grass, plus a gentle flutter
                float gust = sin(t * 0.9 + root.x * 0.06 + root.z * 0.045) * 0.5 + 0.5;
                float2 wind = float2(0.07, 0.035) * 2.2 * gust
                            + float2(sin(t * 1.3 + root.x * 0.35 + root.z * 0.2), cos(t * 1.1 + root.z * 0.3 - root.x * 0.13)) * 0.05;
                float sway = v.uv0.x;
                // a touch of flutter on the outer edges of the branches
                float flutter = sin(t * 3.7 + dot(ws, float3(1.3, 0.7, 1.1))) * 0.025 * v.uv0.y;
                ws.xz += wind * sway + flutter;
                ws.y += flutter * 0.5;
            }
        }

        Varyings vert(Attributes v)
        {
            Varyings o;
            float3 ws, n;
            Deform(v, ws, n);
            o.ws = ws;
            o.n = n;
            o.positionCS = TransformWorldToHClip(ws);
            o.color = v.color.rgb * _Tint.rgb;
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half3 n = normalize(i.n) * (front ? 1 : -1);
                half3 c = GrassShade(i.color, i.ws, n, i.positionCS, 0);
                c += i.color * _Glow * (1 - saturate(dot(n, _MainLightPosition.xyz)) );
                return half4(MixFog(c, ComputeFogFactor(i.positionCS.z)), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float4 vertShadow(Attributes v) : SV_POSITION
            {
                float3 ws, n;
                Deform(v, ws, n);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }
            half4 fragShadow() : SV_Target { return 0; }
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
            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target { return half4(NormalizeNormalPerPixel(i.n * (front ? 1 : -1)), 0); }
            ENDHLSL
        }
    }
}
