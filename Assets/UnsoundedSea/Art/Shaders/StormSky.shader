Shader "UnsoundedSea/Storm Sky"
{
    Properties
    {
        _Horizon("Fog horizon", Color)=(0.3,0.45,0.48,1)
        _Zenith("Night zenith", Color)=(0.05,0.09,0.13,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Horizon, _Zenith;
            CBUFFER_END
            struct V {float4 p:SV_POSITION;float3 d:TEXCOORD0;};
            V Vert(float4 p:POSITION){V o;o.p=TransformObjectToHClip(p.xyz);o.d=p.xyz;return o;}
            float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 Frag(V i):SV_Target
            {
                float3 d=normalize(i.d);float h=saturate(d.y);
                float2 p=d.xz/max(0.15,d.y+0.32)*2.1;
                float cloud=Noise(p)*0.58+Noise(p*2.03)*0.28+Noise(p*4.1)*0.14;
                float cover=smoothstep(0.32,0.74,cloud)*smoothstep(0,0.15,h);
                float3 col=lerp(_Horizon.rgb,_Zenith.rgb,pow(h,0.45));
                col*=1-cover*0.58;
                float moonDot=saturate(dot(d,normalize(float3(-0.4,0.32,1))));
                col+=(pow(moonDot,1800)*float3(0.38,0.5,0.49)+pow(moonDot,38)*0.06)*(1-cover*0.8);
                return half4(col,1);
            }
            ENDHLSL
        }
    }
}
