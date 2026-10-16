Shader "UnsoundedSea/Fog Sky"
{
    Properties { _Horizon("Horizon", Color)=(0.09,0.16,0.18,1) _Zenith("Zenith", Color)=(0.008,0.018,0.03,1) }
    SubShader { Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" } Cull Off ZWrite Off
        Pass { HLSLPROGRAM
        #pragma vertex Vert
        #pragma fragment Frag
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        float4 _Horizon, _Zenith;
        struct V {float4 p:SV_POSITION; float3 d:TEXCOORD0;};
        V Vert(float4 p:POSITION) {V o; o.p=TransformObjectToHClip(p.xyz); o.d=p.xyz; return o;}
        half4 Frag(V i):SV_Target {float3 d=normalize(i.d); float t=pow(saturate(d.y),0.45); float moon=pow(saturate(dot(d,normalize(float3(-0.4,0.32,1)))),1800); float halo=pow(saturate(dot(d,normalize(float3(-0.4,0.32,1)))),35); return half4(lerp(_Horizon.rgb,_Zenith.rgb,t)+moon*float3(0.6,0.75,0.72)+halo*0.03,1);}
        ENDHLSL }
    }
}
