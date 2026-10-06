// Settings > Display > HANDS AND TOOLS (PostFx.cs, StylizePass): the first-person hands and what they hold, drawn in
// plain white into a mask, so the Stylize pass can give them looks of their own (outlines, cel banding, colour).
Shader "Hidden/RockGame/HandMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "HandMask"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return 1; }
            ENDHLSL
        }
    }
}
