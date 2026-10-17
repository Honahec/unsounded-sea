using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    /// <summary>Consumes successful resolutions; no access to hidden session intent or rule RNG.</summary>
    public sealed class BattleAudioPresentation : MonoBehaviour
    {
        private BattleDirector director;
        private BattleStage stage;
        private readonly List<Action> unsubscribe=new List<Action>();
        private readonly HashSet<ResolvedEnemyAction> presented=new HashSet<ResolvedEnemyAction>();
        private readonly Dictionary<ResolvedEnemyAction,string[]> impacts=new Dictionary<ResolvedEnemyAction,string[]>();
        private bool directed;
        private BattleView pendingBefore, pendingAfter;
        private int generation;
        private float lastSplash=-10;
        public static void Attach(BattleDirector owner)
        {
            if(!SeaAudio.Ready)return;
            var audio=owner.GetComponent<BattleAudioPresentation>() ?? owner.gameObject.AddComponent<BattleAudioPresentation>();
            audio.Bind(owner);
        }
        private void Bind(BattleDirector owner)
        {
            Unbind();director=owner;stage=owner.Stage;
            // Disable the old generic cues on this runtime instance only. Prefabs remain intact.
            if(stage.ambience!=null){stage.ambience.Stop();stage.ambience.enabled=false;}
            stage.sealCue=null;stage.impactCue=null;stage.sanityCue=null;
            director.Resolved+=Resolved;director.PresentedEnemyAction+=Presented;
            foreach(var actor in stage.Actors)
            {
                var captured=actor;
                Action<BattleActorView> emerging=a=>SeaAudio.Play("emerge",.6f,Pan(a),1);
                actor.BrokeSurface+=emerging;unsubscribe.Add(()=>{if(captured!=null)captured.BrokeSurface-=emerging;});
                var bell=actor.GetComponentInChildren<BellMotherRigidMotion>();
                if(bell!=null)
                {
                    Action<Vector3> splash=p=>Splash(Pan(captured));
                    bell.SurfaceStruck+=splash;unsubscribe.Add(()=>{if(bell!=null)bell.SurfaceStruck-=splash;});
                }
            }
            SeaAudio.Play("card",.5f);
        }
        private float Pan(BattleActorView actor)
        {
            if(actor==null||stage==null||stage.battleCamera==null)return 0;
            return Mathf.Clamp((stage.battleCamera.WorldToViewportPoint(actor.transform.position).x-.5f)*.8f,-.4f,.4f);
        }
        private void Splash(float pan)
        {
            // Two tentacles can touch in the same frame: keep one full body of water and one quiet layer.
            float gain=Time.unscaledTime-lastSplash<.12f?.35f:.8f;
            lastSplash=Time.unscaledTime;SeaAudio.Play("splash",gain,pan,2);
        }
        private void LateUpdate()
        {
            if(director==null||stage==null)return;
            bool giant=stage.Actors.Any(a=>a.IsSeaActor);
            bool target=director.scenario.encounter.objective!=ObjectiveKind.Defeat;
            var v=director.View;
            SeaAudio.World(giant||target?"boss":"deck",director.Paused || GameSceneLoader.IsLoading,0,
                v==null?.25f:Mathf.Clamp01(.2f+(v.BossStage-1)*.12f+(v.Health<20?.25f:0)+(v.Sanity<=5?.2f:0)));
        }
        private void Resolved(BattleView before,BattleView after,BattleCommand command)
        {
            generation++;int token=generation;
            if(command.kind==CommandKind.EndTurn)
            {
                presented.Clear();impacts.Clear();
                int hp=before.Health,san=before.Sanity,block=before.Block,ward=before.Ward;
                foreach(var action in after.ResolvedEnemyActions)
                {
                    impacts[action]=BattleAudioPolicy.ImpactCues(hp,san,block,ward,action);
                    hp=action.Health;san=action.Sanity;block=action.Block;ward=action.Ward;
                }
                directed=after.BossKind!=BossEncounterKind.None;
                foreach(var action in after.ResolvedEnemyActions)
                {
                    var actor=stage.Actors.FirstOrDefault(a=>a.EnemyId==action.EnemyId);
                    if(directed)StartCoroutine(DirectedRelease(actor,action,token));
                    else StartCoroutine(OrdinaryImpact(actor,action,token));
                }
            }
            else
            {
                var card=command.kind==CommandKind.PlayCard?before.Hand.FirstOrDefault(c=>c.InstanceId==command.cardInstanceId):null;
                string initialCue=card!=null?BattleAudioPolicy.CardCue(card.Definition):command.kind==CommandKind.AdvanceObjective?"seal":"insight";
                SeaAudio.Play(initialCue,.8f,0,1);
                if(after.Sanity<before.Sanity || after.Health<before.Health)SeaAudio.Play("pay",.7f,0,2);
                if(after.Health>before.Health || after.Sanity>before.Sanity)SeaAudio.Play("heal",.7f);
                if(after.Block>before.Block && initialCue!="guard")SeaAudio.Play("guard",.6f);
                if(after.Ward>before.Ward)SeaAudio.Play("ward",.5f);
                foreach(var enemy in after.Enemies)
                {
                    var old=before.Enemies.FirstOrDefault(e=>e.Id==enemy.Id);if(old==null)continue;
                    var actor=stage.Actors.FirstOrDefault(a=>a.EnemyId==enemy.Id);
                    if(enemy.Health<old.Health)SeaAudio.Play(HitMaterial(actor,enemy),.7f,Pan(actor),2);
                    if(old.Health>0 && enemy.Health<=0)SeaAudio.Play("cloth",.6f,Pan(actor));
                    if(!old.IntentConfirmed && enemy.IntentConfirmed)SeaAudio.Play("confirm",.7f,0,2);
                }
                if(after.ObjectiveProgress>before.ObjectiveProgress && initialCue!="seal")SeaAudio.Play("seal",.65f,0,2);
                if(after.Evidence.Count>before.Evidence.Count && initialCue!="insight")SeaAudio.Play("insight",.55f);
            }
            pendingBefore=before;pendingAfter=after;
        }
        private string HitMaterial(BattleActorView actor,EnemyView enemy)
        {
            if(actor!=null&&actor.IsSeaActor)return "hit_giant";
            // Public character identity only; no intent-specific equipment inspection.
            if(enemy.Id=="echo" || enemy.Style==EnemyStyle.Boss)return "hit_metal";
            return "hit_body";
        }
        private IEnumerator DirectedRelease(BattleActorView actor,ResolvedEnemyAction action,int token)
        {
            if(actor==null)yield break;
            float elapsed=0;
            while(token==generation && !presented.Contains(action))
            {
                if(director.Paused){yield return null;continue;}
                if(actor.GiantMotion!=null && actor.GiantMotion.Busy || actor.HasPendingDeckMotion)break;
                elapsed+=Time.unscaledDeltaTime;if(elapsed>45)yield break;
                yield return null;
            }
            if(actor.GiantMotion!=null)
            {
                int release=actor.GiantMotion.ReleaseCount;
                while(token==generation && !presented.Contains(action) && actor.GiantMotion.Busy && actor.GiantMotion.ReleaseCount==release)
                {
                    yield return null;
                }
            }
            // A contact event may arrive during the release frame; the release cue is still useful,
            // but must never replay its damage result.
            if(token==generation)Release(actor,action);
        }
        private void Release(BattleActorView actor,ResolvedEnemyAction action)
        {
            if(action.Kind==EnemyActionKind.Observe)return;
            string cue=action.Kind==EnemyActionKind.Heal?"heal":action.Kind==EnemyActionKind.Summon?"emerge":
                action.Channel==DamageChannel.Sanity?(actor!=null&&actor.IsSeaActor?"bell":"ritual"):
                director.View.BossKind==BossEncounterKind.Choirmaster?"pressure":
                director.View.BossKind==BossEncounterKind.Surveyor?"windstrike":"slash";
            if(actor!=null && actor.GetComponentInChildren<BellMotherRigidMotion>()!=null && action.Channel==DamageChannel.Health)
                cue="emerge";
            SeaAudio.Play(cue,.65f,Pan(actor),1);
        }
        private IEnumerator OrdinaryImpact(BattleActorView actor,ResolvedEnemyAction action,int token)
        {
            Release(actor,action);
            var bell=actor==null?null:actor.GetComponentInChildren<BellMotherRigidMotion>();
            float elapsed=0;
            yield return null;
            while(token==generation && actor!=null && elapsed<6)
            {
                if(director.Paused){yield return null;continue;}
                bool waiting=bell!=null?bell.ActiveAction=="Attack"&&bell.ActionImpact<=0:
                    actor.HasPendingDeckMotion?actor.DeckActionImpact<=0:actor.HasPendingGenericAttack&&!actor.GenericAttackAtImpact;
                if(!waiting)break;
                elapsed+=Time.unscaledDeltaTime;yield return null;
            }
            if(token!=generation)yield break;
            Presented(action);
        }
        private void Presented(ResolvedEnemyAction action)
        {
            if(action==null || !presented.Add(action))return;
            if(impacts.TryGetValue(action,out var cues))foreach(var cue in cues)SeaAudio.Play(cue,.8f,0,3);
            var actor=stage.Actors.FirstOrDefault(a=>a.EnemyId==action.EnemyId);
            if(action.Amount>0 && action.Channel==DamageChannel.Health && actor!=null && actor.IsSeaActor)
                SeaAudio.Play("hull",.7f,0,3);
        }
        public void Complete()
        {
            if(pendingAfter==null || director==null || director.Paused)return;
            var before=pendingBefore;var after=pendingAfter;pendingAfter=null;
            if(before.Phase!=after.Phase && (after.Phase==BattlePhase.Victory || after.Phase==BattlePhase.Defeat))
                SeaAudio.Play(after.Phase==BattlePhase.Defeat?"defeat":director.scenario.encounter.objective==ObjectiveKind.Survive?"retreat":"victory",.8f,0,3,true);
            else if(after.Turn>before.Turn){SeaAudio.Play("turn",.55f);SeaAudio.Play("card",.5f);}
        }
        private void Unbind()
        {
            generation++;pendingBefore=null;pendingAfter=null;StopAllCoroutines();
            if(director!=null){director.Resolved-=Resolved;director.PresentedEnemyAction-=Presented;}
            foreach(var action in unsubscribe)action();unsubscribe.Clear();impacts.Clear();presented.Clear();
        }
        private void OnDisable(){Unbind();}
    }
}
