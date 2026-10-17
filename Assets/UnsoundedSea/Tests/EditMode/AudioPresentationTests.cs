using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class AudioPresentationTests
    {
        [Test] public void FullPhysicalBlockDoesNotPlayBodyDamage()
        {
            var a=new ResolvedEnemyAction("enemy",DamageChannel.Health,8,60,30,2,0);
            Assert.That(BattleAudioPolicy.ImpactCues(60,30,10,0,a),Is.EqualTo(new[]{"block"}));
        }
        [Test] public void PartialBlockPlaysAbsorptionAndActualDamage()
        {
            var a=new ResolvedEnemyAction("enemy",DamageChannel.Health,8,55,30,0,0);
            Assert.That(BattleAudioPolicy.ImpactCues(60,30,3,0,a),Is.EqualTo(new[]{"block","hit_body"}));
        }
        [Test] public void FullSanityProtectionDoesNotPlaySanityDamage()
        {
            var a=new ResolvedEnemyAction("enemy",DamageChannel.Sanity,6,60,30,0,2);
            Assert.That(BattleAudioPolicy.ImpactCues(60,30,0,8,a),Is.EqualTo(new[]{"ward"}));
        }
        [Test] public void BothResourcesCanHaveDistinctFeedback()
        {
            var a=new ResolvedEnemyAction("enemy",DamageChannel.Sanity,6,58,24,0,0);
            Assert.That(BattleAudioPolicy.ImpactCues(60,30,0,0,a),Is.EqualTo(new[]{"hit_body","sanity"}));
        }
        [Test] public void NonDamageActionsCannotProduceFalsePlayerDamage()
        {
            foreach(var kind in new[]{EnemyActionKind.Heal,EnemyActionKind.Summon,EnemyActionKind.Observe})
                Assert.That(BattleAudioPolicy.ImpactCues(60,30,10,8,new ResolvedEnemyAction("e",DamageChannel.Health,5,60,30,0,0,kind)),Is.Empty);
        }
        [Test] public void SequentialSnapshotsDoNotRepeatEarlierDamage()
        {
            var first=new ResolvedEnemyAction("a",DamageChannel.Health,8,55,30,0,0);
            var second=new ResolvedEnemyAction("b",DamageChannel.Sanity,6,55,24,0,0);
            Assert.That(BattleAudioPolicy.ImpactCues(first.Health,first.Sanity,first.Block,first.Ward,second),Is.EqualTo(new[]{"sanity"}));
        }
        [Test] public void GunSelectionUsesStableCardIdNotLocalizedName()
        {
            var renamed=new CardSpec("revolver","任意翻译","",1,new[]{new EffectSpec("damage",6)});
            Assert.That(BattleAudioPolicy.CardCue(renamed),Is.EqualTo("shot"));
        }
        [Test] public void BankContainsAllRuntimeCuesAndAlignedMusic()
        {
            var bank=AssetDatabase.LoadAssetAtPath<SeaAudioBank>("Assets/UnsoundedSea/Resources/SeaAudioBank.asset");
            Assert.That(bank,Is.Not.Null);
            foreach(var id in new[]{"shot","slash","hit_body","hit_metal","hit_giant","block","hull","card","cloth","creak","lap","splash","confirm","mechanism","heal","insight","seal","guard","pay","ward","bell","ritual","victory","retreat","defeat","turn","reject","emerge","pressure","windstrike","sanity"})
                Assert.That(bank.cues.Any(c=>c.id==id&&c.clips.Length>0&&c.clips.All(x=>x!=null)),Is.True,id);
            Assert.That(bank.music.Length,Is.EqualTo(4));
            foreach(var track in bank.music)
            {
                Assert.That(track.stems.Length,Is.EqualTo(3));
                Assert.That(track.stems.All(c=>c!=null&&c.samples==track.stems[0].samples&&c.frequency==48000),Is.True,track.id);
                Assert.That(track.stems[0].length,Is.GreaterThanOrEqualTo(90));
            }
        }
        [Test] public void ExplicitAudioSourcesAreNotScaledTwiceByLegacySettings()
        {
            var go=new GameObject("Managed audio test");
            try
            {
                go.AddComponent<SeaAudioVoice>();var s=go.AddComponent<AudioSource>();s.volume=.37f;
                GameSettings.ApplyAudioSource(s,true);GameSettings.ApplyAudioSource(s,false);
                Assert.That(s.volume,Is.EqualTo(.37f).Within(.00001));
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
