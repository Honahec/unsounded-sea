Shader "UnsoundedSea/Abyss Ocean"
{
    Properties { _DeepColor("Deep", Color)=(0.012,0.045,0.052,1) _CrestColor("Crest", Color)=(0.12,0.24,0.24,1) _SeaTime("Sea time", Float)=0 _Threat("Sea actor XZ, radius, impact", Vector)=(0,0,0,0) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _DeepColor, _CrestColor, _Threat; float _SeaTime;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float fog:TEXCOORD1; };
            float Wake(float2 p)
            {
                float d=distance(p,_Threat.xy);
                return step(0.01,_Threat.z)*exp(-abs(d-_Threat.z)*0.17)*sin(d*1.5-_SeaTime*1.8)*(0.28+_Threat.w*0.3);
            }
            float Wave(float2 p) { return sin(p.x*0.7+p.y*0.32+_SeaTime*0.8)*0.21 + sin(p.y*1.3-p.x*0.22-_SeaTime*0.7)*0.09 + sin(p.x*2.8+p.y*1.7+_SeaTime)*0.026 + Wake(p); }
            Varyings Vert(Attributes v) { Varyings o; o.world=TransformObjectToWorld(v.positionOS.xyz); o.world.y+=Wave(o.world.xz); o.positionCS=TransformWorldToHClip(o.world); o.fog=ComputeFogFactor(o.positionCS.z); return o; }
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.world.xz; float h=Wave(p);
                float3 n=normalize(float3((h-Wave(p+float2(0.04,0)))/0.04,1,(h-Wave(p+float2(0,0.04)))/0.04));
                float3 v=normalize(_WorldSpaceCameraPos-i.world);
                float fresnel=pow(1-saturate(dot(n,v)),4);
                Light sun=GetMainLight(); float spec=pow(saturate(dot(n,normalize(v+sun.direction))),170);
                float ridges=pow(saturate(h*2.7+0.28),8)*0.13;
                float3 col=lerp(_DeepColor.rgb,_CrestColor.rgb,fresnel*0.65+0.12)+sun.color*spec*0.65+ridges*float3(0.2,0.32,0.3);
                float foam=smoothstep(0.14,0.3,Wake(p))* (0.45+0.55*sin(p.x*2+p.y*3)*sin(p.y*1.9));
                col=lerp(col,float3(0.29,0.4,0.38),foam*0.6);
                return half4(MixFog(col,i.fog),1);
            }
            ENDHLSL
        }
    }
}
