// Normal graphics: the textured ground (Plains and Highlands). Textures are laid on in world space (looking down), so
// it doesn't matter what UVs the ground mesh has: a fine detail texture (soil and blade speckles, repeating every few
// metres) over a big, slow colour-variation texture (lighter and darker patches of meadow) that hides the repeats.
Shader "RockGame/Ground"
{
    Properties
    {
        _Detail ("Detail (grey, 0.5 = no change)", 2D) = "grey" {}
        _Macro ("Colour variation", 2D) = "white" {}
        _DetailScale ("Detail repeats per metre", Float) = 0.22
        _MacroScale ("Colour repeats per metre", Float) = 0.011
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "GrassLight.hlsl"
        TEXTURE2D(_Detail); SAMPLER(sampler_Detail);
        TEXTURE2D(_Macro); SAMPLER(sampler_Macro);
        CBUFFER_START(UnityPerMaterial)
            float4 _Detail_ST;
            float4 _Macro_ST;
            float _DetailScale;
            float _MacroScale;
            half4 _Tint;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; half3 n : TEXCOORD1; float fog : TEXCOORD2; };
        Varyings vert(Attributes v)
        {
            Varyings o;
            o.ws = TransformObjectToWorld(v.positionOS.xyz);
            o.n = TransformObjectToWorldNormal(v.normalOS);
            o.positionCS = TransformWorldToHClip(o.ws);
            o.fog = ComputeFogFactor(o.positionCS.z);
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
            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.ws.xz;
                half3 macro = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, p * _MacroScale).rgb;
                // two layers of detail at different sizes and angles, so it never looks like a grid
                half d1 = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, p * _DetailScale).r;
                half d2 = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, float2(p.x * 0.6 - p.y * 0.8, p.x * 0.8 + p.y * 0.6) * _DetailScale * 0.37 + 0.31).r;
                half detail = (d1 * 0.65 + d2 * 0.35) * 2.0;
                half3 albedo = macro * detail * _Tint.rgb;
                half3 c = GrassShade(albedo, i.ws, normalize(i.n), i.positionCS, 0);
                return half4(MixFog(c, i.fog), 1);
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
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.n), 0); }
            ENDHLSL
        }
    }
}
