Shader "UnsoundedSea/Voyage Landmark"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal strength", Float) = 1
        _MetallicRoughnessMap("glTF roughness G / metallic B", 2D) = "white" {}
        _Metallic("Metallic", Range(0,1)) = 0
        _Roughness("Roughness", Range(0,1)) = 0.8
        _EmissionColor("Emission", Color) = (0,0,0,0)
        _NodeVisibility("Arrival through mist", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicRoughnessMap); SAMPLER(sampler_MetallicRoughnessMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor, _EmissionColor;
                float _BumpScale, _Metallic, _Roughness, _NodeVisibility;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float4 tangent:TEXCOORD2; float2 uv:TEXCOORD3; float fog:TEXCOORD4; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.world=TransformObjectToWorld(input.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(input.normalOS);
                o.tangent=float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w*GetOddNegativeScale());
                o.uv=TRANSFORM_TEX(input.uv,_BaseMap); o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                // Fine screen-door coverage avoids transparent sorting and progressively removes the silhouette.
                float dither=frac(52.9829189*frac(dot(i.positionCS.xy,float2(0.06711056,0.00583715))));
                clip(_NodeVisibility-dither-0.00001);
                // glTF embeds an RGB normal texture, not Unity's DXT5nm packing.
                float3 normalTS=SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv).xyz*2-1;
                normalTS.xy*=_BumpScale; normalTS=normalize(normalTS);
                float3 n=normalize(i.normal), t=normalize(i.tangent.xyz);
                float3 b=cross(n,t)*i.tangent.w;
                InputData data=(InputData)0;
                data.positionWS=i.world; data.normalWS=NormalizeNormalPerPixel(normalTS.x*t+normalTS.y*b+normalTS.z*n);
                data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.world);
                data.shadowCoord=TransformWorldToShadowCoord(i.world);
                data.bakedGI=SampleSH(data.normalWS);
                data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                data.shadowMask=half4(1,1,1,1);
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
                float4 mr=SAMPLE_TEXTURE2D(_MetallicRoughnessMap,sampler_MetallicRoughnessMap,i.uv);
                surface.metallic=mr.b*_Metallic; surface.smoothness=1-max(0.25,mr.g*_Roughness);
                surface.occlusion=1; surface.alpha=1; surface.emission=_EmissionColor.rgb*_NodeVisibility;
                half3 color=UniversalFragmentPBR(data,surface).rgb;
                color=lerp(unity_FogColor.rgb,color,smoothstep(0,0.85,_NodeVisibility));
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
