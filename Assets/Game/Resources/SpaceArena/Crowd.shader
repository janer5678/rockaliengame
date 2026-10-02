// The sudden death arena's crowd (SpaceArena.cs): hundreds of aliens drawn in one go per stand (procedural
// instancing: Graphics.RenderMeshPrimitives, each copy finds its own data by SV_InstanceID - no instancing variants),
// every one animated here in the vertex shader. Also draws the floating stands they're on (_Stand = 1), so the
// stands and their fans bob up and down together.
//
// The alien (CrowdAlien.txt, baked by Tools/crowd_alien.py from the player model) is flat coloured per face; each
// corner says which chain moves it (uv0.x: 0 body, 1 head, 2 left arm, 3 right arm, 4 left leg, 5 right leg) and how
// much it follows the chain's first joint (uv0.y: shoulder / neck / hip) and second joint (uv0.z: elbow / knee).
// A pose is a handful of joint angles worked out from the fan's animation (jumping, fist pumping, waving, clapping,
// swaying, dancing, spinning, sitting and standing up, ...), its own timing, how excited the crowd is and the
// Mexican wave going round the stadium.
//
// Per fan (_CrowdData, 3 float4s): xyz position (world), w yaw (radians); rgb skin colour (linear), a scale;
// x animation, y phase (0..1), z speed, w its stand's bob phase.
// Lit per vertex: the main light, the ambient light and a soft floodlight from over the platform.
Shader "RockGame/Crowd"
{
    Properties
    {
        _Stand ("Stand mesh (vertex colours)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            StructuredBuffer<float4> _CrowdData;
            float _Start;                 // this draw's first fan
            float _Stand;
            float4 _CrowdCentre;          // xyz: the middle of the platform (world); w: the floodlight's height over it
            float4 _CrowdMood;            // x: excitement 0..1; y: the Mexican wave's angle (radians); z: its strength 0..1; w: the crowd's clock (s)
            float4 _CrowdJ[8];            // joints: 0 hips, 1 neck, 2/3 shoulder L/R, 4/5 elbow L/R, 6/7 hip L/R (w: 0)
            float4 _CrowdK[4];            // 0/1 knee L/R; 2: x rest angle of the upper arms from straight down, y sitting drop
            float4 _CrowdAxis[2];         // the elbows' hinge axes L/R (the forearm bends forward round them)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;          // rgb: flat colour (linear); a: 1 on the head (fans)
                float4 uv0 : TEXCOORD0;       // fans: chain, w1, w2; stands: x bob phase
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : TEXCOORD0;
            };

            #define D2R 0.0174533

            // rotations (angles in radians): rx turns y towards z, ry turns z towards x... (see each)
            float3 RotX(float3 v, float a) { float s = sin(a), c = cos(a); return float3(v.x, v.y * c - v.z * s, v.y * s + v.z * c); }
            float3 RotY(float3 v, float a) { float s = sin(a), c = cos(a); return float3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c); }
            float3 RotZ(float3 v, float a) { float s = sin(a), c = cos(a); return float3(v.x * c - v.y * s, v.x * s + v.y * c, v.z); }
            float3 RotAxis(float3 v, float3 k, float a) { float s = sin(a), c = cos(a); return v * c + cross(k, v) * s + k * dot(k, v) * (1 - c); }

            struct Pose
            {
                float4 armL;   // raise (0 hanging, 90 out to the side, 180 straight up), forward swing, swing in front, elbow bend (degrees)
                float4 armR;
                float4 legs;   // thigh forward L, knee bend L, thigh forward R, knee bend R
                float4 head;   // nod (down), turn, tilt
                float4 body;   // lean forward (from the hips), lean sideways (from the hips), sway (from the feet), spin (radians)
                float lift;    // up (jumps) or down (sitting), metres in the model
            };

            Pose Relaxed()
            {
                Pose p;
                p.armL = float4(14, 6, 0, 18);
                p.armR = p.armL;
                p.legs = 0;
                p.head = 0;
                p.body = 0;
                p.lift = 0;
                return p;
            }

            Pose Lerp(Pose a, Pose b, float t)
            {
                Pose p;
                p.armL = lerp(a.armL, b.armL, t); p.armR = lerp(a.armR, b.armR, t);
                p.legs = lerp(a.legs, b.legs, t); p.head = lerp(a.head, b.head, t);
                p.body = lerp(a.body, b.body, t); p.lift = lerp(a.lift, b.lift, t);
                return p;
            }

            // seated on the bench behind: thighs forward, shins down, the whole fan lower
            void Sit(inout Pose p, float s)
            {
                p.legs = lerp(p.legs, float4(86, 84, 86, 84), s);
                p.lift = lerp(p.lift, -_CrowdK[2].y, s);
            }

            float Hop(float x) { return sin(frac(x) * 3.14159); } // 0 -> 1 -> 0 once a beat

            Pose Animate(float anim, float t, float hype, float side)
            {
                Pose p = Relaxed();
                float amp = 1 + hype * 0.8;
                int a = (int)anim;
                if (a == 0) // jumping, arms up
                {
                    float h = Hop(t * 1.5);
                    p.lift = h * 0.32 * amp;
                    p.legs = float4(18, 30, 18, 30) * (1 - h) + float4(30, 50, 30, 50) * h * h;
                    float w = sin(t * 9.4) * 10;
                    p.armL = float4(160 + w, -8, 0, 12);
                    p.armR = float4(160 - w, -8, 0, 12);
                    p.head.x = -10 * h;
                }
                else if (a == 1) // fist pumping (one arm), the other fist at the waist
                {
                    float u = 0.5 + 0.5 * sin(t * 6.3);
                    float4 pump = float4(lerp(115, 172, u), lerp(-25, -5, u), 0, lerp(95, 8, u));
                    float4 rest = float4(22, 18, 0, 95);
                    p.armR = side > 0 ? pump : rest;
                    p.armL = side > 0 ? rest : pump;
                    p.lift = 0.04 * u * amp;
                    p.legs = float4(10, 18, 10, 18) * (1 - u);
                    p.head.x = -8 * u;
                }
                else if (a == 2) // waving one arm over the head
                {
                    float w = sin(t * 5.2);
                    float4 wave = float4(148 + 22 * w, -6, 8, 28 + 10 * w);
                    p.armR = side > 0 ? wave : p.armR;
                    p.armL = side > 0 ? p.armL : wave;
                    p.body.z = 3 * w * side;
                    p.head.z = 6 * sin(t * 2.6);
                }
                else if (a == 3) // clapping in front
                {
                    float c = 0.5 + 0.5 * sin(t * 9.4);
                    p.armL = float4(78, -10, 62 + 26 * c, 26);
                    p.armR = p.armL;
                    p.lift = 0.03 * c;
                    p.head.x = 6 * c;
                }
                else if (a == 4) // arms up, swaying side to side (in time with the stand)
                {
                    float s = sin(t * 1.7);
                    p.armL = float4(158 - 8 * s, -6, 0, 14);
                    p.armR = float4(158 + 8 * s, -6, 0, 14);
                    p.body.z = 11 * s * amp;
                    p.body.y = 4 * s;
                    p.head.z = -6 * s;
                }
                else if (a == 5) // dancing: bobbing, head nodding, arms swinging
                {
                    float b = sin(t * 4.2);
                    p.lift = 0.07 * abs(b) * amp;
                    p.legs = float4(14, 26, 14, 26) * (1 - abs(b));
                    p.head.x = 16 * sin(t * 8.4);
                    p.armL = float4(28, 40 * b, 0, 70);
                    p.armR = float4(28, -40 * b, 0, 70);
                    p.body.x = 6;
                    p.body.y = 5 * b;
                }
                else if (a == 6) // spinning round, arms out
                {
                    p.body.w = t * 3.2;
                    p.armL = float4(92, 0, 10, 10);
                    p.armR = p.armL;
                    p.lift = 0.05 * abs(sin(t * 6.4));
                    p.head.x = -12;
                }
                else if (a == 7 || a == 8 || a == 9) // sitting (7: stands up to cheer now and then; 8: claps; 9: sits back and looks round)
                {
                    float u = frac(t * 0.13);
                    float stand = a == 7 ? smoothstep(0.55, 0.63, u) * (1 - smoothstep(0.86, 0.94, u)) : 0;
                    stand = max(stand, saturate(hype * 1.4 - 0.25)); // everyone's up when it goes wild
                    if (a == 8)
                    {
                        float c = 0.5 + 0.5 * sin(t * 8.8);
                        p.armL = float4(55, 12, 58 + 24 * c, 48);
                        p.armR = p.armL;
                    }
                    else if (a == 9)
                    {
                        p.armL = float4(12, 40, 0, 58);
                        p.armR = p.armL;
                        p.head.y = 30 * sin(t * 0.55);
                        p.body.x = -6;
                    }
                    else
                    {
                        p.armL = float4(10, 44, 0, 52);
                        p.armR = p.armL;
                    }
                    Sit(p, 1);
                    Pose up = Relaxed();
                    float w = sin(t * 8) * 10;
                    up.armL = float4(162 + w, -8, 0, 10);
                    up.armR = float4(162 - w, -8, 0, 10);
                    up.lift = 0.06 * abs(sin(t * 5));
                    p = Lerp(p, up, stand);
                }
                else if (a == 10) // pointing at the fight, the other hand on the hip
                {
                    float sh = sin(t * 7.5);
                    float4 aim = float4(18, 82 + 6 * sh, 0, 6);
                    float4 hip = float4(38, -12, 0, 105);
                    p.armR = side > 0 ? aim : hip;
                    p.armL = side > 0 ? hip : aim;
                    p.head.x = 8 + 6 * sh;
                    p.body.x = 6;
                }
                else if (a == 11) // head-banging, horns up
                {
                    float b = sin(t * 8.6);
                    p.body.x = 14 + 14 * b;
                    p.head.x = 14 * sin(t * 8.6 - 0.6);
                    p.armL = float4(150, -10, 0, 45);
                    p.armR = p.armL;
                    p.legs = float4(14, 24, 14, 24);
                }
                else // 12: bouncing, clapping over the head
                {
                    float c = 0.5 + 0.5 * sin(t * 8.4);
                    p.armL = float4(166 + 12 * c, -4, 0, 18 + 18 * c);
                    p.armR = p.armL;
                    float h = Hop(t * 1.34);
                    p.lift = 0.12 * h * amp;
                    p.legs = float4(16, 28, 16, 28) * (1 - h);
                }
                return p;
            }

            // one arm: bend the elbow, then turn the arm at the shoulder (and the normal the same way)
            void Arm(inout float3 q, inout float3 n, float4 ang, float sgn, float3 S, float3 E, float3 axis, float w1, float w2)
            {
                float bend = ang.w * D2R;
                float3 bq = E + RotAxis(q - E, axis, bend);
                float3 bn = RotAxis(n, axis, bend);
                q = lerp(q, bq, w2);
                n = lerp(n, bn, w2);
                float rz = sgn * (ang.x - _CrowdK[2].x) * D2R, rx = -ang.y * D2R, ry = -sgn * ang.z * D2R;
                float3 sq = S + RotY(RotX(RotZ(q - S, rz), rx), ry);
                float3 sn = RotY(RotX(RotZ(n, rz), rx), ry);
                q = lerp(q, sq, w1);
                n = lerp(n, sn, w1);
            }

            void Leg(inout float3 q, inout float3 n, float thigh, float knee, float3 H, float3 K, float w1, float w2)
            {
                float k = knee * D2R, a = -thigh * D2R;
                q = lerp(q, K + RotX(q - K, k), w2);
                n = lerp(n, RotX(n, k), w2);
                q = lerp(q, H + RotX(q - H, a), w1);
                n = lerp(n, RotX(n, a), w1);
            }

            half3 Shade(half3 albedo, float3 ws, float3 n)
            {
                Light l = GetMainLight();
                half3 amb = SampleSH(n);
                half3 direct = l.color * saturate(dot(n, l.direction)) * 0.8;
                // floodlights over the platform light the side facing the fight
                float3 fl = normalize(_CrowdCentre.xyz + float3(0, _CrowdCentre.w, 0) - ws);
                half3 flood = half3(1.0, 0.9, 1.0) * saturate(dot(n, fl)) * 0.75;
                return albedo * (direct + amb + flood);
            }

            Varyings vert(Attributes v, uint iid : SV_InstanceID)
            {
                Varyings o;
                float bobT = _Time.y * 0.5;
                if (_Stand > 0.5)
                {
                    float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                    ws.y += sin(bobT + v.uv0.x) * 0.45;
                    float3 nw = TransformObjectToWorldNormal(v.normalOS);
                    o.positionCS = TransformWorldToHClip(ws);
                    o.color = Shade(v.color.rgb, ws, nw);
                    return o;
                }
                uint id = ((uint)_Start + iid) * 3;
                float4 d0 = _CrowdData[id], d1 = _CrowdData[id + 1], d2 = _CrowdData[id + 2];
                float tt = _CrowdMood.w * d2.z + d2.y * 37.0; // (the crowd's clock runs faster when they're excited)
                float side = frac(d2.y * 7.31) > 0.5 ? 1 : -1;
                Pose p = Animate(d2.x, tt, _CrowdMood.x, side);
                // the Mexican wave: up on their feet, arms in the air as it passes
                float2 rel = d0.xz - _CrowdCentre.xz;
                float ang = atan2(rel.y, rel.x);
                float dw = abs(frac((ang - _CrowdMood.y) / 6.28318 + 0.5) - 0.5) * 6.28318;
                float wv = _CrowdMood.z * saturate(1 - dw / 0.55);
                wv = wv * wv * (3 - 2 * wv);
                if (wv > 0.001)
                {
                    Pose up = Relaxed();
                    up.armL = float4(172, -6, 0, 6);
                    up.armR = up.armL;
                    up.lift = 0.22;
                    up.head.x = -14;
                    p = Lerp(p, up, wv);
                }

                float3 q = v.positionOS.xyz;
                float3 n = v.normalOS;
                int chain = (int)(v.uv0.x + 0.5);
                float w1 = v.uv0.y, w2 = v.uv0.z;
                float3 hips = _CrowdJ[0].xyz;
                if (chain >= 4)
                {
                    bool r = chain == 5;
                    Leg(q, n, r ? p.legs.z : p.legs.x, r ? p.legs.w : p.legs.y, r ? _CrowdJ[7].xyz : _CrowdJ[6].xyz, r ? _CrowdK[1].xyz : _CrowdK[0].xyz, w1, w2);
                }
                else
                {
                    float wt = 1;
                    if (chain == 1)
                    {
                        float3 N = _CrowdJ[1].xyz;
                        float3 hq = N + RotY(RotX(RotZ(q - N, -p.head.z * D2R), p.head.x * D2R), p.head.y * D2R);
                        float3 hn = RotY(RotX(RotZ(n, -p.head.z * D2R), p.head.x * D2R), p.head.y * D2R);
                        q = lerp(q, hq, w1);
                        n = lerp(n, hn, w1);
                    }
                    else if (chain >= 2)
                    {
                        bool r = chain == 3;
                        Arm(q, n, r ? p.armR : p.armL, r ? 1 : -1, r ? _CrowdJ[3].xyz : _CrowdJ[2].xyz, r ? _CrowdJ[5].xyz : _CrowdJ[4].xyz,
                            r ? _CrowdAxis[1].xyz : _CrowdAxis[0].xyz, w1, w2);
                    }
                    else wt = smoothstep(hips.y - 0.03, hips.y + 0.12, q.y); // the pelvis stays put
                    // the upper body leans from the hips
                    float3 lq = hips + RotZ(RotX(q - hips, p.body.x * D2R), -p.body.y * D2R);
                    float3 ln = RotZ(RotX(n, p.body.x * D2R), -p.body.y * D2R);
                    q = lerp(q, lq, wt);
                    n = lerp(n, ln, wt);
                }
                // the whole fan: up / down, sway from the feet, spin
                q.y += p.lift;
                q = RotY(RotZ(q, -p.body.z * D2R), p.body.w);
                n = RotY(RotZ(n, -p.body.z * D2R), p.body.w);
                // into the world: scale (wider, like the players), turn to face the platform, bob with the stand
                q *= d1.a * float3(1.2, 1, 1.2);
                q = RotY(q, d0.w);
                n = normalize(RotY(n, d0.w));
                float3 ws = d0.xyz + q;
                ws.y += sin(bobT + d2.w) * 0.45;
                o.positionCS = TransformWorldToHClip(ws);
                // the head is lighter than the body (like the players, PlayerNet.TeamTint: the grey texture times a bright
                // colour, x1.6 on the body and x1.35 towards white on the head - here in linear, so 1.6^2.2 and 1.35^2.2)
                half3 tint = v.color.a > 0.5 ? lerp(half3(1, 1, 1), d1.rgb, 0.55) * 1.93 : d1.rgb * 2.81;
                o.color = Shade(v.color.rgb * tint, ws, n);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(i.color, 1);
            }
            ENDHLSL
        }
    }
}
