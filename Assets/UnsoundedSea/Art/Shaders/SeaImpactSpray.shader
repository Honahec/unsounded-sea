Shader "UnsoundedSea/Impact Spray"
{
    Properties { _Color("Spray albedo", Color) = (0.65,0.78,0.75,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+15" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float3 world:TEXCOORD1; float fog:TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.world = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.world);
                o.uv = input.uv; o.color = input.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 uv = i.uv*2-1;
                float edge = saturate(1-dot(uv,uv));
                float noise = Noise(i.uv*9)*0.65+Noise(i.uv*23)*0.35;
                float alpha = edge*(0.3+0.7*smoothstep(0.2,0.65,noise))*i.color.a*_Color.a;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS)),_ZBufferParams);
                float eyeDepth = -TransformWorldToView(i.world).z;
                alpha *= saturate((sceneDepth-eyeDepth)/1.5);
                Light light = GetMainLight();
                float3 lighting = max(0,SampleSH(float3(0,1,0)))+light.color*saturate(light.direction.y)*0.7;
                return half4(MixFog(i.color.rgb*_Color.rgb*lighting,i.fog),alpha);
            }
            ENDHLSL
        }
    }
}
