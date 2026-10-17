using System;
using UnityEngine;

namespace UnsoundedSea.Runtime
{
    public enum SeaAudioBus { Music, Ambience, Effects, Interface }
    [Serializable] public sealed class SeaAudioCue
    {
        public string id;
        public AudioClip[] clips;
        [Range(0, 1)] public float gain = .7f;
    }
    [Serializable] public sealed class SeaMusic
    {
        public string id;
        public AudioClip[] stems;
    }
    [CreateAssetMenu(menuName = "Unsounded Sea/Audio Bank")]
    public sealed class SeaAudioBank : ScriptableObject
    {
        public SeaAudioCue[] cues = Array.Empty<SeaAudioCue>();
        public SeaMusic[] music = Array.Empty<SeaMusic>();
        public AudioClip sea, wind, wake;
    }
}
