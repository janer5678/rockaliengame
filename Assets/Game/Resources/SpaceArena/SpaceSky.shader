// The sudden death arena's sky (SpaceArena.cs): the low-poly stars, the galaxy, the planets and their glow.
// Unlit flat vertex colours (linear) times _Color; uv0.x / uv0.y / uv0.z: twinkle phase / how much it twinkles /
// how fast. With _SrcBlend / _DstBlend = One / One (and no depth writes) it's the additive glow behind the stars.
Shader "RockGame/SpaceSky"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float4 uv0 : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                half tw = 1 - v.uv0.y * (0.5 + 0.5 * sin(_Time.y * (1.1 + v.uv0.z) + v.uv0.x));
                o.color = v.color * _Color * tw;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return i.color; }
            ENDHLSL
        }
    }
}
