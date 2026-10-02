// Normal graphics: grass blades (and the flowers among them). Each blade's vertices are stored as offsets from its
// root; the root is put on the ground by reading the grass field texture (GrassField.cs: R height, G how much grass
// grows there, B tall wheat, A short grass for flower clearings). So one 8 m patch of blades, drawn instanced all
// round the camera (far away stretched over 16 or 32 m squares), follows the hills and stays off the bases. Far away
// there are fewer, wider blades (they shrink into the ground smoothly instead of popping). Blades sway in the wind,
// are pushed aside by players and things on the ground, and lie flat where something has walked (the trample map:
// when each spot was last stepped on; they stand back up over ~15 s). Tall wheat isn't flattened.
// Flowers (static meshes, rank < 0) skip the patch-only parts (wheat, thinning out, random gaps); their uv0.w says
// what they are (0 a daisy's middle, 1 daisy petals, 2 a lupin spike, 3 stems and leaves) for the colour settings.
// Blades right in front of the camera fade out (a dither pattern, so they stay opaque and sorted: the further from the
// eyes, the more of their pixels are drawn) - standing or crouching in the tall wheat, it doesn't cover your screen.
// It's only around this camera: everyone else still sees you hidden in it.
// The settings are globals set by GrassField.cs (no material properties).
Shader "RockGame/Grass"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "GrassLight.hlsl"
        TEXTURE2D(_GrassField); SAMPLER(sampler_GrassField);
        TEXTURE2D(_GrassTrample); SAMPLER(sampler_GrassTrample);
        float4 _GrassFieldRect;          // xy: world xz of texel (0,0)'s centre; zw: 1 / texture size in metres
        float4 _GrassTrampleRect;        // xy: world xz of the trample map's corner; zw: 1 / its size in metres
        float4 _GrassTrampleTime;        // x: now; y: stays flat this long (s); z: standing again by (s)
        float4 _GrassPush[16];           // xyz: something standing in the grass; w: radius
        float _GrassPushCount;
        float4 _GrassTiles[1024];        // where each drawn patch goes (x, z) and how much it's stretched (y: 1, 2 or 4);
        float _TileStart;                // the draw for one level of detail uses _GrassTiles[_TileStart + instance]
        float4 _GrassFade;               // x, y: blades fade out from / gone by (m); z, w: the same for flowers
        float4 _GrassWheat;              // wheat colour (linear)
        float _GrassNear;                // full density out to (m)
        float _GrassFalloff;             // how fast it thins out past that: density = (near / distance) ^ falloff
        float _GrassDensity;             // share of the blades drawn (Settings > Display)
        float4 _GrassTint;               // the grass colour picked in the settings, as a multiplier (linear)
        float4 _GrassDaisyTint;          // the flowers' colours picked in the settings (multipliers, linear)
        float4 _GrassLupinTint;
        float _GrassWind;
        float _GrassDebug;
        float4 _GrassNearFade;           // x, y: blades closer to the camera than x (m) are gone, further than y fully there

        struct Attributes
        {
            float4 positionOS : POSITION;   // offset from the root (x/z: across the blade, y: up)
            half4 color : COLOR;            // rgb: tip colour; a: 0 at the root .. 1 at the tip
            float4 uv0 : TEXCOORD0;         // xy: root (object x/z); z: rank (which ones go first far away); w: random
            float2 uv1 : TEXCOORD1;         // lean of the tip (x/z)
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 ws : TEXCOORD0;
            half3 color : TEXCOORD1;
            float fog : TEXCOORD2;
            half fade : TEXCOORD3;          // 0: right at the camera (not drawn) .. 1: drawn
        };

        float GrassHash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

        Varyings vert(Attributes v, uint iid : SV_InstanceID)
        {
            Varyings o;
            float3 root;
            float stretch = 1;
            bool decor = v.uv0.z < -0.5;
            if (decor) root = TransformObjectToWorld(float3(v.uv0.x, 0, v.uv0.y));
            else
            {
                float4 tile = _GrassTiles[(uint)_TileStart + iid];
                stretch = max(tile.y, 1);
                root = float3(v.uv0.x * stretch + tile.x, 0, v.uv0.y * stretch + tile.z);
            }
            float4 f = SAMPLE_TEXTURE2D_LOD(_GrassField, sampler_GrassField, (root.xz - _GrassFieldRect.xy) * _GrassFieldRect.zw, 0);
            root.y = f.r - 0.04;
            float dist = distance(root.xz, _WorldSpaceCameraPos.xz);
            float h = GrassHash(root.xz);
            float bend = v.color.a;
            half3 col = v.color.rgb;
            float hMul = 1, wMul = 1;
            float wheat = decor ? 0 : f.b;
            if (!decor)
            {
                col *= _GrassTint.rgb;                                   // the colour picked in the settings
                hMul = saturate((f.g - h * 0.85) * 7);                    // patchy edges where the grass stops
                hMul *= lerp(lerp(0.7, 1.3, frac(h * 7.13)), 1, wheat * 0.7); // taller and shorter bits
                hMul *= lerp(1, 4.6 * lerp(0.88, 1.12, v.uv0.w), wheat);  // tall wheat (head high: you can hide in it)...
                col = lerp(col, _GrassWheat.rgb * lerp(0.85, 1.15, v.uv0.w), wheat); // ...and golden
                hMul *= lerp(1, 0.3, f.a);                               // flower clearings are short
                float dens = (dist < _GrassNear ? 1 : pow(_GrassNear / dist, _GrassFalloff)) * _GrassDensity;
                hMul *= saturate((dens * stretch * stretch - v.uv0.z) * 12); // fewer blades far away...
                wMul = clamp(rsqrt(max(dens, 0.04)) * 0.8, 1, 2.6);      // ...but wider, so it still looks full
                wMul *= lerp(1, 1.5, wheat);
            }
            else
            {
                hMul = saturate((f.g - 0.5) * 8);
                float kind = v.uv0.w;
                col *= kind > 2.5 ? _GrassTint.rgb : kind > 1.5 ? _GrassLupinTint.rgb : kind > 0.5 ? _GrassDaisyTint.rgb : half3(1, 1, 1);
            }
            hMul *= 1 - (decor ? smoothstep(_GrassFade.z, _GrassFade.w, dist) : smoothstep(_GrassFade.x, _GrassFade.y, dist));

            float3 off = v.positionOS.xyz;
            off.xz *= wMul * saturate(hMul * 4);
            off.y *= hMul;
            float b2 = bend * bend;
            float2 lean = v.uv1 * b2 * hMul;
            // trampled: lying flat where something walked (each blade falls its own way), standing back up after a while
            float2 tuv = (root.xz - _GrassTrampleRect.xy) * _GrassTrampleRect.zw;
            float stamp = SAMPLE_TEXTURE2D_LOD(_GrassTrample, sampler_GrassTrample, tuv, 0).r;
            float flat = (1 - smoothstep(_GrassTrampleTime.y, _GrassTrampleTime.z, _GrassTrampleTime.x - stamp)) * (1 - wheat);
            if (_GrassTrampleTime.w > 0.5) col = lerp(half3(0, 0, 1), half3(1, 0, 0), flat); // (debug)
            if (flat > 0.001)
            {
                float fa = h * 6.2832 + v.uv0.w * 2.0;
                float2 fall = float2(cos(fa), sin(fa));
                float ang = flat * lerp(1.15, 1.45, frac(h * 13.7));
                float fy = off.y;
                off.y = fy * cos(ang);
                lean = lean * (1 - flat) + fall * fy * sin(ang);
            }
            // pushed aside (and down) by players, horses and things lying in the grass (tall wheat only parts a little)
            [loop] for (int k = 0; k < (int)_GrassPushCount; k++)
            {
                float4 p = _GrassPush[k];
                float2 d = root.xz - p.xz;
                float dl = length(d);
                if (dl < p.w && abs(root.y - p.y) < 2.5)
                {
                    float s = (1 - dl / p.w) * (1 - flat) * lerp(1, 0.35, wheat);
                    off.y *= 1 - s * 0.7;
                    lean += d / max(dl, 0.01) * s * 0.35 * b2 * hMul;
                }
            }
            // wind: slow gusts rolling over the field, and a quicker flutter
            float t = _Time.y;
            float gust = sin(t * 0.9 + root.x * 0.06 + root.z * 0.045) * 0.5 + 0.5;
            float2 wind = float2(sin(t * 1.9 + root.x * 0.35 + root.z * 0.2), cos(t * 1.5 + root.z * 0.3 - root.x * 0.13)) * 0.03
                        + float2(0.07, 0.035) * gust;
            lean += wind * b2 * hMul * _GrassWind * (1 - flat * 0.85);

            o.ws = root + off + float3(lean.x, 0, lean.y);
            o.positionCS = TransformWorldToHClip(o.ws);
            // dark at the root, bright at the tip; gusts lighten the tops a touch; flattened grass is a bit paler
            o.color = col * lerp(decor ? 0.6 : 0.42, 1.0, bend) * (1 + gust * 0.08 * b2) * lerp(half3(1, 1, 1), half3(1.18, 1.14, 0.82), flat); // (trampled: paler, drier)
            o.fog = ComputeFogFactor(o.positionCS.z);
            // close to the camera: fade out (measured with the height counted double, so the grass at your feet stays)
            float3 dc = o.ws - _WorldSpaceCameraPos;
            float dn = length(float3(dc.x, dc.y * 2, dc.z));
            o.fade = _GrassNearFade.y > _GrassNearFade.x ? smoothstep(_GrassNearFade.x, _GrassNearFade.y, dn) : 1;
            if (_GrassDebug > 0.5)
            {
                // debug: draw every blade full size, coloured by what the shader reads
                o.ws = root + v.positionOS.xyz;
                o.positionCS = TransformWorldToHClip(o.ws);
                o.color = _GrassDebug > 1.5 ? v.color.rgb : float3(frac(v.uv0.x / 8), frac(v.uv0.y / 8), v.uv0.w);
                o.fog = 0;
                o.fade = 1;
            }
            return o;
        }

        // screen-door transparency: a 4x4 ordered dither, so a blade at fade f keeps about f of its pixels
        void NearFadeClip(Varyings i)
        {
            if (i.fade >= 0.999) return;
            uint2 q = (uint2)i.positionCS.xy & 3;
            uint x = q.x ^ q.y;
            uint bayer = ((x & 1) * 2 + (q.y & 1)) * 4 + ((x >> 1) & 1) * 2 + ((q.y >> 1) & 1); // (the 4x4 Bayer matrix)
            clip(i.fade - (bayer + 0.5) / 16.0);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vertLit
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            // lit per vertex (blades are small, and there are many layers of them on screen, so per pixel costs a lot)
            Varyings vertLit(Attributes v, uint iid : SV_InstanceID)
            {
                Varyings o = vert(v, iid);
                o.color = GrassShade(o.color, o.ws, half3(0, 1, 0), o.positionCS, 0.35);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                NearFadeClip(i);
                return half4(MixFog(i.color, i.fog), 1);
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
            half frag(Varyings i) : SV_Target { NearFadeClip(i); return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target { NearFadeClip(i); return half4(0, 1, 0, 0); }
            ENDHLSL
        }
    }
}
