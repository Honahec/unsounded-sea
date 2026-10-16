using UnityEngine;

namespace UnsoundedSea.Runtime
{
    // Static source meshes: whole-body staging, never a substitute for skeletal animation.
    public sealed class BattleActorView : MonoBehaviour
    {
        public string EnemyId { get; private set; }
        public Vector3 LabelPosition => transform.TransformPoint(labelPosition);
        public bool Alive { get; private set; } = true;
        public bool IsSeaActor => placement != null && placement.environment == BattleEnvironment.Sea;
        public float WaterRadius => IsSeaActor ? placement.waterRadius : 0;
        private Transform body;
        private LineRenderer ring;
        private BattleDirector director;
        private BattleAnchor placement;
        private Vector3 bodyPosition, labelPosition;
        private Quaternion bodyRotation;
        private float height, impulse, attack, fall, age;

        public void Bind(BattleDirector owner, string id, GameObject prefab, Material ringMaterial)
        {
            director = owner; EnemyId = id;
            placement = transform.parent.GetComponent<BattleAnchor>();
            body = Instantiate(prefab, transform).transform;
            bodyPosition = body.localPosition; bodyRotation = body.localRotation;
            var bounds = MeasureLocalBounds(transform);
            height = bounds.size.y;
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
            var radius = Mathf.Max(0.7f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            for (var i = 0; i < 64; i++) { var a = i * Mathf.PI / 32; ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.035f, Mathf.Sin(a) * radius)); }
            Select(false);
        }

        public static Bounds MeasureLocalBounds(Transform root)
        {
            var bounds = new Bounds(); bool first = true;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var local = renderer.localBounds;
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
        public void Feedback(bool damaged, bool attacking, bool alive) { impulse = damaged ? 1 : 0; attack = attacking ? 1 : 0; Alive = alive; }
        private void Update()
        {
            if (body == null || director.Paused) return;
            var dt = Time.unscaledDeltaTime; age += dt;
            impulse = Mathf.MoveTowards(impulse, 0, dt * 2.2f); attack = Mathf.MoveTowards(attack, 0, dt * (IsSeaActor ? 0.8f : 1.4f));
            fall = Mathf.MoveTowards(fall, Alive ? 0 : 1, dt * 0.9f);
            var reduced = GameSettings.ReducedMotion;
            if (IsSeaActor)
            {
                body.localPosition = bodyPosition + Vector3.up * (-fall * height + (reduced ? 0 : Mathf.Sin(age * 0.34f) * placement.heave + Mathf.Sin(attack * Mathf.PI) * 0.45f));
                body.localRotation = bodyRotation * Quaternion.Euler(reduced ? 0 : impulse * 0.8f, 0, reduced ? 0 : Mathf.Sin(age * 0.23f) * 0.3f);
            }
            else
            {
                body.localPosition = bodyPosition + new Vector3(0, -fall * height * 0.55f, reduced ? 0 : impulse * 0.12f - Mathf.Sin(attack * Mathf.PI) * 0.3f);
                body.localRotation = bodyRotation * Quaternion.Euler(-fall * 65 - (reduced ? 0 : impulse * 8), 0, reduced ? 0 : Mathf.Sin(age * 0.8f + transform.position.x) * 0.45f);
            }
        }
    }
}
