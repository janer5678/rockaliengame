// Shared lighting for the ground and the grass: the main light with its shadows, ambient light and SSAO,
// close to what URP's Lit gives everything else (so the grass sits in the same world as the rest).
#ifndef ROCKGAME_GRASS_LIGHT
#define ROCKGAME_GRASS_LIGHT

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

half3 GrassShade(half3 albedo, float3 ws, half3 n, float4 positionCS, half wrap)
{
    float4 sc = TransformWorldToShadowCoord(ws);
    Light l = GetMainLight(sc, ws, half4(1, 1, 1, 1));
    half3 amb = SampleSH(n);
    half3 direct = l.color * l.shadowAttenuation;
#if defined(_SCREEN_SPACE_OCCLUSION)
    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(positionCS));
    amb *= ao.indirectAmbientOcclusion;
    direct *= ao.directAmbientOcclusion;
#endif
    // wrap > 0 lets light "through" a little (thin grass blades lit from behind)
    half ndl = saturate((dot(n, l.direction) + wrap) / (1 + wrap));
    return albedo * (direct * ndl + amb);
}

#endif
