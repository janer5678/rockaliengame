// Settings > Display > POST PROCESSING > EXTRA LOOKS, the ones URP doesn't have (PostFx.cs, StylizePass): one
// full-screen pass before URP's own post processing, only drawn while one of these is on.
//   Outlines: dark ink lines where the depth jumps (silhouettes) or the surface folds sharply (the normals the
//     renderer's SSAO already draws), a darker shade of the colour under them rather than black; they fade out with
//     distance so the far meadow doesn't turn to scribble. None on the see-through grass right round the camera
//     (the grass writes that into the normals' alpha: Grass.shader) - its dither pattern used to crawl with lines.
//   Distance haze: far things fade towards a pale version of the sky colour (never the sky itself).
//   Sharpen: a small unsharp mask (4 taps).
//   Cel banding: the brightness snapped to a few steps, the hue and saturation kept.
// Each one is skipped (a uniform branch) when it's off.
Shader "Hidden/RockGame/Stylize"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "Stylize"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _RgOutline;   // x darkness (0 = off), y thickness (px), z depth step (relative), w fold (1 - cos of the angle)
            float4 _RgHaze;      // x most haze (0 = off), y start (m), z distance until it's all there (m)
            float4 _RgHazeColor; // linear
            float4 _RgLook;      // x sharpen (0 = off), y cel banding steps (0 = off)

            half3 Col(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb; }
            float Eye(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }
            // the near grass round the camera says "no ink here" in the normals' alpha (everything else writes 0)
            float NoInk(float2 uv) { return saturate(SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, UnityStereoTransformScreenSpaceTex(uv)).a); }
            bool Sky(float raw)
            {
            #if UNITY_REVERSED_Z
                return raw <= 0.000001;
            #else
                return raw >= 0.999999;
            #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 texel = _ScreenSize.zw;
                half3 c = Col(uv);

                // sharpen: the colour minus a blur of its neighbours
                if (_RgLook.x > 0)
                {
                    half3 n = Col(uv + float2(0, texel.y)) + Col(uv - float2(0, texel.y)) + Col(uv + float2(texel.x, 0)) + Col(uv - float2(texel.x, 0));
                    c = max(0, c + (c * 4 - n) * _RgLook.x * 0.25);
                }

                float raw = SampleSceneDepth(uv);
                float d = LinearEyeDepth(raw, _ZBufferParams);

                // outlines: depth jumps and sharp folds across the diagonals (Roberts cross)
                if (_RgOutline.x > 0)
                {
                    float2 o = texel * _RgOutline.y;
                    float2 a = uv + float2(-o.x, -o.y), b = uv + float2(o.x, o.y), e = uv + float2(o.x, -o.y), f = uv + float2(-o.x, o.y);
                    float da = Eye(a), db = Eye(b), de = Eye(e), df = Eye(f);
                    float near = min(min(da, db), min(min(de, df), d));
                    float jump = max(abs(da - db), abs(de - df)) / max(near, 0.01);
                    float edge = smoothstep(_RgOutline.z, _RgOutline.z * 2.2, jump);
                    float3 na = SampleSceneNormals(a), nb = SampleSceneNormals(b), ne = SampleSceneNormals(e), nf = SampleSceneNormals(f);
                    float fold = max(1 - dot(na, nb), 1 - dot(ne, nf));
                    edge = max(edge, smoothstep(_RgOutline.w, _RgOutline.w + 0.35, fold) * (Sky(raw) ? 0 : 1));
                    // none on (or against) the faded grass right round the camera
                    edge *= 1 - max(max(max(NoInk(a), NoInk(b)), max(NoInk(e), NoInk(f))), NoInk(uv));
                    // fade out with distance (the nearest of the samples: a silhouette against the sky keeps its line)
                    edge *= 1 - smoothstep(45.0, 130.0, near);
                    c *= 1 - edge * _RgOutline.x * 0.78;
                }

                // distance haze (not on the sky)
                if (_RgHaze.x > 0 && !Sky(raw))
                {
                    float h = saturate((d - _RgHaze.y) / _RgHaze.z);
                    h = h * (2 - h) * _RgHaze.x;
                    c = lerp(c, _RgHazeColor.rgb, h);
                }

                // cel banding: brightness in a few steps (in gamma, so the steps look even), same hue
                if (_RgLook.y > 0)
                {
                    float l = max(0.0001, dot(c, float3(0.2126, 0.7152, 0.0722)));
                    float g = LinearToSRGB(saturate(l));
                    float q = (floor(g * _RgLook.y) + 0.5) / _RgLook.y;
                    // soft edges between the steps (no crawling single-pixel stairs)
                    float t = frac(g * _RgLook.y);
                    q += (smoothstep(0.88, 1.0, t)) / _RgLook.y * 0.5 - (1 - smoothstep(0.0, 0.12, t)) / _RgLook.y * 0.5;
                    c *= l < 1 ? SRGBToLinear(saturate(q)) / l : 1; // (the brightest bits - the sun, sparks - keep their glow)
                }
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
