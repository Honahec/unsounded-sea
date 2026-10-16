Shader "UnsoundedSea/Voyage Wake"
{
    Properties { _Color("Foam albedo", Color) = (0.55,0.66,0.64,0.65) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _WakeTime;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; float4 color:COLOR; float fog:TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.world);
                output.uv=input.uv; output.color=input.color;
                output.fog=ComputeFogFactor(output.positionCS.z); return output;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 Frag(Varyings input):SV_Target
            {
                float across=abs(input.uv.x*2-1);
                float n=Noise(float2(input.uv.x*15,input.uv.y*3-_WakeTime*0.25));
                float fine=Noise(input.world.xz*1.6+_WakeTime*0.13);
                float wash=(1-smoothstep(0.15,0.85,across))*0.45;
                float arms=exp(-pow((across-0.67)*9,2))*0.6;
                float alpha=(wash+arms)*smoothstep(0.25,0.8,n*0.65+fine*0.35)*input.color.a*_Color.a;
                Light light=GetMainLight(TransformWorldToShadowCoord(input.world));
                float3 lighting=max(0,SampleSH(float3(0,1,0)))+light.color*saturate(light.direction.y)*light.shadowAttenuation;
                return half4(MixFog(_Color.rgb*lighting,input.fog),alpha);
            }
            ENDHLSL
        }
    }
}
