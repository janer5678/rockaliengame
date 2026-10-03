// Settings > Display > POST PROCESSING > "On the UI too" (UiLook.cs, UiPass): the menus / HUD, drawn by OnGUI into a
// screen-sized texture, laid over the camera's picture - just before URP's post processing (so the world's post
// processing goes over the UI as well), or just after it (POST PROCESSING ON THE UI > World effects on the UI: off).
// IMGUI blends with SrcAlpha / OneMinusSrcAlpha onto a clear texture, which leaves the colour premultiplied by its
// alpha: so it goes on with One / OneMinusSrcAlpha.
// The UI's own looks, all off (and skipped) unless picked: saturation / contrast, cel shading (the colours snapped to a
// few flat steps), ink outlines round everything (on the darker side of each edge: round text on a panel too, and round
// the panels themselves) and a glow round the bright parts (added on, so it shows over the world as well).
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
            float4 _RgUiTexel; // 1/w, 1/h, w, h
            float4 _RgUiLook;  // colour steps (0 off), saturation, contrast, outline radius px (0 off)
            float4 _RgUiGlow;  // glow amount (0 off), glow radius px
            float4 _RgUiInk;   // outline colour (linear) and opacity

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

            // IMGUI blends the alpha channel with SrcAlpha / OneMinusSrcAlpha too, so a fill of alpha a on the clear
            // texture stores a * a (a 90% panel came out 81%: the world showed through the menus twice as much as
            // drawn straight to the screen). The colour is right (premultiplied); the square root gives the alpha
            // back exactly for one layer (and near enough under anything opaque on top, which stores ~1 anyway).
            float4 Ui(float2 uv)
            {
                float4 c = SAMPLE_TEXTURE2D_LOD(_RgUiTex, sampler_RgUiTex, uv, 0);
                c.a = sqrt(saturate(c.a));
                return c;
            }
            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            // saturation, contrast and cel steps on a premultiplied colour (worked on in gamma space, as drawn)
            float4 Grade(float4 c)
            {
                if (c.a < 0.002) return c;
                float3 g = pow(saturate(c.rgb / c.a), 1.0 / 2.2);
                float l = Luma(g);
                g = lerp(l.xxx, g, _RgUiLook.y);
                g = (g - 0.5) * _RgUiLook.z + 0.5;
                g = saturate(g);
                if (_RgUiLook.x > 0.5)
                {
                    float n = _RgUiLook.x;
                    g = floor(g * n + 0.5) / n;
                }
                return float4(pow(g, 2.2) * c.a, c.a);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float4 c = Ui(i.uv);
                bool graded = _RgUiLook.x > 0.5 || abs(_RgUiLook.y - 1) > 0.001 || abs(_RgUiLook.z - 1) > 0.001;
                if (graded) c = Grade(c);

                // ink outlines: on the outside of shapes (alpha steps) and on the darker side of bright text / icons
                if (_RgUiLook.w > 0.01)
                {
                    float r = _RgUiLook.w;
                    float maxA = 0, maxL = 0;
                    [loop] for (int k = 0; k < 16; k++)
                    {
                        float ang = k * (6.2831853 / 16.0);
                        float2 d = float2(cos(ang), sin(ang)) * _RgUiTexel.xy;
                        float4 a = Ui(i.uv + d * r);
                        float4 b = Ui(i.uv + d * (r * 0.5));
                        maxA = max(maxA, max(a.a, b.a));
                        maxL = max(maxL, max(Luma(a.rgb), Luma(b.rgb)));
                    }
                    // only where this pixel is clearly the background of what's next to it (a text stroke's own soft
                    // edge is left alone, so thin letters keep their shape)
                    float ea = saturate((maxA * 0.5 - c.a) / max(maxA * 0.5, 1e-3));
                    float el = saturate((maxL * 0.3 - Luma(c.rgb)) / max(maxL * 0.3, 1e-4)) * smoothstep(0.02, 0.12, maxL);
                    float ink = max(ea, el) * _RgUiInk.a;
                    // the ink over the UI (premultiplied "over")
                    c = float4(_RgUiInk.rgb * ink + c.rgb * (1 - ink), ink + c.a * (1 - ink));
                }

                // glow round the bright parts: a soft blur of what's bright, added on
                if (_RgUiGlow.x > 0.001)
                {
                    float3 sum = 0;
                    float wsum = 0;
                    // a disc of samples on a golden-angle spiral (smooth, no rings), nearer ones counting more
                    [loop] for (int j = 0; j < 40; j++)
                    {
                        float t = (j + 0.5) / 40.0;
                        float ang = j * 2.39996323;
                        float2 d = float2(cos(ang), sin(ang)) * _RgUiTexel.xy * (sqrt(t) * _RgUiGlow.y);
                        float3 a = Ui(i.uv + d).rgb;
                        float w = 1.0 - t * 0.7;
                        // (only what's bright counts: white text, the accent colour)
                        sum += a * smoothstep(0.25, 0.8, Luma(a)) * w;
                        wsum += w;
                    }
                    c.rgb += sum / wsum * (_RgUiGlow.x * 1.4);
                }
                return c;
            }
            ENDHLSL
        }
    }
}
