using UnityEngine;

namespace WanjieScroll.Effects.Attacks
{
    /// <summary>
    /// Verlet rope with a fixed card-side root and a spring-guided tip.
    /// Distance constraints preserve rope length, while retained velocity,
    /// configurable slack and gentle bend relaxation keep motion flexible.
    /// Rendering samples a Catmull-Rom curve so physical nodes never appear as
    /// visible corners. This module uniquely owns all mutable rope state.
    /// </summary>
    internal sealed class FlexibleWhipSimulation
    {
        private const int NodeCount = 44;
        private const int ConstraintIterations = 7;
        private const float MaximumStep = 1f / 120f;

        private readonly Vector3[] _positions = new Vector3[NodeCount];
        private readonly Vector3[] _previous = new Vector3[NodeCount];
        private readonly Vector3[] _relaxed = new Vector3[NodeCount];
        private readonly float _flexibility;
        private bool _initialized;
        private float _simulatedLength;

        public FlexibleWhipSimulation(float flexibility)
        {
            _flexibility = Mathf.Clamp01(flexibility);
        }

        public void Step(
            Vector3 root,
            Vector3 guidedTip,
            float desiredLength,
            float arcHeight,
            float deltaTime)
        {
            float directLength = Vector3.Distance(root, guidedTip);
            desiredLength = Mathf.Max(
                desiredLength,
                directLength + 0.001f);
            if (!_initialized)
                Initialize(root, guidedTip, desiredLength);

            float frameDelta = Mathf.Clamp(deltaTime, 0f, 0.04f);
            if (frameDelta <= 0f)
            {
                _positions[0] = root;
                return;
            }

            float lengthResponse = Mathf.Lerp(15f, 8f, _flexibility);
            float lengthBlend = 1f - Mathf.Exp(-lengthResponse * frameDelta);
            _simulatedLength = Mathf.Max(
                directLength + 0.001f,
                Mathf.Lerp(_simulatedLength, desiredLength, lengthBlend));

            int substeps = Mathf.Clamp(
                Mathf.CeilToInt(frameDelta / MaximumStep),
                2,
                6);
            float stepDelta = frameDelta / substeps;
            float restLength = _simulatedLength / (NodeCount - 1);
            float gravity = arcHeight * Mathf.Lerp(13f, 25f, _flexibility);
            float tipFollow = Mathf.Lerp(0.82f, 0.52f, _flexibility);
            float bendRelaxation = Mathf.Lerp(0.035f, 0.105f, _flexibility);

            for (int substep = 0; substep < substeps; substep++)
            {
                Integrate(root, gravity, stepDelta);
                for (int iteration = 0;
                     iteration < ConstraintIterations;
                     iteration++)
                {
                    SatisfyDistances(root, restLength);
                    RelaxBends(root, bendRelaxation);
                }

                // The tip follows a spring instead of teleporting to its guide.
                // A second distance pass distributes that pull through the body.
                int tipIndex = NodeCount - 1;
                _positions[tipIndex] = Vector3.Lerp(
                    _positions[tipIndex],
                    guidedTip,
                    tipFollow);
                for (int iteration = 0; iteration < 2; iteration++)
                    SatisfyDistances(root, restLength);
            }
        }

        public Vector3 Sample(float normalized)
        {
            float node = Mathf.Clamp01(normalized) * (NodeCount - 1);
            int i1 = Mathf.Clamp(Mathf.FloorToInt(node), 0, NodeCount - 1);
            int i2 = Mathf.Min(i1 + 1, NodeCount - 1);
            int i0 = Mathf.Max(i1 - 1, 0);
            int i3 = Mathf.Min(i2 + 1, NodeCount - 1);
            float t = node - i1;
            return CatmullRom(
                _positions[i0],
                _positions[i1],
                _positions[i2],
                _positions[i3],
                t);
        }

        private void Initialize(
            Vector3 root,
            Vector3 tip,
            float ropeLength)
        {
            _simulatedLength = ropeLength;
            float directLength = Vector3.Distance(root, tip);
            float slackHeight = Mathf.Sqrt(Mathf.Max(
                0f,
                ropeLength * ropeLength - directLength * directLength)) *
                Mathf.Lerp(0.28f, 0.48f, _flexibility);
            for (int i = 0; i < NodeCount; i++)
            {
                float u = i / (float)(NodeCount - 1);
                Vector3 point = Vector3.Lerp(root, tip, u) +
                    Vector3.up * Mathf.Sin(u * Mathf.PI) * slackHeight;
                _positions[i] = point;
                _previous[i] = point;
            }
            _initialized = true;
        }

        private void Integrate(
            Vector3 root,
            float gravity,
            float deltaTime)
        {
            float damping = Mathf.Lerp(0.935f, 0.982f, _flexibility);
            float deltaSquared = deltaTime * deltaTime;
            _positions[0] = root;
            _previous[0] = root;
            for (int i = 1; i < NodeCount; i++)
            {
                float u = i / (float)(NodeCount - 1);
                Vector3 current = _positions[i];
                Vector3 velocity = (current - _previous[i]) * damping;
                Vector3 acceleration = Vector3.down * gravity *
                    Mathf.Lerp(0.35f, 1f, u);
                _positions[i] = current + velocity +
                    acceleration * deltaSquared;
                _previous[i] = current;
            }
        }

        private void SatisfyDistances(Vector3 root, float restLength)
        {
            _positions[0] = root;
            for (int i = 0; i < NodeCount - 1; i++)
                SatisfySegment(i, restLength);
            _positions[0] = root;
            for (int i = NodeCount - 2; i >= 0; i--)
                SatisfySegment(i, restLength);
            _positions[0] = root;

            float planeZ = root.z;
            for (int i = 1; i < NodeCount; i++)
                _positions[i].z = planeZ;
        }

        private void SatisfySegment(int index, float restLength)
        {
            Vector3 delta = _positions[index + 1] - _positions[index];
            float distance = delta.magnitude;
            if (distance <= 0.00001f)
                return;

            float leftWeight = index == 0
                ? 0f
                : Mathf.Lerp(
                    1f,
                    1.7f,
                    index / (float)(NodeCount - 1));
            float rightWeight = Mathf.Lerp(
                1f,
                1.7f,
                (index + 1f) / (NodeCount - 1));
            float totalWeight = leftWeight + rightWeight;
            Vector3 correction = delta *
                ((distance - restLength) / distance);
            if (leftWeight > 0f)
            {
                _positions[index] += correction *
                    (leftWeight / totalWeight);
            }
            _positions[index + 1] -= correction *
                (rightWeight / totalWeight);
        }

        private void RelaxBends(Vector3 root, float amount)
        {
            _relaxed[0] = root;
            _relaxed[NodeCount - 1] = _positions[NodeCount - 1];
            for (int i = 1; i < NodeCount - 1; i++)
            {
                Vector3 midpoint =
                    (_positions[i - 1] + _positions[i + 1]) * 0.5f;
                _relaxed[i] = Vector3.Lerp(
                    _positions[i],
                    midpoint,
                    amount);
            }
            for (int i = 1; i < NodeCount - 1; i++)
                _positions[i] = _relaxed[i];
            _positions[0] = root;
        }

        private static Vector3 CatmullRom(
            Vector3 p0,
            Vector3 p1,
            Vector3 p2,
            Vector3 p3,
            float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f *
                ((2f * p1) +
                 (-p0 + p2) * t +
                 (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                 (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
