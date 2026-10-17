using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnsoundedSea.Tests
{
    public sealed class BellMotherAnimationTests
    {
        const string PrefabPath = "Assets/UnsoundedSea/Art/Prefabs/BellMother.prefab";
        static readonly string[] RequiredActions =
        {
            "BellMother_Idle", "BellMother_Telegraph", "BellMother_Attack_A",
            "BellMother_Recover", "BellMother_Hit", "BellMother_Death"
        };

        [Test]
        public void AnimatedPrefabContainsBattleControllerAndClips()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "Run BellMotherAnimationSetup.Build first.");
            var animator = prefab.GetComponentInChildren<Animator>();
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
            var clips = animator.runtimeAnimatorController.animationClips.Select(x => x.name).ToArray();
            foreach (var action in RequiredActions)
                Assert.That(clips.Any(x => x == action || x.EndsWith(action)), Is.True, action);
            var skin = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
            Assert.That(skin, Is.Not.Null);
            Assert.That(skin.sharedMaterial, Is.Not.Null);
            Assert.That(skin.sharedMaterial.name, Is.EqualTo("BellMother"));
        }

        [Test]
        public void AnimatedPrefabUsesGenericArmatureDeformation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var skin = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
            Assert.That(skin, Is.Not.Null);
            Assert.That(skin.rootBone, Is.Not.Null);
            Assert.That(skin.bones.Length, Is.GreaterThanOrEqualTo(19));
        }

        [Test]
        public void AttackClipChangesImportedBonePose()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(prefab);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                var bone = instance.GetComponentsInChildren<Transform>(true)
                    .First(x => x.name == "tentacle_front_left_01");
                animator.Play("BellMother_Attack_A", 0, 0.5f);
                var before = bone.localRotation;
                animator.Update(0.4f);
                Assert.That(Quaternion.Angle(before, bone.localRotation), Is.GreaterThan(0.01f));
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
