// Sudden death arena: the space all around the Final Destination platform. Drawn on the inside of a huge sphere
// centred on the arena (it hides the far-away map behind it). Fully procedural: a deep purple / blue nebula with a
// bright milky band across the horizon, two layers of stars and a few big twinkling ones.
Shader "RockGame/SpaceSky"
{
    Properties
    {
        _Seed ("Seed", Float) = 3.7
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+400" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Seed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float3 hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float hash31(float3 p) { return hash33(p).x; }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash31(i), b = hash31(i + float3(1, 0, 0)), c = hash31(i + float3(0, 1, 0)), d = hash31(i + float3(1, 1, 0));
                float e = hash31(i + float3(0, 0, 1)), g = hash31(i + float3(1, 0, 1)), h = hash31(i + float3(0, 1, 1)), k = hash31(i + float3(1, 1, 1));
                return lerp(lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y), lerp(lerp(e, g, f.x), lerp(h, k, f.x), f.y), f.z);
            }

            float fbm(float3 p)
            {
                float s = 0.0, a = 0.5;
                for (int i = 0; i < 5; i++) { s += a * vnoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            // one layer of round stars: a star somewhere in each grid cell of the direction, only some cells lit
            float3 stars(float3 d, float scale, float density, float size)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float3 h = hash33(cell + _Seed);
                float3 sp = cell + 0.25 + h * 0.5;
                float dist = length(p - sp);
                float on = step(1.0 - density, hash31(cell * 1.7 + 4.1));
                float b = on * saturate(1.0 - dist / size);
                b = b * b;
                float3 tint = lerp(float3(0.75, 0.82, 1.0), float3(1.0, 0.85, 0.95), h.y);
                return tint * b * (0.5 + h.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 q = d + _Seed;
                // warped nebula clouds
                float3 w = float3(fbm(q * 1.6), fbm(q * 1.6 + 5.2), fbm(q * 1.6 + 9.4));
                float n1 = fbm(d * 2.2 + w * 1.4);
                float n2 = fbm(d * 3.7 + w * 2.0 + 11.0);
                float3 col = float3(0.008, 0.006, 0.03);
                col += float3(0.30, 0.06, 0.42) * smoothstep(0.42, 0.85, n1);
                col += float3(0.04, 0.12, 0.48) * smoothstep(0.48, 0.9, n2);
                col += float3(0.55, 0.12, 0.45) * pow(saturate(n1 * n2 * 2.3 - 0.35), 2.0);
                // a bright milky band sweeping round near the horizon (tilted, wavy, dusty)
                float band = d.y + 0.16 * sin(atan2(d.z, d.x) * 2.0 + 1.3) + 0.05;
                float dust = fbm(d * 6.0 + w * 1.5);
                float bandI = exp(-band * band * 38.0) * smoothstep(0.3, 0.8, dust);
                col += float3(0.45, 0.42, 0.85) * bandI * 0.9;
                col += float3(0.85, 0.75, 1.0) * stars(d, 260.0, 0.6, 0.34) * (0.35 + bandI * 3.0);
                // stars
                col += stars(d, 140.0, 0.22, 0.3) * 1.2;
                col += stars(d, 60.0, 0.06, 0.22) * 1.6;
                // a few big twinkly ones: a bright core with a four-point cross
                float3 p = d * 18.0;
                float3 cell = floor(p);
                float3 h = hash33(cell + _Seed * 2.0);
                float3 sp = cell + 0.3 + h * 0.4;
                float3 dv = p - sp;
                float on = step(0.86, hash31(cell * 3.1 + 2.0));
                float r = length(dv);
                // the cross is drawn in a local tangent frame of the direction
                float3 t1 = normalize(cross(d, float3(0, 1, 0.001)));
                float3 t2 = cross(d, t1);
                float a = dot(dv, t1), b = dot(dv, t2);
                float cross4 = saturate(1.0 - abs(a) * 60.0) * saturate(1.0 - abs(b) * 3.5) + saturate(1.0 - abs(b) * 60.0) * saturate(1.0 - abs(a) * 3.5);
                float core = saturate(1.0 - r * 9.0);
                col += on * (core * core * 3.0 + cross4 * 0.9) * lerp(float3(0.7, 0.85, 1.0), float3(1.0, 0.8, 1.0), h.x);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
