using UnityEngine;

namespace UnsoundedSea.Runtime
{
    public sealed class BattleActorView : MonoBehaviour
    {
        public string EnemyId { get; private set; }
        public Vector3 LabelPosition => transform.TransformPoint(labelPosition);
        public bool Alive { get; private set; } = true;
        public bool IsEmerging => isActiveAndEnabled && Alive && emergence < 1;
        public float HighestPointY => transform.TransformPoint(Vector3.up*(emergenceTop+emergenceOffset)).y;
        public event System.Action<BattleActorView> BrokeSurface;
        public bool IsSeaActor => placement != null && placement.environment == BattleEnvironment.Sea;
        public float WaterRadius => IsSeaActor ? placement.waterRadius : 0;
        private Transform body;
        private LineRenderer ring;
        internal bool HasSelectionRing => ring != null;
        private float ringRadius;
        private BattleDirector director;
        private BattleAnchor placement;
        private Vector3 bodyPosition, labelPosition;
        private Quaternion bodyRotation;
        private float height, impulse, attack, fall, age, emergence = 1, emergenceTop, emergenceOffset;
        private bool surfaceBroken;
        private System.Func<Vector3,float> surfaceHeight;
        private Animator animator;
        private BellMotherRigidMotion rigidMotion;
        private DeckActorMotion deckMotion;
        public GiantBossMotion GiantMotion { get; private set; }
        public bool HasPendingDeckMotion => deckMotion != null && deckMotion.Busy;
        public bool HasPendingRetirement => withdrawalPending || rigidMotion != null && rigidMotion.HasPendingRetirement;
        public float RetirementProgress => GiantMotion != null ? GiantMotion.RetirementProgress : rigidMotion != null ? rigidMotion.RetirementProgress : 0;
        private bool withdrawalPending;
        public float DeckActionImpact => deckMotion == null ? 0 : deckMotion.ActionImpact;
        internal bool HasPendingGenericAttack => GiantMotion == null && deckMotion == null && !skeletalPresentation && !rigidPresentation && attack > 0;
        internal bool GenericAttackAtImpact => attack <= .5f;
        private Coroutine actionSequence;
        private bool skeletalPresentation;
        private bool rigidPresentation;
        private bool legacyMotion;

        public bool HasSkeletalPresentation => skeletalPresentation || rigidPresentation || deckMotion != null || GiantMotion != null;

        public void Bind(BattleDirector owner, string id, GameObject prefab, Material ringMaterial)
        {
            director = owner; EnemyId = id;
            placement = transform.parent.GetComponent<BattleAnchor>();
            body = Instantiate(prefab, transform).transform;
            bodyPosition = body.localPosition; bodyRotation = body.localRotation;
            animator = body.GetComponentInChildren<Animator>();
            rigidMotion = body.GetComponentInChildren<BellMotherRigidMotion>();
            GiantMotion = IsSeaActor ? body.GetComponentInChildren<GiantBossMotion>() : null;
            if (GiantMotion != null) GiantMotion.Initialize();
            deckMotion = IsSeaActor ? null : body.GetComponentInChildren<DeckActorMotion>();
            if (deckMotion != null) deckMotion.Initialize(id);
            skeletalPresentation = animator != null && animator.runtimeAnimatorController != null;
            rigidPresentation = rigidMotion != null;
            if (skeletalPresentation)
            {
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.applyRootMotion = false;
                animator.Play("BellMother_Idle", 0, 0);
            }
            else if (rigidPresentation) rigidMotion.ResetIdle();
            var bounds = MeasureLocalBounds(transform);
            height = bounds.size.y;
            emergenceTop = bounds.max.y;
            if (placement != null && placement.emergeDepth > 0)
            {
                emergence = 0; surfaceBroken = false; emergenceOffset = -placement.emergeDepth;
                body.localPosition = bodyPosition + Vector3.up * emergenceOffset;
            }
            labelPosition = new Vector3(bounds.center.x, Mathf.Lerp(bounds.min.y, bounds.max.y, placement == null ? 1 : placement.labelHeight), bounds.center.z)
                + (placement == null ? new Vector3(0, 0.28f, 0) : placement.labelOffset);
            // Bounds are measured in actor space, so a 30 m sea creature is selectable across its body,
            // and rotating/scaling its anchor does not accidentally scale a world-space collider twice.
            var hit = gameObject.AddComponent<BoxCollider>();
            hit.center = bounds.center; hit.size = Vector3.Max(bounds.size, Vector3.one * 0.1f);
            if (placement != null && !placement.showSelectionRing) return;
            ring = new GameObject("Target sigil").AddComponent<LineRenderer>(); ring.transform.SetParent(transform, false);
            ring.sharedMaterial = ringMaterial; ring.useWorldSpace = false; ring.loop = true;
            ring.widthMultiplier = 0.025f; ring.positionCount = 64;
            ringRadius = Mathf.Max(0.7f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            for (var i = 0; i < 64; i++) { var a = i * Mathf.PI / 32; ring.SetPosition(i, new Vector3(Mathf.Cos(a) * ringRadius, 0.035f, Mathf.Sin(a) * ringRadius)); }
            Select(false);
        }

        public void ConfigureMotion(bool enableLegacyMotion)
        {
            legacyMotion = enableLegacyMotion;
        }
        public void ConfigureSeaEmergence(System.Func<Vector3,float> heightAt)
        {
            if(placement==null||placement.emergeDepth<=0||heightAt==null)return;
            surfaceHeight=heightAt;
            var surface=heightAt(transform.position);
            var normalTop=transform.TransformPoint(Vector3.up*emergenceTop).y;
            var scale=Mathf.Max(.001f,Mathf.Abs(transform.lossyScale.y));
            placement.emergeDepth=Mathf.Max(placement.emergeDepth,(normalTop-surface+1)/scale);
            emergence=0;surfaceBroken=false;emergenceOffset=-placement.emergeDepth;
            body.localPosition=bodyPosition+Vector3.up*emergenceOffset;
        }
        private void CheckSurfaceBreak()
        {
            if(surfaceBroken||surfaceHeight==null||HighestPointY<surfaceHeight(transform.position))return;
            surfaceBroken=true;BrokeSurface?.Invoke(this);
        }

        public static Bounds MeasureLocalBounds(Transform root)
        {
            var bounds = new Bounds(); bool first = true;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                var local = renderer.localBounds;
                // A skin's culling bounds can be expressed relative to its root bone and
                // deliberately padded for motion. Use baked geometry for selection/labels.
                if (renderer is SkinnedMeshRenderer skin)
                {
                    var baked = new Mesh();
                    try { skin.BakeMesh(baked, true); baked.RecalculateBounds(); local = baked.bounds; }
                    finally { if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked); }
                }
                for (var i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = root.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                }
            }
            return bounds;
        }

        public void Select(bool selected) { if (ring != null) ring.enabled = selected && Alive; }
        internal void ShowCardTarget(bool selected)
        {
            if (ring == null) return;
            ring.enabled = Alive;
            ring.widthMultiplier = selected ? 0.06f : 0.025f;
        }
        internal bool HitSelectionRing(Ray ray)
        {
            if (ring == null || !ring.enabled || !Alive) return false;
            var plane = new Plane(transform.up, transform.TransformPoint(new Vector3(0, 0.035f, 0)));
            if (!plane.Raycast(ray, out var distance)) return false;
            var local = transform.InverseTransformPoint(ray.GetPoint(distance));
            return local.x * local.x + local.z * local.z <= ringRadius * ringRadius;
        }
        public void Feedback(bool damaged, bool attacking, bool alive, bool ritual = false, bool observing = false, float delay = 0)
        {
            var newlyAlive = alive && !Alive;
            Alive = alive;
            if (newlyAlive && placement != null && placement.emergeDepth > 0)
            {
                emergence = 0; surfaceBroken = false;
                if (surfaceHeight != null) ConfigureSeaEmergence(surfaceHeight);
                else { emergenceOffset = -placement.emergeDepth; body.localPosition = bodyPosition + Vector3.up * emergenceOffset; }
            }
            if (GiantMotion != null)
            {
                if (!alive) GiantMotion.Retire();
                else { if (damaged) GiantMotion.PlayHit(); if (attacking) GiantMotion.PlayAttack(ritual, observing); }
                return;
            }
            if (deckMotion != null)
            {
                deckMotion.SetAlive(alive);
                if (damaged) deckMotion.PlayHit();
                if (attacking) deckMotion.PlayAttack(ritual, observing, delay);
                return;
            }
            if (skeletalPresentation)
            {
                if (actionSequence != null) StopCoroutine(actionSequence);
                if (!alive) actionSequence = StartCoroutine(PlayAction("BellMother_Death", false));
                else if (damaged) actionSequence = StartCoroutine(PlayAction("BellMother_Hit", false));
                else if (attacking) actionSequence = StartCoroutine(PlayAttack());
                return;
            }
            if (rigidPresentation)
            {
                if (!alive) rigidMotion.PlayDeath();
                else if (damaged) rigidMotion.PlayHit();
                else if (attacking) rigidMotion.PlayAttack();
                return;
            }
            impulse = damaged ? 1 : 0; attack = attacking ? 1 : 0;
        }

        public void Retire(bool withdrawal)
        {
            if (withdrawal) { withdrawalPending = true; return; }
            GiantMotion?.Retire();
            rigidMotion?.PlayDeath();
        }

        private System.Collections.IEnumerator PlayAttack()
        {
            yield return PlayAction("BellMother_Telegraph", false);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return PlayAction("BellMother_Attack_A", false);
            yield return PlayAction("BellMother_Recover", false);
            animator.Play("BellMother_Idle", 0, 0);
            actionSequence = null;
        }

        private System.Collections.IEnumerator PlayAction(string action, bool loop)
        {
            animator.CrossFadeInFixedTime(action, 0.08f, 0, 0);
            var clip = animator.runtimeAnimatorController.animationClips;
            var duration = 0.5f;
            for (var i = 0; i < clip.Length; i++)
                if (clip[i].name == action || clip[i].name.EndsWith(action, System.StringComparison.Ordinal)) { duration = clip[i].length; break; }
            if (!loop) yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, duration - 0.08f));
        }

        private void Update()
        {
            if (body == null || director == null) return;
            if (withdrawalPending && !director.Paused &&
                (GiantMotion == null || !GiantMotion.Busy) && (rigidMotion == null || rigidMotion.ActiveAction != "Attack"))
            {
                withdrawalPending = false;
                GiantMotion?.Retire(true); rigidMotion?.PlayDeath(true);
            }
            if (deckMotion != null) deckMotion.Tick(Time.unscaledDeltaTime, director.Paused, GameSettings.ReducedMotion);
            if (animator != null) animator.speed = director.Paused ? 0 : 1;
            if (rigidMotion != null) rigidMotion.SetPaused(director.Paused);
            if (GiantMotion != null) { GiantMotion.SetPaused(director.Paused); GiantMotion.Tick(Time.unscaledDeltaTime); }
            if (director.Paused) return;
            // Retirement owns the model root, including water drag and pivot compensation.
            // Do not add the old one-second linear sink on top or reset it every frame.
            if (GiantMotion != null && GiantMotion.IsRetiring || rigidMotion != null && rigidMotion.IsRetiring) return;
            var dt = Time.unscaledDeltaTime; age += dt;
            if (emergence < 1) emergence = Mathf.Min(1, emergence + dt / Mathf.Max(.1f, placement.emergeDuration));
            emergenceOffset = placement == null ? 0 : -placement.emergeDepth * (1 - Mathf.SmoothStep(0, 1, emergence));
            impulse = Mathf.MoveTowards(impulse, 0, dt * 2.2f); attack = Mathf.MoveTowards(attack, 0, dt * (IsSeaActor ? 0.8f : 1.4f));
            fall = Mathf.MoveTowards(fall, Alive ? 0 : 1, dt * 0.9f);
            var reduced = GameSettings.ReducedMotion;
            var emergeOffset = emergenceOffset;
            if (deckMotion != null || GiantMotion != null)
            {
                body.localPosition = bodyPosition + Vector3.up * emergeOffset;
                body.localRotation = bodyRotation;
                CheckSurfaceBreak();
                return;
            }
            if ((skeletalPresentation || rigidPresentation) && !legacyMotion)
            {
                body.localPosition = bodyPosition + Vector3.up * (-fall * height + emergeOffset);
                body.localRotation = bodyRotation;
                CheckSurfaceBreak();
                return;
            }
            if (IsSeaActor)
            {
                body.localPosition = bodyPosition + Vector3.up * (-fall * height + emergeOffset + (reduced ? 0 : Mathf.Sin(age * 0.34f) * placement.heave + Mathf.Sin(attack * Mathf.PI) * 0.45f));
                body.localRotation = bodyRotation * Quaternion.Euler(reduced ? 0 : impulse * 0.8f, 0, reduced ? 0 : Mathf.Sin(age * 0.23f) * 0.3f);
                CheckSurfaceBreak();
            }
            else
            {
                body.localPosition = bodyPosition + new Vector3(0, -fall * height * 0.55f, reduced ? 0 : impulse * 0.12f - Mathf.Sin(attack * Mathf.PI) * 0.3f);
                body.localRotation = bodyRotation * Quaternion.Euler(-fall * 65 - (reduced ? 0 : impulse * 8), 0, reduced ? 0 : Mathf.Sin(age * 0.8f + transform.position.x) * 0.45f);
            }
        }
    }
}
