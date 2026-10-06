// Experimental "alien outlines" (main menu > Testing): a faint glow around a player in their team colour, so enemies
// are easier to spot. An inverted hull: the model drawn again slightly fattened along its normals, back faces only,
// blended additively so it reads as a soft flare round the edges.
Shader "RockGame/AlienOutline"
{
    Properties
    {
        _Color ("Colour (alpha = strength)", Color) = (1,1,1,0.3)
        _Width ("Width (metres)", Float) = 0.03
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 wn = normalize(TransformObjectToWorldNormal(v.normalOS));
                // the same look near and far: the width is what it is at 6 m, thinner up close (so it hugs the body
                // instead of puffing out round it) and wider further off (so it still shows), in step with the distance
                float dist = length(ws - _WorldSpaceCameraPos);
                o.positionCS = TransformWorldToHClip(ws + wn * _Width * clamp(dist / 6.0, 0.3, 8.0));
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
