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
// Settings > Display > HANDS and TOOLS & WEAPONS: inside the hand mask (_RgHandMask: the first-person hands drawn 1,
// what they hold 0.5 - HandMask.shader) the outlines, cel banding, saturation and contrast are the hands' own (_RgHand*)
// or the tools' own (_RgTool*) - each only while that one's own look is on (x > 0), else the world's.
// An outline's darkness (x) can go past 1: saturate() then lets the darkest outlines reach black.
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
            float4 _RgFarLine;   // x the far things' line thickness (x the usual; 1 = as near), y how much silhouettes against the sky keep their line at any distance (0..1)

            float4 _RgHand;        // HANDS AND TOOLS (their own looks, inside the hand mask): x on, y cel banding steps (0 = off), z saturation, w contrast
            float4 _RgHandOutline; // the hands' outlines, as _RgOutline
            float4 _RgTool;        // TOOLS & WEAPONS (what the hands hold), as _RgHand
            float4 _RgToolOutline; // their outlines, as _RgOutline
            TEXTURE2D(_RgHandMask);
            float Hand(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_RgHandMask, sampler_PointClamp, uv, 0).r; }
            // which part of the mask a value is: 2 the hands, 1 a tool / weapon, 0 neither
            int Part(float m) { return m > 0.75 ? 2 : (m > 0.25 ? 1 : 0); }

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

            // how much of an ink line this pixel is, with the Roberts cross's samples o (uv) away along the diagonals
            float Edge(float2 uv, float2 o, float d, float raw, float2 ol)
            {
                float2 a = uv + float2(-o.x, -o.y), b = uv + float2(o.x, o.y), e = uv + float2(o.x, -o.y), f = uv + float2(-o.x, o.y);
                float ra = SampleSceneDepth(a), rb = SampleSceneDepth(b), re = SampleSceneDepth(e), rf = SampleSceneDepth(f);
                float da = LinearEyeDepth(ra, _ZBufferParams), db = LinearEyeDepth(rb, _ZBufferParams), de = LinearEyeDepth(re, _ZBufferParams), df = LinearEyeDepth(rf, _ZBufferParams);
                float near = min(min(da, db), min(min(de, df), d));
                float jump = max(abs(da - db), abs(de - df)) / max(near, 0.01);
                float edge = smoothstep(ol.x, ol.x * 2.2, jump);
                float3 na = SampleSceneNormals(a), nb = SampleSceneNormals(b), ne = SampleSceneNormals(e), nf = SampleSceneNormals(f);
                float fold = max(1 - dot(na, nb), 1 - dot(ne, nf));
                edge = max(edge, smoothstep(ol.y, ol.y + 0.35, fold) * (Sky(raw) ? 0 : 1));
                // none on (or against) the faded grass right round the camera
                edge *= 1 - max(max(max(NoInk(a), NoInk(b)), max(NoInk(e), NoInk(f))), NoInk(uv));
                // fade out with distance (the nearest of the samples) - except, with the far lines turned up past 100%
                // (_RgFarLine.y), an outline against the sky: a cloud's, a planet's or a far mountain's silhouette
                float fade = 1 - smoothstep(45.0, 130.0, near);
                if (_RgFarLine.y > 0)
                {
                    bool anySky = Sky(ra) || Sky(rb) || Sky(re) || Sky(rf) || Sky(raw);
                    bool allSky = Sky(ra) && Sky(rb) && Sky(re) && Sky(rf) && Sky(raw);
                    if (anySky && !allSky) fade = max(fade, _RgFarLine.y);
                }
                edge *= fade;
                return edge;
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

                // outlines: depth jumps and sharp folds across the diagonals (Roberts cross). The thickness (y, pixels,
                // already scaled with the screen height: PostFx.cs) can be a fraction of a pixel: the depth and normals
                // are read pixel by pixel, so the line is worked out at the whole pixel offsets either side of it and
                // blended between them - a line 1.5 px thick is the 1 px line with the 2 px one's extra ring half dark.
                // That keeps lines the same share of the screen at any resolution instead of snapping to whole pixels.
                // (the hands and tools - and the ring of pixels round them, where their silhouette line goes - use their own)
                int part = 0;
                float4 ol = _RgOutline;
                if (_RgHand.x > 0 || _RgTool.x > 0)
                {
                    part = Part(Hand(uv));
                    int linePart = part;
                    if (linePart == 0)
                    {
                        float2 ho = texel * max(1.0, ceil(max(_RgOutline.y, max(_RgHandOutline.y, _RgToolOutline.y))));
                        float ring = max(max(Hand(uv + ho), Hand(uv - ho)), max(Hand(uv + float2(ho.x, -ho.y)), Hand(uv + float2(-ho.x, ho.y))));
                        linePart = Part(ring);
                    }
                    if (linePart == 2 && _RgHand.x > 0) ol = _RgHandOutline;
                    else if (linePart == 1 && _RgTool.x > 0) ol = _RgToolOutline;
                }
                if (ol.x > 0)
                {
                    float t = ol.y;
                    // the far things - clouds, planets, far mountains, and the sky round them - get their own
                    // thickness (Settings > Display > Far line thickness: x 1 = the same as near)
                    t *= lerp(1.0, _RgFarLine.x, smoothstep(40.0, 110.0, d));
                    float k0 = floor(t), w = t - k0;
                    float edge = k0 >= 1 ? Edge(uv, texel * k0, d, raw, ol.zw) : 0;
                    if (w > 0.01) edge = lerp(edge, Edge(uv, texel * (k0 + 1), d, raw, ol.zw), w);
                    c *= 1 - saturate(edge * ol.x * 0.78);
                }

                // distance haze (not on the sky)
                if (_RgHaze.x > 0 && !Sky(raw) && part == 0)
                {
                    float h = saturate((d - _RgHaze.y) / _RgHaze.z);
                    h = h * (2 - h) * _RgHaze.x;
                    c = lerp(c, _RgHazeColor.rgb, h);
                }

                // cel banding: brightness in a few steps (in gamma, so the steps look even), same hue
                // (the hands and tools: their own number of steps)
                float4 own = part == 2 ? _RgHand : (part == 1 ? _RgTool : float4(0, 0, 1, 1));
                float steps = own.x > 0 ? own.y : _RgLook.y;
                if (steps > 0)
                {
                    float l = max(0.0001, dot(c, float3(0.2126, 0.7152, 0.0722)));
                    float g = LinearToSRGB(saturate(l));
                    float q = (floor(g * steps) + 0.5) / steps;
                    // soft edges between the steps (no crawling single-pixel stairs)
                    float t = frac(g * steps);
                    q += (smoothstep(0.88, 1.0, t)) / steps * 0.5 - (1 - smoothstep(0.0, 0.12, t)) / steps * 0.5;
                    c *= l < 1 ? SRGBToLinear(saturate(q)) / l : 1; // (the brightest bits - the sun, sparks - keep their glow)
                }

                // the hands' / tools' own saturation and contrast (in gamma, as drawn)
                if (own.x > 0 && (abs(own.z - 1) > 0.001 || abs(own.w - 1) > 0.001))
                {
                    float3 g3 = LinearToSRGB(saturate(c));
                    float l3 = dot(g3, float3(0.2126, 0.7152, 0.0722));
                    g3 = lerp(l3.xxx, g3, own.z);
                    g3 = saturate((g3 - 0.5) * own.w + 0.5);
                    c = SRGBToLinear(g3);
                }
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
