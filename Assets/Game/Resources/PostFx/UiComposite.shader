// Settings > Display > POST PROCESSING > "On the UI too" (UiLook.cs, UiPass): the menus / HUD, drawn by OnGUI into a
// screen-sized texture, laid over the camera's picture just before URP's post processing, so the post processing
// goes over the UI as well. IMGUI blends with SrcAlpha / OneMinusSrcAlpha onto a clear texture, which leaves the
// colour premultiplied by its alpha: so it goes on with One / OneMinusSrcAlpha.
Shader "Hidden/RockGame/UiComposite"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            Name "UiComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_RgUiTex);
            SAMPLER(sampler_RgUiTex);
            float _RgUiFlip;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(id);
                o.uv = GetFullScreenTriangleTexCoord(id);
                if (_RgUiFlip > 0.5) o.uv.y = 1 - o.uv.y;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D_LOD(_RgUiTex, sampler_RgUiTex, i.uv, 0);
            }
            ENDHLSL
        }
    }
}
