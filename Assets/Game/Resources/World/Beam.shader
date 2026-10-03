// Additive glowing light (no lighting, no fog, no depth writes): the airdrop ship's tractor beam and the glow under it,
// the ball's beacon, the landed airdrop crate's beacon, the victory UFO's beam, the arena's light shafts and the
// explosions' fireballs.
//   _Mode 0, a column / shaft (a smooth-sided cylinder or cone): brightest through its middle, fading out to soft edges
//            (by how square-on you see the surface: _Edge), with bands of light running along it (_Bands, _BandScale,
//            _Scroll) and fading out at its two ends (_FadeBottom, _FadeTop: fractions of its length, measured along
//            object y from _YRange.x to _YRange.y). Faces pointing along y (a cylinder's caps) are skipped.
//   _Mode 1, a disc (a quad, by its uv): a soft round glow, brightest in the middle.
//   _Mode 2, a ball (any closed mesh): brightest through its middle, soft edges, no caps skipped, no bands.
// Colour = _Color x _Intensity (HDR: above 1 it blooms when post processing is on).
//
// Beams (_DistFade 1) are brightest from far away and fade as you come up to them (Settings > Display > BEAMS, set as
// globals by BeamFx): x _BeamStrength, and by how far the camera is from the beam's upright axis (flat distance from the
// object's pivot): _BeamNearK right next to it, rising to 1 at _BeamFalloff metres (0 = no fading).
// _Clear0.._Clear3 (xyz a point, w a radius; w 0 = unused): the beam goes see-through round anything there (the crate coming
// down an airdrop beam, the winners floating up the victory beam), so you see it inside the light.
Shader "RockGame/Beam"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 1
        _Edge ("Edge softness (power)", Float) = 1.5
        _Bands ("Band strength (0..1)", Float) = 0.25
        _BandScale ("Bands per metre", Float) = 0.35
        _Scroll ("Band speed (bands a second, + up)", Float) = 1
        _FadeBottom ("Fade in from the bottom (fraction)", Float) = 0.02
        _FadeTop ("Fade out at the top (fraction)", Float) = 0.1
        _YRange ("Object y at the bottom (x) and top (y)", Vector) = (-1, 1, 0, 0)
        _Mode ("0 column, 1 disc, 2 ball", Float) = 0
        _DistFade ("Fades as you come close (a beam)", Float) = 0
        _Clear0 ("See-through round (xyz, radius)", Vector) = (0, 0, 0, 0)
        _Clear1 ("See-through round (xyz, radius)", Vector) = (0, 0, 0, 0)
        _Clear2 ("See-through round (xyz, radius)", Vector) = (0, 0, 0, 0)
        _Clear3 ("See-through round (xyz, radius)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity, _Edge, _Bands, _BandScale, _Scroll, _FadeBottom, _FadeTop, _Mode, _DistFade;
                float4 _YRange, _Clear0, _Clear1, _Clear2, _Clear3;
            CBUFFER_END
            // (globals, Settings > Display > BEAMS)
            float _BeamStrength, _BeamFalloff, _BeamNearK;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 ws : TEXCOORD0;
                float3 nw : TEXCOORD1;
                float4 data : TEXCOORD2; // uv, object y, how much the face points along y
                float near : TEXCOORD3;  // the distance fade (1: full strength)
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.nw = TransformObjectToWorldNormal(v.normalOS);
                o.data = float4(v.uv, v.positionOS.y, abs(v.normalOS.y));
                float k = 1.0;
                if (_DistFade > 0.5)
                {
                    float3 pivot = TransformObjectToWorld(float3(0, 0, 0));
                    float d = length(GetCameraPositionWS().xz - pivot.xz);
                    float ramp = _BeamFalloff > 0.01 ? smoothstep(2.0, max(2.5, _BeamFalloff), d) : 1.0;
                    k = lerp(saturate(_BeamNearK), 1.0, ramp) * max(0.0, _BeamStrength);
                }
                o.near = k;
                return o;
            }

            float Clear(float4 c, float3 cam, float3 dir)
            {
                if (c.w <= 0.0) return 1.0;
                float3 to = c.xyz - cam;
                float along = dot(to, dir);
                if (along <= 0.0) return 1.0;
                float miss = length(to - dir * along);
                return lerp(0.12, 1.0, smoothstep(c.w * 0.75, c.w * 1.35, miss));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float a;
                if (_Mode > 1.5)
                {
                    float3 V = normalize(GetCameraPositionWS() - i.ws);
                    a = pow(saturate(abs(dot(normalize(i.nw), V))), _Edge);
                }
                else if (_Mode > 0.5)
                {
                    float r = length(i.data.xy - 0.5) * 2.0;
                    a = pow(saturate(1.0 - r), _Edge);
                }
                else
                {
                    float3 V = normalize(GetCameraPositionWS() - i.ws);
                    float ndv = abs(dot(normalize(i.nw), V));
                    float core = pow(saturate(ndv), _Edge);
                    float t = saturate((i.data.z - _YRange.x) / max(1e-4, _YRange.y - _YRange.x));
                    float fade = smoothstep(0.0, max(_FadeBottom, 1e-4), t) * smoothstep(0.0, max(_FadeTop, 1e-4), 1.0 - t);
                    float bands = 1.0 - _Bands + _Bands * (0.5 + 0.5 * sin((i.ws.y * _BandScale - _Time.y * _Scroll) * 6.28318));
                    float cap = 1.0 - step(0.9, i.data.w);
                    a = core * fade * bands * cap;
                    if (_Clear0.w > 0.0 || _Clear1.w > 0.0 || _Clear2.w > 0.0 || _Clear3.w > 0.0)
                    {
                        float3 cam = GetCameraPositionWS();
                        float3 dir = normalize(i.ws - cam);
                        a *= Clear(_Clear0, cam, dir) * Clear(_Clear1, cam, dir) * Clear(_Clear2, cam, dir) * Clear(_Clear3, cam, dir);
                    }
                }
                return half4(_Color.rgb * (_Intensity * a * i.near), 0);
            }
            ENDHLSL
        }
    }
}
