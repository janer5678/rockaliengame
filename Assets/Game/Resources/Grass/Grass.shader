// Normal graphics: grass blades (and the flowers among them). Each blade's vertices are stored as offsets from its
// root; the root is put on the ground by reading the grass field texture (GrassField.cs: R height, G how much grass
// grows there, B wheat, A short grass for flower clearings). So one 8 m patch of blades, drawn instanced all round
// the camera, follows the hills and stays off the bases. Far away there are fewer, wider blades (they shrink into
// the ground smoothly instead of popping). Blades sway in the wind and are pushed aside by players and things on the
// ground. Flowers (static meshes, rank < 0) skip the patch-only parts (wheat, thinning out, random gaps).
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
        float4 _GrassFieldRect;          // xy: world xz of texel (0,0)'s centre; zw: 1 / texture size in metres
        float4 _GrassPush[16];           // xyz: something standing in the grass; w: radius
        float _GrassPushCount;
        float4 _GrassTiles[400];         // where each drawn patch goes (x, z); the draw for one level of detail uses
        float _TileStart;                // _GrassTiles[_TileStart + instance]
        float4 _GrassFade;               // x, y: blades fade out from / gone by (m); z, w: the same for flowers
        float4 _GrassWheat;              // wheat colour (linear)
        float _GrassNear;                // full density out to (m)
        float _GrassWind;
        float _GrassDebug;

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
        };

        float GrassHash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

        Varyings vert(Attributes v, uint iid : SV_InstanceID)
        {
            Varyings o;
            float3 root;
            bool decor = v.uv0.z < -0.5;
            if (decor) root = TransformObjectToWorld(float3(v.uv0.x, 0, v.uv0.y));
            else
            {
                float4 tile = _GrassTiles[(uint)_TileStart + iid];
                root = float3(v.uv0.x + tile.x, 0, v.uv0.y + tile.z);
            }
            float4 f = SAMPLE_TEXTURE2D_LOD(_GrassField, sampler_GrassField, (root.xz - _GrassFieldRect.xy) * _GrassFieldRect.zw, 0);
            root.y = f.r - 0.04;
            float dist = distance(root.xz, _WorldSpaceCameraPos.xz);
            float h = GrassHash(root.xz);
            float bend = v.color.a;
            half3 col = v.color.rgb;
            float hMul = 1, wMul = 1;
            if (!decor)
            {
                hMul = saturate((f.g - h * 0.85) * 7);                    // patchy edges where the grass stops
                hMul *= lerp(0.7, 1.3, frac(h * 7.13));                  // taller and shorter bits
                hMul *= lerp(1, 1.5, f.b);                              // wheat is taller...
                col = lerp(col, _GrassWheat.rgb * lerp(0.85, 1.15, v.uv0.w), f.b); // ...and golden
                hMul *= lerp(1, 0.3, f.a);                               // flower clearings are short
                float dens = dist < _GrassNear ? 1 : pow(_GrassNear / dist, 1.35);
                hMul *= saturate((dens - v.uv0.z) * 12);                 // fewer blades far away...
                wMul = clamp(rsqrt(max(dens, 0.04)) * 0.8, 1, 2.6);      // ...but wider, so it still looks full
            }
            else hMul = saturate((f.g - 0.5) * 8);
            hMul *= 1 - (decor ? smoothstep(_GrassFade.z, _GrassFade.w, dist) : smoothstep(_GrassFade.x, _GrassFade.y, dist));

            float3 off = v.positionOS.xyz;
            off.xz *= wMul * saturate(hMul * 4);
            off.y *= hMul;
            float b2 = bend * bend;
            float2 lean = v.uv1 * b2 * hMul;
            // pushed aside (and down) by players, horses and things lying in the grass
            [loop] for (int k = 0; k < (int)_GrassPushCount; k++)
            {
                float4 p = _GrassPush[k];
                float2 d = root.xz - p.xz;
                float dl = length(d);
                if (dl < p.w && abs(root.y - p.y) < 2.5)
                {
                    float s = 1 - dl / p.w;
                    off.y *= 1 - s * 0.7;
                    lean += d / max(dl, 0.01) * s * 0.35 * b2 * hMul;
                }
            }
            // wind: slow gusts rolling over the field, and a quicker flutter
            float t = _Time.y;
            float gust = sin(t * 0.9 + root.x * 0.06 + root.z * 0.045) * 0.5 + 0.5;
            float2 wind = float2(sin(t * 1.9 + root.x * 0.35 + root.z * 0.2), cos(t * 1.5 + root.z * 0.3 - root.x * 0.13)) * 0.03
                        + float2(0.07, 0.035) * gust;
            lean += wind * b2 * hMul * _GrassWind;

            o.ws = root + off + float3(lean.x, 0, lean.y);
            o.positionCS = TransformWorldToHClip(o.ws);
            // dark at the root, bright at the tip; gusts lighten the tops a touch
            o.color = col * lerp(decor ? 0.6 : 0.42, 1.0, bend) * (1 + gust * 0.08 * b2);
            o.fog = ComputeFogFactor(o.positionCS.z);
            if (_GrassDebug > 0.5)
            {
                // debug: draw every blade full size, coloured by what the shader reads
                o.ws = root + v.positionOS.xyz;
                o.positionCS = TransformWorldToHClip(o.ws);
                o.color = _GrassDebug > 1.5 ? v.color.rgb : float3(frac(v.uv0.x / 8), frac(v.uv0.y / 8), v.uv0.w);
                o.fog = 0;
            }
            return o;
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
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target { return half4(0, 1, 0, 0); }
            ENDHLSL
        }
    }
}
