using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Owns the flame surfaces, branching electric paths and flying wake of one
    /// ring. The casting owner supplies time and visibility; this module never
    /// schedules contacts or knows receipt counts. All geometry buffers are reused.
    /// </summary>
    internal sealed class FireRingEnergyGeometry : MonoBehaviour
    {
        // Includes the longest tongue and local electric fork, before root scale.
        internal const float OuterRadius = 0.98f;
        private sealed class Arc
        {
            internal LineRenderer Glow, Core, Branch;
            internal readonly Vector3[] Path = new Vector3[9];
            internal readonly Vector3[] Fork = new Vector3[5];
        }

        private Mesh _flames;
        private Vector3[] _vertices;
        private Color[] _colors;
        private Arc[] _arcs;
        private LineRenderer _wake, _wakeCore;
        private readonly Vector3[] _wakePoints = new Vector3[4];
        private int _tongues;
        private bool _evolved, _burst;
        private int _arcTick = -1;
        private float _opacity = 1f;

        internal void Initialize(bool evolved, bool burst, bool flying)
        {
            _evolved = evolved;
            _burst = burst;
            _tongues = evolved ? 28 : 18;
            _vertices = new Vector3[_tongues * 2 * 7];
            _colors = new Color[_vertices.Length];
            var uv = new Vector2[_vertices.Length];
            var triangles = new int[_tongues * 2 * 18];
            int[] topology = { 0, 1, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 3, 4, 6, 3, 6, 5 };
            for (int f = 0; f < _tongues * 2; f++)
            {
                int v = f * 7;
                uv[v] = new Vector2(.5f, .15f);
                uv[v + 1] = new Vector2(.08f, .15f);
                uv[v + 2] = new Vector2(.92f, .15f);
                uv[v + 3] = new Vector2(.5f, .5f);
                uv[v + 4] = new Vector2(.18f, .5f);
                uv[v + 5] = new Vector2(.82f, .5f);
                uv[v + 6] = new Vector2(.5f, .95f);
                for (int t = 0; t < topology.Length; t++) triangles[f * 18 + t] = v + topology[t];
            }
            _flames = new Mesh { name = "RuntimeThunderFireTongues" };
            _flames.MarkDynamic();
            _flames.vertices = _vertices;
            _flames.colors = _colors;
            _flames.uv = uv;
            _flames.triangles = triangles;
            var flameObject = new GameObject("LayeredFlameSurfaces", typeof(MeshFilter), typeof(MeshRenderer));
            flameObject.transform.SetParent(transform, false);
            flameObject.GetComponent<MeshFilter>().sharedMesh = _flames;
            var renderer = flameObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlendedBox();
            renderer.sortingOrder = 48;
            _arcs = new Arc[burst ? (evolved ? 12 : 8) : (evolved ? 10 : 6)];
            for (int i = 0; i < _arcs.Length; i++)
            {
                _arcs[i] = new Arc
                {
                    Glow = CreateLine("ElectricBlueHalo", evolved ? .070f : .052f, 9),
                    Core = CreateLine("ElectricWhiteCore", evolved ? .022f : .016f, 9),
                    Branch = CreateLine("ElectricFork", evolved ? .024f : .018f, 5)
                };
                _arcs[i].Glow.sortingOrder = 50;
                _arcs[i].Core.sortingOrder = _arcs[i].Branch.sortingOrder = 52;
            }
            if (flying)
            {
                _wake = CreateLine("BroadOrangeFlameWake", evolved ? .20f : .15f, 4);
                _wakeCore = CreateLine("GoldenFlameWakeCore", evolved ? .070f : .05f, 4);
                _wake.useWorldSpace = _wakeCore.useWorldSpace = true;
                _wake.endWidth = _wakeCore.endWidth = .002f;
                _wake.sortingOrder = _wakeCore.sortingOrder = 47;
            }
            Advance(0f, 1f);
        }

        internal void Advance(float time, float opacity)
        {
            _opacity = opacity;
            for (int i = 0; i < _tongues; i++)
            {
                float a = i * Mathf.PI * 2f / _tongues;
                Vector3 radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 tangent = new Vector3(-radial.y, radial.x, 0f);
                float flicker = .72f + .28f * Mathf.Sin(time * 21f + i * 2.31f);
                float length = (_burst ? .59f : .36f) * flicker;
                float baseRadius = _burst ? .11f : .435f;
                float width = _burst ? .095f : .073f;
                // Broad translucent orange silhouette and narrower luminous core.
                WriteTongue(i * 14, radial, tangent, baseRadius, length, width,
                    .10f + .025f * Mathf.Sin(time * 16f + i), false);
                WriteTongue(i * 14 + 7, radial, tangent, baseRadius, length * .69f,
                    width * .48f, .075f, true);
            }
            _flames.vertices = _vertices;
            _flames.colors = _colors;
            _flames.RecalculateBounds();
            int tick = Mathf.FloorToInt(time * 24f);
            if (tick != _arcTick)
            {
                _arcTick = tick;
                for (int i = 0; i < _arcs.Length; i++) ShapeArc(_arcs[i], i, tick);
            }
            for (int i = 0; i < _arcs.Length; i++)
            {
                float pulse = .52f + .48f * Mathf.Abs(Mathf.Sin(time * 23f + i * 1.71f));
                Tint(_arcs[i].Glow, new Color(.10f, .55f, 1f, .64f * pulse * opacity));
                Tint(_arcs[i].Core, new Color(.82f, .98f, 1f, pulse * opacity));
                Tint(_arcs[i].Branch, new Color(.42f, .82f, 1f, .90f * pulse * opacity));
            }
            if (_wake != null)
            {
                _wake.startColor = new Color(1f, .24f, .02f, .88f * opacity);
                _wake.endColor = new Color(1f, .07f, .01f, 0f);
                _wakeCore.startColor = new Color(1f, .89f, .35f, opacity);
                _wakeCore.endColor = new Color(1f, .35f, .02f, 0f);
            }
        }

        internal void SetFlightWake(Vector3 point, Vector3 departure)
        {
            if (_wake == null) return;
            Vector3 behind = (departure - point).normalized * (_evolved ? .72f : .46f);
            Vector3 side = new Vector3(-behind.y, behind.x, 0f).normalized;
            _wakePoints[0] = point;
            _wakePoints[1] = point + behind * .28f + side * .028f;
            _wakePoints[2] = point + behind * .64f - side * .045f;
            _wakePoints[3] = point + behind;
            _wake.SetPositions(_wakePoints);
            _wakeCore.SetPositions(_wakePoints);
        }

        private void WriteTongue(int v, Vector3 radial, Vector3 tangent,
            float radius, float length, float width, float curl, bool core)
        {
            Vector3 start = radial * radius;
            Vector3 middle = radial * (radius + length * .48f) - tangent * curl * .35f;
            _vertices[v] = start;
            _vertices[v + 1] = start - tangent * width;
            _vertices[v + 2] = start + tangent * width;
            _vertices[v + 3] = middle;
            _vertices[v + 4] = middle - tangent * width * .67f;
            _vertices[v + 5] = middle + tangent * width * .67f;
            _vertices[v + 6] = radial * (radius + length) - tangent * curl;
            _colors[v] = core ? new Color(1f, .95f, .57f, .95f * _opacity) : new Color(1f, .58f, .06f, .88f * _opacity);
            _colors[v + 1] = _colors[v + 2] = core ? new Color(1f, .68f, .10f, .70f * _opacity) : new Color(1f, .13f, .01f, .12f * _opacity);
            _colors[v + 3] = core ? new Color(1f, .76f, .16f, .88f * _opacity) : new Color(1f, .29f, .01f, .75f * _opacity);
            _colors[v + 4] = _colors[v + 5] = new Color(1f, core ? .45f : .12f, .01f, .10f * _opacity);
            _colors[v + 6] = new Color(1f, .06f, .01f, 0f);
        }

        private void ShapeArc(Arc arc, int index, int tick)
        {
            float a = index * Mathf.PI * 2f / _arcs.Length;
            // Rings crawl along the rim before discharging outward; bursts radiate
            // only around the actual contact, never toward another target or sky.
            for (int j = 0; j < arc.Path.Length; j++)
            {
                float t = j / 8f;
                float angle = a + (_burst ? .11f : .64f) * t;
                Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 tangent = new Vector3(-radial.y, radial.x, 0f);
                float radius = _burst ? Mathf.Lerp(.06f, .83f, t) : .465f + Mathf.Max(0f, t - .55f) * .61f;
                float jitter = j == 0 || j == 8 ? 0f : Mathf.Sin(tick * 2.17f + index * 3.13f + j * 5.27f) * .068f;
                arc.Path[j] = radial * radius + tangent * jitter;
            }
            Vector3 forkStart = arc.Path[5];
            Vector3 forkEnd = forkStart + new Vector3(Mathf.Cos(a + .85f), Mathf.Sin(a + .85f), 0f) * .23f;
            for (int j = 0; j < arc.Fork.Length; j++)
            {
                float t = j / 4f;
                arc.Fork[j] = Vector3.Lerp(forkStart, forkEnd, t) +
                    new Vector3(Mathf.Sin(j * 4.2f + tick), Mathf.Cos(j * 5.3f + tick), 0f) * Mathf.Sin(t * Mathf.PI) * .03f;
            }
            arc.Glow.SetPositions(arc.Path);
            arc.Core.SetPositions(arc.Path);
            arc.Branch.SetPositions(arc.Fork);
        }

        private LineRenderer CreateLine(string name, float width, int count)
        {
            var obj = new GameObject(name, typeof(LineRenderer));
            obj.transform.SetParent(transform, false);
            var line = obj.GetComponent<LineRenderer>();
            line.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlendedBox();
            line.useWorldSpace = false;
            line.positionCount = count;
            line.startWidth = line.endWidth = width;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.sortingOrder = 51;
            return line;
        }

        private static void Tint(LineRenderer line, Color color) => line.startColor = line.endColor = color;
        private void OnDestroy() { if (_flames != null) Destroy(_flames); }
    }
}
