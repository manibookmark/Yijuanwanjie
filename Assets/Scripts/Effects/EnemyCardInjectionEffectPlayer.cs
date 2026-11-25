using System.Collections;
using UnityEngine;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns one short, dark-purple paper stream for an already committed enemy
    /// card grant. Bootstrap supplies captured world anchors and actual count;
    /// this owner knows no card zones, UI hierarchy, random costs or combat rules.
    /// Re-entry cancels the old generation. Disable/destroy hide the graphics
    /// synchronously and release this owner's mesh/material/worker. Re-enable
    /// does not replay a committed grant, and no business callback is delivered.
    /// </summary>
    public sealed class EnemyCardInjectionEffectPlayer : MonoBehaviour
    {
        private const float Duration = 1.26f;
        private const float GatherDuration = 0.14f;
        private const float FlightDuration = 0.66f;
        private const float LaunchSpan = 0.16f;
        private const int RibbonSegments = 18;
        private const int RibbonStrands = 3;
        private const int SourceQuads = 8;
        private const int ArrivalQuads = 16;
        private const int SortingOrder = 96;

        private static readonly Color PaperInk = new Color(0.29f, 0.12f, 0.41f, 0.94f);
        private static readonly Color FoldEdge = new Color(0.67f, 0.44f, 0.81f, 0.88f);
        private static readonly Color ArrivalLight = new Color(0.76f, 0.58f, 0.91f, 0.76f);

        private GameObject _root;
        private Mesh _mesh;
        private Material _material;
        private Vector3[] _vertices;
        private Color[] _colors;
        private Coroutine _routine;
        private int _generation;
        private int _paperCount;
        private bool _playing;
        private bool _startsAtRelease;
        private Vector3 _origin, _target, _control;

        public bool IsPlaying => _playing;

        public void Play(Vector3 worldOrigin, Vector3 worldTarget, int actualCount)
            => Begin(worldOrigin, worldTarget, actualCount, false);

        // The source clip has already gathered the spell; begin at release.
        public void PlayReleased(Vector3 worldOrigin, Vector3 worldTarget, int actualCount)
            => Begin(worldOrigin, worldTarget, actualCount, true);

        private void Begin(Vector3 worldOrigin, Vector3 worldTarget, int actualCount, bool released)
        {
            Cancel();
            _startsAtRelease = released;
            if (!isActiveAndEnabled || actualCount <= 0 ||
                !IsFinite(worldOrigin) || !IsFinite(worldTarget)) return;

            Material template = ProceduralParticleMaterial.GetAlphaBlendedBox();
            if (template == null) return;
            _origin = worldOrigin;
            _target = worldTarget;
            Vector3 delta = worldTarget - worldOrigin;
            Vector3 perpendicular = new Vector3(-delta.y, delta.x, 0f).normalized;
            if (perpendicular.y < 0f) perpendicular = -perpendicular;
            _control = (worldOrigin + worldTarget) * 0.5f + perpendicular *
                Mathf.Clamp(delta.magnitude * 0.11f, 0.35f, 1.05f);
            // Count changes visual density only; it never manufactures card
            // instances, consumes the gameplay RNG or retries a capped grant.
            _paperCount = 12 + Mathf.Clamp(actualCount, 1, 3) * 4;
            try
            {
                BuildGraphics(template);
                _playing = true;
                int generation = _generation;
                Coroutine started = StartCoroutine(Animate(generation));
                if (_playing && generation == _generation) _routine = started;
                else if (started != null) StopCoroutine(started);
            }
            catch
            {
                Cancel();
                throw;
            }
        }

        public void Cancel()
        {
            unchecked { _generation++; }
            _playing = false;
            Coroutine previous = _routine;
            _routine = null;
            if (previous != null) StopCoroutine(previous);
            ReleaseGraphics();
        }

        private void BuildGraphics(Material template)
        {
            _root = new GameObject("EnemyCardInjectionPaperStream");
            _root.transform.SetParent(transform, false);
            _root.transform.position = _origin;
            _material = new Material(template)
            {
                name = "EnemyCardInjectionPurplePaper",
                hideFlags = HideFlags.HideAndDontSave
            };
            _mesh = new Mesh
            {
                name = "EnemyCardInjectionPaperMesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            _mesh.MarkDynamic();
            int quads = _paperCount * 2 + RibbonSegments * RibbonStrands +
                        SourceQuads + ArrivalQuads;
            _vertices = new Vector3[quads * 4];
            _colors = new Color[quads * 4];
            var uv = new Vector2[quads * 4];
            var triangles = new int[quads * 6];
            for (int i = 0; i < quads; i++)
            {
                int v = i * 4, t = i * 6;
                uv[v] = new Vector2(0f, 0f);
                uv[v + 1] = new Vector2(1f, 0f);
                uv[v + 2] = new Vector2(1f, 1f);
                uv[v + 3] = new Vector2(0f, 1f);
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            _mesh.vertices = _vertices;
            _mesh.uv = uv;
            _mesh.colors = _colors;
            _mesh.triangles = triangles;
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = SortingOrder;
        }

        private IEnumerator Animate(int generation)
        {
            float elapsed = _startsAtRelease ? GatherDuration : 0f;
            while (generation == _generation && _playing && _root != null && elapsed < Duration)
            {
                Sample(elapsed);
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            if (generation != _generation) yield break;
            _routine = null;
            _playing = false;
            ReleaseGraphics();
        }

        private void Sample(float elapsed)
        {
            // Each small sheet has two folded halves, rather than a flat
            // rectangular particle or an unexplained solid beam across the HUD.
            for (int i = 0; i < _paperCount; i++)
            {
                float launch = GatherDuration + LaunchSpan * i /
                    Mathf.Max(1, _paperCount - 1);
                float t = (elapsed - launch) / FlightDuration;
                if (t <= 0f || t >= 1f)
                {
                    HideQuad(i * 2); HideQuad(i * 2 + 1);
                    continue;
                }
                Vector3 centre = FlightPoint(t);
                float spread = Mathf.Sin(t * Mathf.PI);
                centre += new Vector3(Mathf.Sin(i * 2.37f + t * 9f),
                    Mathf.Cos(i * 1.71f + t * 7f), 0f) * (0.14f * spread);
                float angle = i * 2.41f + t * (i % 2 == 0 ? 4f : -4f);
                // One leading folded sheet makes the travelling spell readable;
                // smaller fragments support it without resembling extra cards.
                float sheetScale = i == 0 ? 1.65f : 1f;
                Vector3 length = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) *
                    ((0.105f + (i % 3) * 0.017f) * sheetScale);
                Vector3 width = new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0f) *
                    ((0.042f + (i % 2) * 0.014f) * sheetScale);
                Vector3 crease = centre + width * (0.28f + 0.22f * Mathf.Sin(t * 12f + i));
                float alpha = Mathf.SmoothStep(0f, 1f, t / 0.12f) *
                    (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 1f, t)));
                SetQuad(i * 2, centre - length - width, centre + length - width,
                    crease + length, crease - length, WithAlpha(PaperInk, alpha));
                SetQuad(i * 2 + 1, crease - length, crease + length,
                    centre + length + width, centre - length + width, WithAlpha(FoldEdge, alpha));
            }

            float head = Mathf.Clamp01((elapsed - GatherDuration) / FlightDuration);
            float ribbonFade = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(GatherDuration + FlightDuration, Duration, elapsed));
            for (int strand = 0; strand < RibbonStrands; strand++)
            for (int i = 0; i < RibbonSegments; i++)
            {
                float a = Mathf.Max(0f, head - 0.34f + i * 0.34f / RibbonSegments);
                float b = Mathf.Max(0f, head - 0.34f + (i + 1) * 0.34f / RibbonSegments);
                Vector3 start = RibbonPoint(a, strand), end = RibbonPoint(b, strand);
                Vector3 normal = new Vector3(-(end - start).y, (end - start).x, 0f).normalized *
                    (strand == 0 ? 0.014f : 0.009f);
                SetQuad(_paperCount * 2 + strand * RibbonSegments + i,
                    start - normal, end - normal,
                    end + normal, start + normal,
                    WithAlpha(FoldEdge, ribbonFade * (i + 1f) / RibbonSegments * 0.36f));
            }

            int source = _paperCount * 2 + RibbonSegments * RibbonStrands;
            float gather = Mathf.Clamp01(elapsed / (GatherDuration + 0.16f));
            float gatherAlpha = _startsAtRelease ? 0f : Mathf.Sin(gather * Mathf.PI);
            for (int i = 0; i < SourceQuads; i++)
            {
                float angle = i * Mathf.PI * 2f / SourceQuads + gather * 1.4f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 side = new Vector3(-radial.y, radial.x, 0f);
                Vector3 centre = _origin + radial * Mathf.Lerp(0.48f, 0.14f, gather);
                SetQuad(source + i, centre - radial * 0.10f - side * 0.028f,
                    centre + radial * 0.10f - side * 0.028f,
                    centre + radial * 0.10f + side * 0.028f,
                    centre - radial * 0.10f + side * 0.028f,
                    WithAlpha(FoldEdge, gatherAlpha));
            }

            float arrivalStart = GatherDuration + FlightDuration;
            float pulseT = Mathf.InverseLerp(arrivalStart, Duration, elapsed);
            float pulse = Mathf.Sin(pulseT * Mathf.PI);
            int arrival = source + SourceQuads;
            for (int i = 0; i < ArrivalQuads; i++)
            {
                float angle = i * Mathf.PI * 2f / ArrivalQuads + pulseT * 0.45f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 tangent = new Vector3(-radial.y, radial.x, 0f);
                Vector3 centre = _target + radial * Mathf.Lerp(0.14f, 0.52f, pulseT);
                Vector3 along = tangent * Mathf.Lerp(0.07f, 0.11f, pulseT);
                Vector3 across = radial * (i % 2 == 0 ? 0.025f : 0.014f);
                SetQuad(arrival + i, centre - along - across, centre + along - across,
                    centre + along + across, centre - along + across,
                    WithAlpha(ArrivalLight, pulse * (i % 2 == 0 ? 0.9f : 0.6f)));
            }
            _mesh.vertices = _vertices;
            _mesh.colors = _colors;
            _mesh.RecalculateBounds();
        }

        private Vector3 FlightPoint(float t)
        {
            t = Mathf.Clamp01(t);
            t = t * t * (3f - 2f * t);
            return (1f - t) * (1f - t) * _origin +
                2f * (1f - t) * t * _control + t * t * _target;
        }

        private Vector3 RibbonPoint(float t, int strand)
        {
            Vector3 point = FlightPoint(t);
            if (strand == 0) return point;
            float envelope = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
            return point + new Vector3(Mathf.Sin(t * 11f + strand * 2f),
                Mathf.Cos(t * 9f + strand * 2f), 0f) * (0.085f * envelope);
        }

        private void SetQuad(int quad, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int start = quad * 4;
            // Convert captured world geometry here so owner hierarchy scaling
            // cannot distort the supplied source or target anchors.
            _vertices[start] = _root.transform.InverseTransformPoint(a);
            _vertices[start + 1] = _root.transform.InverseTransformPoint(b);
            _vertices[start + 2] = _root.transform.InverseTransformPoint(c);
            _vertices[start + 3] = _root.transform.InverseTransformPoint(d);
            for (int i = 0; i < 4; i++) _colors[start + i] = color;
        }

        private void HideQuad(int quad)
            => SetQuad(quad, _origin, _origin, _origin, _origin, Color.clear);

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            return color;
        }

        private static bool IsFinite(Vector3 point)
            => !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
               !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
               !float.IsNaN(point.z) && !float.IsInfinity(point.z);

        private void ReleaseGraphics()
        {
            GameObject root = _root;
            Mesh mesh = _mesh;
            Material material = _material;
            _root = null;
            _mesh = null;
            _material = null;
            _vertices = null;
            _colors = null;
            _paperCount = 0;
            if (root != null) root.SetActive(false);
            DestroyOwned(root);
            DestroyOwned(mesh);
            DestroyOwned(material);
        }

        private static void DestroyOwned(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource);
            else DestroyImmediate(resource);
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();
    }
}
