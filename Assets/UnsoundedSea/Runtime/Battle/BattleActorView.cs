using UnityEngine;

namespace UnsoundedSea.Runtime
{
    // Static source meshes: whole-body staging, never a substitute for skeletal animation.
    public sealed class BattleActorView : MonoBehaviour
    {
        public string EnemyId { get; private set; }
        public Vector3 LabelPosition => transform.TransformPoint(new Vector3(0, height + 0.28f, 0));
        public bool Alive { get; private set; } = true;
        private Transform body;
        private LineRenderer ring;
        private BattleDirector director;
        private float height, impulse, attack, fall;
        public void Bind(BattleDirector owner, string id, GameObject prefab, Material ringMaterial)
        {
            director = owner; EnemyId = id;
            body = Instantiate(prefab, transform).transform;
            var bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var renderer in body.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            height = bounds.size.y;
            var hit = gameObject.AddComponent<CapsuleCollider>();
            hit.height = Mathf.Max(height, 1); hit.radius = 0.55f; hit.center = new Vector3(0, height * 0.5f, 0);
            ring = new GameObject("Target sigil").AddComponent<LineRenderer>(); ring.transform.SetParent(transform, false);
            ring.sharedMaterial = ringMaterial; ring.useWorldSpace = false; ring.loop = true;
            ring.widthMultiplier = 0.025f; ring.positionCount = 64;
            for (var i = 0; i < 64; i++) { var a = i * Mathf.PI / 32; ring.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.7f, 0.035f, Mathf.Sin(a) * 0.7f)); }
            Select(false);
        }
        public void Select(bool selected) { if (ring != null) ring.enabled = selected && Alive; }
        public void Feedback(bool damaged, bool attacking, bool alive) { impulse = damaged ? 1 : 0; attack = attacking ? 1 : 0; Alive = alive; }
        private void Update()
        {
            if (body == null || director.Paused) return;
            var dt = Time.unscaledDeltaTime;
            impulse = Mathf.MoveTowards(impulse, 0, dt * 2.2f); attack = Mathf.MoveTowards(attack, 0, dt * 1.4f);
            fall = Mathf.MoveTowards(fall, Alive ? 0 : 1, dt * 0.9f);
            var reduced = GameSettings.ReducedMotion;
            body.localPosition = new Vector3(0, -fall * height * 0.55f, reduced ? 0 : impulse * 0.12f - Mathf.Sin(attack * Mathf.PI) * 0.3f);
            body.localRotation = Quaternion.Euler(-fall * 65 - (reduced ? 0 : impulse * 8), 0, reduced ? 0 : Mathf.Sin(Time.unscaledTime * 0.8f + transform.position.x) * 0.45f);
        }
    }
}
