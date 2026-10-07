// Settings > Display > HANDS and TOOLS & WEAPONS (PostFx.cs, StylizePass): the first-person hands and what they hold,
// drawn into a mask so the Stylize pass can give each its own looks (outlines, cel banding, colour). The hands write 1,
// the tools / weapons 0.5 (_RgMaskValue: one material each). The mask has a depth buffer of its own, so where a hand and
// a tool overlap on screen the nearer one wins.
Shader "Hidden/RockGame/HandMask"
{
    Properties
    {
        _RgMaskValue ("Mask value", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite On ZTest LEqual Cull Off Blend Off
        Pass
        {
            Name "HandMask"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _RgMaskValue;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return _RgMaskValue; }
            ENDHLSL
        }
    }
}
