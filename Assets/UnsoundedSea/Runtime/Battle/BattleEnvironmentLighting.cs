using UnityEngine;
using UnityEngine.Rendering;

namespace UnsoundedSea.Runtime
{
    /// <summary>Explicit scene-independent lighting for a stage, restored when leaving it.</summary>
    public sealed class BattleEnvironmentLighting : MonoBehaviour
    {
        public Color sky = new Color(.18f,.25f,.28f);
        public Color equator = new Color(.10f,.15f,.16f);
        public Color ground = new Color(.025f,.04f,.045f);
        public Cubemap reflection;
        [Range(0,1)] public float reflectionIntensity = .35f;
        private AmbientMode previousMode;
        private DefaultReflectionMode previousReflectionMode;
        private Color previousSky, previousEquator, previousGround;
        private Texture previousReflection;
        private float previousIntensity;
        private void OnEnable()
        {
            previousMode=RenderSettings.ambientMode; previousSky=RenderSettings.ambientSkyColor;
            previousEquator=RenderSettings.ambientEquatorColor; previousGround=RenderSettings.ambientGroundColor;
            previousReflectionMode=RenderSettings.defaultReflectionMode;
            previousReflection=RenderSettings.customReflectionTexture; previousIntensity=RenderSettings.reflectionIntensity;
            RenderSettings.ambientMode=AmbientMode.Trilight; RenderSettings.ambientSkyColor=sky;
            RenderSettings.ambientEquatorColor=equator; RenderSettings.ambientGroundColor=ground;
            if(reflection!=null){RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;RenderSettings.customReflectionTexture=reflection;}
            RenderSettings.reflectionIntensity=reflectionIntensity;
        }
        private void OnDisable()
        {
            RenderSettings.ambientMode=previousMode;RenderSettings.ambientSkyColor=previousSky;
            RenderSettings.ambientEquatorColor=previousEquator;RenderSettings.ambientGroundColor=previousGround;
            RenderSettings.defaultReflectionMode=previousReflectionMode;RenderSettings.customReflectionTexture=previousReflection;
            RenderSettings.reflectionIntensity=previousIntensity;
        }
    }
}
