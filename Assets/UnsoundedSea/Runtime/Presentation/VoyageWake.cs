using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnsoundedSea.Runtime
{
    /// <summary>World-space propeller wash follows the sailed curve and resamples the moving water.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class VoyageWake : MonoBehaviour
    {
        private const int MaxRows = 128, Columns = 9;
        private const float Lifetime = 11;
        private struct Row { public Vector3 point, right; public float born, strength; }
        private readonly List<Row> rows = new List<Row>(MaxRows);
        private readonly Vector3[] vertices = new Vector3[MaxRows * Columns];
        private readonly Vector2[] uv = new Vector2[MaxRows * Columns];
        private readonly Color[] colors = new Color[MaxRows * Columns];
        private readonly List<int> indices = new List<int>((MaxRows - 1) * (Columns - 1) * 6);
        private Mesh mesh;
        private float clock;
        private MaterialPropertyBlock properties;
        private MeshRenderer surface;
        public int SampleCount => rows.Count;

        private void OnEnable()
        {
            mesh = new Mesh { name = "Voyage wake (runtime)", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic(); GetComponent<MeshFilter>().sharedMesh = mesh;
            surface = GetComponent<MeshRenderer>(); surface.shadowCastingMode = ShadowCastingMode.Off;
            properties = new MaterialPropertyBlock();
        }
        public void Clear() { rows.Clear(); clock = 0; if (mesh != null) mesh.Clear(); }
        public void Tick(float dt, Vector3 position, Vector3 heading, float speed, BattleWater water, bool reduced)
        {
            if (mesh == null) return;
            if (reduced) { Clear(); return; }
            clock += dt;
            rows.RemoveAll(row => clock - row.born >= Lifetime);
            var stern = position - heading * 8;
            if (speed > 0.35f && (rows.Count == 0 || Vector3.Distance(stern, rows[rows.Count - 1].point) >= 0.45f))
            {
                if (rows.Count == MaxRows) rows.RemoveAt(0);
                rows.Add(new Row { point = stern, right = Vector3.Cross(Vector3.up, heading), born = clock, strength = Mathf.Clamp01(speed / 4) });
            }
            mesh.Clear();
            if (rows.Count < 2) return;
            indices.Clear();
            for (int row = 0; row < rows.Count; row++)
            {
                var sample = rows[row]; float age = clock - sample.born;
                float width = 2.4f + age * 0.65f;
                for (int column = 0; column < Columns; column++)
                {
                    int i = row * Columns + column;
                    float u = column / (float)(Columns - 1);
                    var rest = sample.point + sample.right * ((u * 2 - 1) * width);
                    vertices[i] = transform.InverseTransformPoint(rest + water.SampleDisplacement(rest) + Vector3.up * 0.15f);
                    uv[i] = new Vector2(u, sample.born * 1.7f);
                    float fade = Mathf.SmoothStep(1, 0, age / Lifetime) * sample.strength;
                    // No square end caps at emission or expiry.
                    colors[i] = new Color(1, 1, 1, fade * Mathf.Clamp01(row / 3f) * Mathf.Clamp01((rows.Count - 1 - row) / 2f));
                    if (row == 0 || column == 0) continue;
                    indices.Add(i - Columns - 1); indices.Add(i - 1); indices.Add(i);
                    indices.Add(i - Columns - 1); indices.Add(i); indices.Add(i - Columns);
                }
            }
            mesh.SetVertices(vertices, 0, rows.Count * Columns);
            mesh.SetUVs(0, uv, 0, rows.Count * Columns);
            mesh.SetColors(colors, 0, rows.Count * Columns);
            mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
            properties.SetFloat("_WakeTime", clock); surface.SetPropertyBlock(properties);
        }
        private void OnDisable()
        {
            rows.Clear();
            if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            mesh = null;
        }
    }
}
