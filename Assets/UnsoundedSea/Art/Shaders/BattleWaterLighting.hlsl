#ifndef UNSOUNDED_SEA_WATER_LIGHTING_INCLUDED
#define UNSOUNDED_SEA_WATER_LIGHTING_INCLUDED

// Water is a dielectric (F0 ~ 2%). Light colour/intensity, visibility and
// distance attenuation must survive the specular calculation, including in Forward+.
float3 UnsoundedSeaLight(Light light, float3 n, float3 v, float roughness, out float3 diffuse)
{
    float nl = saturate(dot(n, light.direction));
    float nv = max(0.04, saturate(dot(n, v)));
    float3 radiance = light.color * light.distanceAttenuation * light.shadowAttenuation;
    diffuse = radiance * nl / PI;
    float3 h = SafeNormalize(light.direction + v);
    float nh = saturate(dot(n, h));
    float vh = saturate(dot(v, h));
    float a2 = pow(roughness, 4);
    float denom = nh * nh * (a2 - 1) + 1;
    float distribution = a2 / max(0.00001, PI * denom * denom);
    float gv = nl * sqrt(nv * nv * (1 - a2) + a2);
    float gl = nv * sqrt(nl * nl * (1 - a2) + a2);
    float visibility = 0.5 / max(0.0001, gv + gl);
    float fresnel = 0.02 + 0.98 * pow(1 - vh, 5);
    return radiance * (distribution * visibility * fresnel * nl);
}

float3 UnsoundedSeaLighting(float3 p, float3 n, float3 v, out float3 diffuse)
{
    n = normalize(n); v = SafeNormalize(v);
    // Pixel normal variance softens distant subpixel glints instead of sparkling.
    float variance = dot(ddx(n), ddx(n)) + dot(ddy(n), ddy(n));
    float roughness = clamp(sqrt(_Specular_Spread * _Specular_Spread + min(0.25, variance)), 0.22, 0.8);
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(p), p, half4(1,1,1,1));
    float3 directDiffuse;
    float3 specular = 0;
    diffuse = max(0, SampleSH(n));
    uint layers = GetMeshRenderingLayer();
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, layers))
#endif
    {
        specular += UnsoundedSeaLight(mainLight, n, v, roughness, directDiffuse);
        diffuse += directDiffuse;
    }
#if defined(_ADDITIONAL_LIGHTS)
#if UBER_USE_FORWARD_PLUS
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        UBER_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, p, half4(1,1,1,1));
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, layers))
#endif
        {
            specular += UnsoundedSeaLight(light, n, v, roughness, directDiffuse);
            diffuse += directDiffuse;
        }
    }
#endif
    InputData inputData = (InputData)0;
    float4 screenPos = ComputeScreenPos(TransformWorldToHClip(p));
    inputData.normalizedScreenSpaceUV = screenPos.xy / screenPos.w;
    inputData.positionWS = p;
    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, p, half4(1,1,1,1));
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, layers))
#endif
        {
            specular += UnsoundedSeaLight(light, n, v, roughness, directDiffuse);
            diffuse += directDiffuse;
        }
    LIGHT_LOOP_END
#endif
    return specular * saturate(_Specular_Color.rgb);
}

float3 UnsoundedSeaLitSurface(float3 water, float3 p, float3 n, float3 v, float3 tangentNormal, float3 tangent, float3 bitangent)
{
    float3 detailNormal = normalize(TransformTangentToWorld(tangentNormal, float3x3(tangent, bitangent, n), true));
    float3 diffuse;
    float3 specular = UnsoundedSeaLighting(p, detailNormal, v, diffuse);
    float foam = UnsoundedSeaFoamMask(p.xz, _Threat, _SeaTime);
    // Foam covers reflective water; it is lit diffuse material, never additive emission.
    return lerp(water + specular, saturate(_SurfFoam_Color.rgb) * diffuse, foam);
}
#endif
