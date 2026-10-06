// Settings > Display > Energy wall (EnergyWall.cs): all the glass - the wall between the halves, the ball's dome and the
// big dome over the map - drawn as a see-through energy field instead of glass. Team-neutral cyan: a faint tinted sheet
// with a honeycomb of light over it (laid out in world space, so the panels and the domes all line up), cells that glow
// up now and then, soft scan bands drifting up it and a brighter rim where you see it edge-on. _Strength (Settings >
// Display, 0..1) scales how bright, opaque and busy all of that is: low is a faint shimmer you see the other teams and the
// sky through. Unlit, no fog, no depth writes, both sides; premultiplied blend (One, OneMinusSrcAlpha): mostly added
// light, with a little of what's behind dimmed. The big dome's copy has its hole mask (_HoleMask, UVs = the map's x, z:
// blown-out holes stay open) and its fade (_Fade: the victory cutscene). Only the look: the colliders are the glass's own.
Shader "RockGame/EnergyWall"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.4, 0.85, 1, 1)
        _Strength ("Strength", Range(0, 1)) = 0.3
        _Base ("Sheet opacity", Float) = 0.05
        _HexSize ("Hex size (m)", Float) = 1.1
        _Speed ("Animation speed", Float) = 0.6
        _HoleMask ("Hole mask", 2D) = "white" {}
        _UseMask ("Use the hole mask", Float) = 0
        _Fade ("Fade", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "EnergyWall"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_HoleMask); SAMPLER(sampler_HoleMask);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _HoleMask_ST;
                float _Strength, _Base, _HexSize, _Speed, _UseMask, _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 nw : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.nw = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float2 Mod(float2 a, float2 b) { return a - b * floor(a / b); }

            half4 frag(Varyings i) : SV_Target
            {
                float s = saturate(_Strength);
                float3 n = normalize(i.nw);
                float t = _Time.y * _Speed * (0.4 + 0.6 * s);
                // the plane the honeycomb is laid in: the face's own (a wall along x or z, or the top of a dome)
                float3 an = abs(n);
                float2 p = an.y > 0.7 ? i.ws.xz : (an.x > an.z ? i.ws.zy : i.ws.xy);
                // a hex grid: the nearer of two offset rectangular grids' centres is this cell's centre
                float2 q = p / max(0.2, _HexSize);
                const float2 r = float2(1.0, 1.7320508);
                float2 a = Mod(q, r) - r * 0.5;
                float2 b = Mod(q - r * 0.5, r) - r * 0.5;
                float2 g = dot(a, a) < dot(b, b) ? a : b;
                float2 cell = q - g;
                float2 ag = abs(g);
                float toEdge = 0.5 - max(dot(ag, normalize(r)), ag.x); // 0 on a cell's edge
                float aa = max(0.03, fwidth(toEdge) * 1.5);
                float edge = 1.0 - smoothstep(0.0, aa, toEdge);
                // far away the honeycomb melts into an even, faint glow (no shimmering moire)
                float dist = distance(GetCameraPositionWS(), i.ws);
                edge *= 1.0 - smoothstep(45.0, 120.0, dist) * 0.85;
                // now and then a cell glows up and fades (slowly)
                float h = Hash(cell);
                float flare = pow(saturate(sin(t * (0.3 + h * 0.5) + h * 6.2831)), 16.0);
                // soft bands drifting up it
                float band = pow(frac(i.ws.y * 0.06 - t * 0.12 + h * 0.02), 10.0);
                // brighter where you see it edge-on
                float3 view = normalize(GetCameraPositionWS() - i.ws);
                float rim = pow(1.0 - saturate(abs(dot(view, n))), 3.0);

                float base = _Base * (0.35 + 0.65 * s);
                float glow = base + (edge * 0.36 + flare * 0.12 + band * 0.14) * s + rim * (0.08 + 0.2 * s);
                half3 col = _Color.rgb * glow * (0.5 + 1.0 * s);
                half alpha = saturate(base * 1.2 + edge * 0.08 * s + rim * 0.1 * s);
                float keep = saturate(_Fade);
                if (_UseMask > 0.5) keep *= SAMPLE_TEXTURE2D(_HoleMask, sampler_HoleMask, i.uv).a;
                return half4(col, alpha) * keep;
            }
            ENDHLSL
        }
    }
}
