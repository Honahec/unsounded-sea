using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;

// Compare actual GPU wave evaluation with vessel samples. Temporary compute asset is removed afterwards.
public static class StormSeaQA
{
    public static object Samples()
    {
        var water=UnityEngine.Object.FindAnyObjectByType<BattleDirector>().Stage.water;
        var mat=water.surface.sharedMaterial;
        if(mat.IsKeywordEnabled("_ENABLEREFRACTION")||mat.GetFloat("_ShoreFade")!=0||mat.GetColor("_Color_Shallow").a!=1)
            throw new Exception("Storm water still permits shallow transparency/refraction");
        const string folder="Assets/Logs/StormSeaQA";
        if(AssetDatabase.IsValidFolder(folder))throw new Exception("Temporary QA folder already exists");
        AssetDatabase.CreateFolder("Assets/Logs","StormSeaQA");
        ComputeBuffer positions=null,results=null;
        try
        {
            File.WriteAllText(folder+"/Sample.compute",@"
#pragma kernel Sample
#define _ENABLEWAVE 1
float _StormStrength, _SeaTime;
float4 _StormWaveA, _StormWaveB, _StormWaveC, _StormWaveD, _Threat;
float4 _BaseA, _BaseB, _DirA, _DirB;
#include ""../../UnsoundedSea/Art/Shaders/BattleWaterWake.hlsl""
StructuredBuffer<float3> Positions;
RWStructuredBuffer<float3> Results;
float3 BaseWave(float3 p,float4 w,float3 dir)
{
    float k=6.283185/w.x; dir=normalize(dir);
    float phase=dot(dir,p)*k-sqrt(9.8*k)*_SeaTime*w.z;
    return float3(0,w.y*cos(phase),0)-dir*(w.w/k*sin(phase));
}
[numthreads(64,1,1)]
void Sample(uint3 id:SV_DispatchThreadID)
{
    float2 gradient;float3 p=Positions[id.x];
    Results[id.x]=BaseWave(p,_BaseA,_DirA.xyz)+BaseWave(p,_BaseB,_DirB.xyz)
        +float3(0,UnsoundedSeaSurface(p.xz,_Threat,_SeaTime,gradient),0);
}");
            AssetDatabase.ImportAsset(folder+"/Sample.compute",ImportAssetOptions.ForceSynchronousImport);
            var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(folder+"/Sample.compute");
            shader.SetFloat("_SeaTime",water.SeaTime);shader.SetFloat("_StormStrength",mat.GetFloat("_StormStrength"));
            foreach(var suffix in new[]{"A","B","C","D"})shader.SetVector("_StormWave"+suffix,mat.GetVector("_StormWave"+suffix));
            var block=new MaterialPropertyBlock();water.surface.GetPropertyBlock(block);shader.SetVector("_Threat",block.GetVector("_Threat"));
            shader.SetVectorArray("_SeaImpacts",block.GetVectorArray("_SeaImpacts"));
            void Base(string prefix,string suffix)
            {
                shader.SetVector("_Base"+suffix,new Vector4(mat.GetFloat(prefix+"Length"),mat.GetFloat(prefix+"Height"),mat.GetFloat(prefix+"Speed"),mat.GetFloat(prefix+"Sharpness")));
                shader.SetVector("_Dir"+suffix,mat.GetVector(prefix+"Direction"));
            }
            Base("_1st_Wave_","A");Base("_2nd_Wave_","B");
            var samples=new Vector3[64];var gpu=new Vector3[64];
            for(int i=0;i<64;i++)samples[i]=new Vector3((i%8-3)*23.7f,0,(i/8)*47.3f-25);
            positions=new ComputeBuffer(64,12);results=new ComputeBuffer(64,12);positions.SetData(samples);
            int kernel=shader.FindKernel("Sample");shader.SetBuffer(kernel,"Positions",positions);shader.SetBuffer(kernel,"Results",results);
            shader.Dispatch(kernel,1,1,1);results.GetData(gpu);
            float maxError=0;
            for(int i=0;i<64;i++)
            {
                float error=Vector3.Distance(gpu[i],water.SampleDisplacement(samples[i]));
                if(float.IsNaN(error)||float.IsInfinity(error))throw new Exception("Non-finite wave sample "+i);
                maxError=Mathf.Max(maxError,error);
            }
            if(float.IsNaN(maxError)||maxError>0.005f)throw new Exception("CPU/GPU water sample mismatch: "+maxError);
            return new{points=64,maxErrorMetres=maxError,opaqueWater=true,clock=water.SeaTime,impacts=water.ImpactCount};
        }
        finally {positions?.Release();results?.Release();AssetDatabase.DeleteAsset(folder);}
    }
}
