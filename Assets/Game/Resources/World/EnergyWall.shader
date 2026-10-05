// Settings > Display > Energy wall (EnergyWall.cs): the glass wall between the halves (and the ball's dome in the
// middle) drawn as a glowing, see-through energy field instead of glass. Team-neutral cyan: a faint tinted sheet with a
// honeycomb of light over it (laid out in world space, so the panels and the dome all line up), cells that flare up now
// and then, bright scan bands rising up it, fine flickering scanlines and a brighter rim where you see it edge-on.
// Unlit, no fog, no depth writes, both sides; premultiplied blend (One, OneMinusSrcAlpha): mostly added light, with a
// little of what's behind dimmed so it reads against a bright sky too. Colour = _Color x _Intensity (HDR: over 1 it
// blooms when post processing is on). Only the look: the wall's colliders and its drop are the glass wall's own.
Shader "RockGame/EnergyWall"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.4, 0.85, 1, 1)
        _Intensity ("Intensity", Float) = 1.5
        _Base ("Sheet opacity", Float) = 0.07
        _HexSize ("Hex size (m)", Float) = 1.1
        _Speed ("Animation speed", Float) = 1
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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity, _Base, _HexSize, _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 nw : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.nw = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float2 Mod(float2 a, float2 b) { return a - b * floor(a / b); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.nw);
                float t = _Time.y * _Speed;
                // the plane the honeycomb is laid in: the face's own (a wall along x or z, or the top of the dome)
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
                float aa = max(0.035, fwidth(toEdge) * 1.5);
                float edge = 1.0 - smoothstep(0.0, aa, toEdge);
                // far away the honeycomb melts into an even glow (no shimmering moire)
                float dist = distance(GetCameraPositionWS(), i.ws);
                edge *= 1.0 - smoothstep(55.0, 140.0, dist) * 0.7;
                // now and then a cell flares up and fades
                float h = Hash(cell);
                float flare = pow(saturate(sin(t * (0.6 + h * 0.9) + h * 6.2831)), 12.0);
                // bright bands rising up it, and fine flickering scanlines
                float band = pow(frac(i.ws.y * 0.09 - t * 0.22 + h * 0.02), 14.0);
                float lines = 0.5 + 0.5 * sin(i.ws.y * 18.0 - t * 5.0);
                float flicker = 0.92 + 0.08 * sin(t * 23.0 + i.ws.x * 0.7);
                // brighter where you see it edge-on
                float3 view = normalize(GetCameraPositionWS() - i.ws);
                float rim = pow(1.0 - saturate(abs(dot(view, n))), 2.5);

                float glow = (_Base + edge * 0.42 + flare * 0.16 + band * 0.3 + lines * 0.035 + rim * 0.3) * flicker;
                half3 col = _Color.rgb * _Intensity * glow;
                half alpha = saturate(_Base * 1.4 + edge * 0.12 + rim * 0.15);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
