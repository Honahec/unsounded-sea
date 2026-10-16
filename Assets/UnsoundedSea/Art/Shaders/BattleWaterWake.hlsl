#ifndef UNSOUNDED_SEA_WATER_WAKE_INCLUDED
#define UNSOUNDED_SEA_WATER_WAKE_INCLUDED

// Presentation-only disturbance around the active sea actor; never a hit or intent indicator.
float UnsoundedSeaWake(float2 worldXZ, float4 threat, float time)
{
    float d = distance(worldXZ, threat.xy);
    float envelope = step(0.01, threat.z) * exp(-abs(d - threat.z) * 0.17);
    return envelope * sin(d * 1.5 - time * 1.8) * (0.14 + threat.w * 0.25);
}

float3 UnsoundedSeaWakeFoam(float2 worldXZ, float4 threat, float time)
{
    float d = distance(worldXZ, threat.xy);
    float envelope = step(0.01, threat.z) * exp(-abs(d - threat.z) * 0.24);
    float breakup = 0.5 + 0.5 * sin(worldXZ.x * 2.3 + sin(worldXZ.y * 1.7 + time * 0.4));
    float crest = smoothstep(0.7, 1.0, sin(d * 1.5 - time * 1.8));
    return float3(0.08, 0.13, 0.13) * envelope * crest * breakup;
}
#endif
