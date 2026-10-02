// Normal graphics: the low-poly sun (SkySun in WorldDressing.cs). Flat vertex colours (linear) x _Tint, alpha from
// the vertex colour (the soft halo and the rays fade out), no lighting and no fog. Drawn after the opaque world with
// the depth test on, so the mountains and the clouds pass in front of it.
Shader "RockGame/Sun"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Sun"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = half4(v.color.rgb * _Tint.rgb, v.color.a);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return i.color; }
            ENDHLSL
        }
    }
}
