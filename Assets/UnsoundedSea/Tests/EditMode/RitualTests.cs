using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class RitualTests
    {
        private static void Move(VoyageSession session, int layer, RunNodeKind? desired = null)
        {
            while (session.Current.Layer < layer)
            {
                if (session.Phase == VoyagePhase.ChoosingRoute)
                {
                    var next=desired.HasValue&&session.Current.Layer==layer-1?session.Choices.First(x=>x.Kind==desired.Value):session.Choices[0];
                    Assert.That(session.Select(next.Id));Assert.That(session.SetSail());Assert.That(session.Arrive());
                }
                else if (session.Phase == VoyagePhase.Reward) Assert.That(session.ClaimReward(null));
                else if (session.Phase == VoyagePhase.Arrived)
                {
                    if (session.Current.Kind == RunNodeKind.Battle) { Assert.That(session.BeginEncounter()); Assert.That(session.ResolveEncounter(true, GameBalance.MaxHealth, GameBalance.MaxSanity)); }
                    else { var choice = session.GetNodeChoices().FirstOrDefault(x => x.Enabled && (x.Id == "leave" || x.Id == "depart")) ?? session.GetNodeChoices().FirstOrDefault(x => x.Enabled); if (choice != null) Assert.That(session.ChooseNode(choice.Id)); Assert.That(session.Continue()); }
                }
            }
        }

        [Test] public void RitualLearningPreparationReplayAndExpiryAreExplicit()
        {
            var voyage = VoyageSession.Create(31);
            Move(voyage, 2, RunNodeKind.Event);
            Assert.That(voyage.ManageSupport("ritual:learn"));
            Assert.That(voyage.ManageSupport("ritual:learn"), Is.False);
            Assert.That(voyage.ManageSupport("ritual:prepare"), Is.False);
            Assert.That(voyage.Continue());
            Move(voyage, 5);
            Assert.That(voyage.ManageSupport("ritual:prepare"));
            Assert.That(voyage.Loadout.EchoRitual, Is.True);
            var restored = VoyageSession.Restore(JsonConvert.DeserializeObject<VoyageCheckpoint>(JsonConvert.SerializeObject(voyage.Export())));
            Assert.That(restored.Loadout.EchoRitual, Is.True);
            Move(restored, 6); Assert.That(restored.BeginEncounter()); Assert.That(restored.ResolveEncounter(true, 60, 30));
            Assert.That(restored.Loadout.EchoRitual, Is.False);
        }

        private static BattleDefinition Watcher(bool ritual, int sanity = 30)
        {
            var ids = new[] { "b05-watcher", "b05-guard-anchor", "b05-guard-lamp", "b05-guard-bell", "b05-guard-sail", "b05-guard-eye" };
            var idle = new IntentSpec("idle", "公开行动", "公开行动", DamageChannel.Health, 0, true);
            return new BattleDefinition("ritual-watcher", Enumerable.Range(0, 12).Select(i => new CardSpec("shot-" + i, "射击", "伤害", 1, new[] { new EffectSpec("damage", 1) })),
                ids.Select((id, i) => new EnemySpec(id, id, i == 0 ? 1 : 12, "公开", new[] { idle })), Array.Empty<ToolSpec>(), sanity: sanity, initialSanity: sanity,
                loadout: new BattleLoadout(echoRitual: ritual));
        }

        private static BattleSession ActiveWatcher(int sanity = 30)
        {
            var session = new BattleSession(Watcher(true, sanity), 0, boss: BossEncounterKind.Watcher);
            var start = session.View.Hand.Single(x => x.IsEncounter && x.Definition.Id == "E05-01");
            Assert.That(session.Execute(BattleCommand.Play(start.InstanceId)).Success);
            return session;
        }

        [Test] public void InvalidRitualTargetAndInsufficientSanityAreAtomic()
        {
            var session = ActiveWatcher(); var before = session.View;
            Assert.That(session.Execute(BattleCommand.Consume("ritual:echo", "enemy:missing")).Success, Is.False);
            Assert.That(session.View.Sanity, Is.EqualTo(before.Sanity)); Assert.That(session.View.SupplyActions.Single(x => x.Id == "ritual:echo").Uses, Is.EqualTo(1));
            var low = ActiveWatcher(3);
            Assert.That(low.View.SupplyActions.Single(x => x.Id == "ritual:echo").Enabled, Is.False);
            Assert.That(low.Execute(BattleCommand.Consume("ritual:echo", low.View.SupplyActions.Single(x => x.Id == "ritual:echo").Targets[0].Id)).Success, Is.False);
            Assert.That(low.View.Sanity, Is.EqualTo(3));
            Assert.That(BattleReplay.Fingerprint(Watcher(true)), Is.Not.EqualTo(BattleReplay.Fingerprint(Watcher(false))));
        }

        [Test] public void PreparedRitualSuppressesOneSummonAttackOnceAndReplays()
        {
            var definition = Watcher(true); var session = ActiveWatcher();
            var ritual = session.View.SupplyActions.Single(x => x.Id == "ritual:echo"); var target = ritual.Targets[0].Id; var targetName = target.Substring(6);
            Assert.That(session.Execute(BattleCommand.Consume("ritual:echo", target)).Success);
            Assert.That(session.View.Sanity, Is.EqualTo(27)); Assert.That(session.View.SupplyActions.Single(x => x.Id == "ritual:echo").Uses, Is.Zero);
            Assert.That(session.Execute(BattleCommand.Consume("ritual:echo", target)).Success, Is.False);
            Assert.That(session.Execute(BattleCommand.EndTurn()).Success);
            Assert.That(session.View.Log.Any(x => x.Contains(targetName + "执行")), Is.False, "The chosen summon must not attack.");
            var restored = BattleReplay.Capture(session).Restore(definition);
            Assert.That(restored.View.Sanity, Is.EqualTo(session.View.Sanity)); Assert.That(restored.View.SupplyActions.Single(x => x.Id == "ritual:echo").Uses, Is.Zero);
            Assert.That(restored.View.Enemies[0].Targetable, Is.False, "Ritual must not alter unrelated boss invulnerability.");
        }
    }
}
