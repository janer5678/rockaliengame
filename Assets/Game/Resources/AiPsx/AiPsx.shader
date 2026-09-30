// AI PSX TEST graphics mode: a PlayStation 1 style surface.
// - vertices snap to a coarse screen grid (the PS1's wobbly, jittery geometry)
// - textures are mapped affinely (no perspective correction, so they warp and swim) and point sampled
// - lighting is worked out per vertex (Gouraud) from the main light plus ambient
// - the final colour is cut down to 15 bits with a 4x4 ordered dither, like the PS1's framebuffer
Shader "RockGame/AiPsx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Tiling ("Projected UV tiling (repeats per metre)", Float) = 0.5
        _UVMode ("UVs: 0 mesh, 1 world projected, 2 object projected", Float) = 1
        _Cutoff ("Alpha cutoff", Float) = 0
        _Snap ("Vertex snap grid (columns)", Float) = 200
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _Tiling;
                float _UVMode;
                float _Cutoff;
                float _Snap;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uvw : TEXCOORD0;      // uv * w and w: divided back per pixel = affine mapping
                half3 light : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            static const float k_Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            // box projection: pick the plane the surface faces most
            float2 Project(float3 p, float3 n)
            {
                float3 a = abs(n);
                if (a.y >= a.x && a.y >= a.z) return p.xz;
                if (a.x >= a.z) return p.zy;
                return p.xy;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 wn = normalize(TransformObjectToWorldNormal(v.normalOS));
                float4 cs = TransformWorldToHClip(ws);
                // snap to a low-res grid in screen space
                float2 grid = float2(_Snap, _Snap * _ScreenParams.y / _ScreenParams.x);
                if (cs.w > 0.0001)
                    cs.xy = floor(cs.xy / cs.w * grid + 0.5) / grid * cs.w;
                o.positionCS = cs;

                float2 uv = v.uv;
                if (_UVMode > 1.5) uv = Project(v.positionOS.xyz, v.normalOS) * _Tiling;
                else if (_UVMode > 0.5) uv = Project(ws, wn) * _Tiling;
                o.uvw = float3(uv * cs.w, cs.w);

                Light l = GetMainLight();
                half ndl = saturate(dot(wn, l.direction));
                o.light = l.color * ndl * 0.9 + SampleSH(wn);
                o.fog = ComputeFogFactor(cs.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uvw.xy / max(i.uvw.z, 0.0001);
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                clip(t.a - _Cutoff - 0.001);
                half3 c = t.rgb * _Color.rgb * i.light;
                c = MixFog(c, i.fog);
                // 15-bit colour (5 bits a channel) with a 4x4 ordered dither
                uint2 p = (uint2)i.positionCS.xy % 4;
                float d = k_Bayer[p.y * 4 + p.x] / 16.0 - 0.5;
                c = floor(saturate(c) * 31.0 + d + 0.5) / 31.0;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
