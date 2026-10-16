#ifndef UNSOUNDED_SEA_WATER_WAKE_INCLUDED
#define UNSOUNDED_SEA_WATER_WAKE_INCLUDED

// Four crossing, phase-warped swells. Wave = heading, wavelength, amplitude, speed.
// CPU counterpart lives in BattleWater so the ship follows the rendered sea.
float UnsoundedStormWave(float2 p, float time, float4 wave, float offset, out float2 gradient)
{
    float2 dir = float2(cos(wave.x), sin(wave.x));
    float2 side = float2(-dir.y, dir.x);
    float k = 6.283185 / max(0.01, wave.y);
    float warp = dot(side, p) * k * 0.63 + time * 0.19 + offset;
    float phase = dot(dir, p) * k - sqrt(9.8 * k) * time * wave.w + offset + 0.85 * sin(warp);
    gradient = -wave.z / 1.22 * (sin(phase) + 0.44 * sin(2 * phase))
        * (dir * k + side * (0.85 * cos(warp) * k * 0.63));
    return wave.z * (cos(phase) + 0.22 * cos(2 * phase)) / 1.22;
}

float UnsoundedStorm(float2 p, float time, out float2 gradient)
{
    float2 a,b,c,d;
    float h = UnsoundedStormWave(p,time,_StormWaveA,0.7,a)
        + UnsoundedStormWave(p,time,_StormWaveB,2.3,b)
        + UnsoundedStormWave(p,time,_StormWaveC,4.1,c)
        + UnsoundedStormWave(p,time,_StormWaveD,5.8,d);
    gradient = (a+b+c+d)*_StormStrength;
    return h*_StormStrength;
}

float UnsoundedSeaWake(float2 worldXZ, float4 threat, float time)
{
    float2 delta = worldXZ-threat.xy;
    float d=length(delta), angle=atan2(delta.y,delta.x);
    float r=d-threat.z+12*sin(angle*3+time*0.17)+7*sin(angle*7-time*0.11);
    float envelope=step(0.01,threat.z)*exp(-abs(r)*0.018)*smoothstep(20,65,d);
    return envelope*(sin(r*0.16-time*0.75+sin(angle*4)*0.8)*1.4+sin(r*0.29+time*0.61+angle*3)*0.65);
}

float UnsoundedSeaSurface(float2 p, float4 threat, float time, out float2 gradient)
{
#if !defined(_ENABLEWAVE)
    gradient=0;
    return 0;
#else
    float h=UnsoundedStorm(p,time,gradient);
    float wake=UnsoundedSeaWake(p,threat,time);
    gradient+=float2(UnsoundedSeaWake(p+float2(0.5,0),threat,time)-UnsoundedSeaWake(p-float2(0.5,0),threat,time),
        UnsoundedSeaWake(p+float2(0,0.5),threat,time)-UnsoundedSeaWake(p-float2(0,0.5),threat,time));
    return h+wake;
#endif
}

float SeaHash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float SeaNoise(float2 p)
{
    float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(SeaHash(i),SeaHash(i+float2(1,0)),f.x),lerp(SeaHash(i+float2(0,1)),SeaHash(i+1),f.x),f.y);
}

float UnsoundedSeaFoamMask(float2 p, float4 threat, float time)
{
#if !defined(_ENABLEWAVE)
    return 0;
#else
    float2 gradient;
    float crest=UnsoundedStorm(p,time,gradient);
    float n=SeaNoise(p*0.14+float2(-time*0.15,time*0.09));
    float fine=SeaNoise(p*0.72+float2(time*0.27,-time*0.18));
    float whitecap=smoothstep(0.6,1.65,crest+n*1.1)*smoothstep(0.45,0.78,n*0.6+fine*0.4);
    float d=distance(p,threat.xy);
    float disturbance=exp(-abs(d-threat.z)*0.021)*step(0.01,threat.z);
    float churn=smoothstep(0.35,1.3,UnsoundedSeaWake(p,threat,time))*smoothstep(0.38,0.72,n+fine*0.25);
    return saturate(whitecap*_StormStrength+churn*disturbance*0.85);
#endif
}
#endif
