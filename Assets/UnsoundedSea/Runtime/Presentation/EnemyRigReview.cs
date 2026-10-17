using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace UnsoundedSea.Runtime
{
    public sealed class EnemyRigReview : MonoBehaviour
    {
        [Serializable]
        public sealed class ActorEntry
        {
            public string label;
            public Transform actor;
            public AnimationClip idle;
            public AnimationClip action;
            public AnimationClip hit;
            public AnimationClip death;
            public AnimationClip rigValidation;

            public AnimationClip Clip(int index)
            {
                switch (index)
                {
                    case 0: return idle;
                    case 1: return action;
                    case 2: return hit;
                    case 3: return death;
                    case 4: return rigValidation;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        private static readonly string[] ClipNames = { "Idle", "Action", "Hit", "Death", "RigValidation" };

        public Camera reviewCamera;
        public ActorEntry[] actors;

        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private int selectedActor;
        private int selectedClip;
        private bool playing = true;

        public bool IsPlaying => playing;
        public int SelectedActor => selectedActor;
        public int SelectedClip => selectedClip;
        public double Time => graph.IsValid() ? playable.GetTime() : 0;

        private AnimationClip CurrentClip => actors[selectedActor].Clip(selectedClip);
        private bool CurrentClipLoops => selectedClip == 0 || selectedClip == 4;

        private void Awake()
        {
            if (actors == null || actors.Length == 0)
                throw new InvalidOperationException("Rig review requires at least one actor.");

            for (var i = 0; i < actors.Length; i++)
            {
                var entry = actors[i];
                if (entry == null || !entry.actor)
                    throw new InvalidOperationException("Rig review actor is missing.");
                for (var clip = 0; clip < ClipNames.Length; clip++)
                    if (!entry.Clip(clip))
                        throw new InvalidOperationException(entry.actor.name + " is missing " + ClipNames[clip] + ".");
                entry.actor.gameObject.SetActive(i == 0);
            }

            BuildPlayable();
        }

        private void Update()
        {
            if (!playing || !graph.IsValid()) return;
            var length = CurrentClip.length;
            var time = playable.GetTime();
            if (time < length) return;

            if (CurrentClipLoops)
            {
                playable.SetTime(time % length);
                graph.Evaluate(0);
            }
            else
            {
                playable.SetTime(length);
                graph.Evaluate(0);
                SetPlaying(false);
            }
        }

        private void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        public void SelectActor(int index)
        {
            if (actors == null || actors.Length == 0) return;
            index = (index % actors.Length + actors.Length) % actors.Length;
            if (index == selectedActor) return;
            actors[selectedActor].actor.gameObject.SetActive(false);
            selectedActor = index;
            actors[selectedActor].actor.gameObject.SetActive(true);
            BuildPlayable();
        }

        public void PreviousEnemy() => SelectActor(selectedActor - 1);
        public void NextEnemy() => SelectActor(selectedActor + 1);

        public void SelectClip(int index)
        {
            if (index < 0 || index >= ClipNames.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (index == selectedClip) return;
            selectedClip = index;
            BuildPlayable();
        }

        public void SetPlaying(bool value)
        {
            playing = value;
            if (!graph.IsValid()) return;
            if (value) graph.Play(); else graph.Stop();
        }

        public void SetNormalizedTime(float value)
        {
            if (!graph.IsValid()) return;
            value = CurrentClipLoops ? Mathf.Repeat(value, 1f) : Mathf.Clamp01(value);
            playable.SetTime(value * CurrentClip.length);
            graph.Evaluate(0);
        }

        public void Restart()
        {
            SetNormalizedTime(0);
            SetPlaying(true);
        }

        public void SetView(int view)
        {
            var yaw = view == 1 ? 90f : view == 2 ? 180f : 0f;
            actors[selectedActor].actor.localRotation = Quaternion.Euler(0, 180f + yaw, 0);
        }

        private void BuildPlayable()
        {
            if (graph.IsValid()) graph.Destroy();
            graph = PlayableGraph.Create("Enemy rig review");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            playable = AnimationClipPlayable.Create(graph, CurrentClip);
            playable.SetApplyFootIK(false);
            var animator = actors[selectedActor].actor.GetComponent<Animator>()
                ?? actors[selectedActor].actor.gameObject.AddComponent<Animator>();
            AnimationPlayableOutput.Create(graph, "Rig review", animator).SetSourcePlayable(playable);
            playing = true;
            graph.Play();
            graph.Evaluate(0);
        }

        private void OnGUI()
        {
            var scale = Mathf.Clamp(Screen.dpi > 0 ? Screen.dpi / 96f : 1f, 1f, 2f);
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var width = 590f;
            GUILayout.BeginArea(new Rect(16, 16, width, 132), GUI.skin.box);
            GUILayout.Label((actors[selectedActor].label ?? actors[selectedActor].actor.name) + " — " + ClipNames[selectedClip]);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous")) PreviousEnemy();
            if (GUILayout.Button("Next")) NextEnemy();
            if (GUILayout.Button(playing ? "Pause" : "Play")) SetPlaying(!playing);
            if (GUILayout.Button("Restart")) Restart();
            if (GUILayout.Button("Front")) SetView(0);
            if (GUILayout.Button("Side")) SetView(1);
            if (GUILayout.Button("Back")) SetView(2);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            for (var i = 0; i < ClipNames.Length; i++)
                if (GUILayout.Button(ClipNames[i])) SelectClip(i);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.matrix = oldMatrix;
        }
    }
}
