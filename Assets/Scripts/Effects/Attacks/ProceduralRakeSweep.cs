using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects.Attacks
{
    /// <summary>
    /// Owns the rigid nine-tooth rake silhouette, grouped sweep clock and all
    /// temporary particles. Recorded hits are presentation receipts: this
    /// module neither selects targets nor applies damage. The existing sweep
    /// records are shared because they describe attack instances, not ropes.
    ///
    /// One runtime owner also watches cancellation independently of the caller's
    /// coroutine, so stopping or destroying that caller cannot strand a rake.
    /// Scaled time freezes geometry and receipts together during a checkpoint.
    /// Re-review this boundary before adding a second weapon or lifecycle here.
    /// </summary>
    public sealed class ProceduralRakeSweep : MonoBehaviour
    {
        public const string VisualTypeName = "ProceduralRakeSweep";
        public const string RootName = "ProceduralRakeSweepRoot";

        private const float LeftContactMoment = .25f;
        private const float RightContactMoment = .72f;
        private const float MaximumLaunchWindow = .9f;

        private IReadOnlyList<AuthoritativeWhipSweep> _sweeps;
        private readonly List<Sweep> _active = new List<Sweep>();
        private Vector3 _origin;
        private Action<AuthoritativeWhipHit> _onEachHit;
        private Func<bool> _isCancelled;
        private float _duration;
        private float _launchInterval;
        private float _elapsed;
        private VfxProfile _profile;
        private float _thickness;
        private int _nextSweep;
        private bool _released;

        /// <summary>
        /// Presents each recorded attack instance once. The optional cancellation
        /// query identifies the caller's source lease; no callback is sent after
        /// that lease ends. Disposing playback also destroys its runtime owner.
        /// </summary>
        public static IEnumerator Play(
            Transform visualParent,
            Vector3 attackOrigin,
            IReadOnlyList<AuthoritativeWhipSweep> sweeps,
            VfxProfile profile,
            Action<AuthoritativeWhipHit> onEachHit,
            Func<bool> isCancelled = null)
        {
            if (sweeps == null || sweeps.Count == 0 ||
                (isCancelled != null && isCancelled()))
                yield break;

            var host = new GameObject(RootName);
            if (visualParent != null) host.transform.SetParent(visualParent, false);
            var owner = host.AddComponent<ProceduralRakeSweep>();
            owner._sweeps = sweeps;
            owner._origin = attackOrigin;
            owner._onEachHit = onEachHit;
            owner._isCancelled = isCancelled;
            owner._profile = profile;
            owner._duration = profile != null
                ? Mathf.Clamp(profile.whipSweepDuration, .45f, .65f) : .58f;
            owner._launchInterval = profile != null
                ? Mathf.Clamp(profile.whipInterSweepDelay, 0f, .3f) : .1f;
            if (sweeps.Count > 1)
                owner._launchInterval = Mathf.Min(owner._launchInterval,
                    MaximumLaunchWindow / (sweeps.Count - 1));
            owner._thickness = profile != null
                ? Mathf.Clamp(profile.whipSegmentThickness, .08f, .16f) : .11f;

            try
            {
                while (owner != null && !owner._released)
                {
                    if (owner.Cancelled || !owner.isActiveAndEnabled) break;
                    if (Time.timeScale > 0f && Time.deltaTime > 0f)
                        owner.Advance(Time.deltaTime);
                    if (owner._nextSweep >= sweeps.Count && owner._active.Count == 0)
                        break;
                    yield return null;
                }
            }
            finally
            {
                if (owner != null)
                {
                    owner.Release();
                    Destroy(owner.gameObject);
                }
            }
        }

        private bool Cancelled => _isCancelled != null && _isCancelled();

        private void Update()
        {
            // This does not advance a second clock. It only releases resources
            // if the coroutine host disappears before its iterator is disposed.
            if (_released || _sweeps == null || !Cancelled) return;
            Release();
            Destroy(gameObject);
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        private void Advance(float delta)
        {
            _elapsed += delta;
            while (_nextSweep < _sweeps.Count &&
                   _elapsed + Mathf.Epsilon >= _nextSweep * _launchInterval)
            {
                int index = _nextSweep++;
                AuthoritativeWhipSweep record = _sweeps[index];
                if (record == null || record.Hits.Count == 0) continue;
                _active.Add(new Sweep(transform, _origin, record,
                    index * _launchInterval, _duration, _thickness, _profile));
            }

            for (int i = 0; i < _active.Count;)
            {
                if (Cancelled || Time.timeScale <= 0f) return;
                Sweep sweep = _active[i];
                float motion = Mathf.Clamp01((_elapsed - sweep.StartTime) / _duration);
                sweep.Render(motion);
                while (sweep.NextHit < sweep.Record.Hits.Count &&
                       motion >= sweep.HitMoment(sweep.NextHit))
                {
                    AuthoritativeWhipHit hit = sweep.Record.Hits[sweep.NextHit++];
                    _onEachHit?.Invoke(hit);
                    // A hit can synchronously open the level-up checkpoint or
                    // cancel its source. Do not drain further receipts then.
                    if (Cancelled || Time.timeScale <= 0f) return;
                }
                if (motion < 1f) { i++; continue; }
                sweep.Release();
                _active.RemoveAt(i);
            }
        }

        private void Release()
        {
            if (_released) return;
            _released = true;
            foreach (Sweep sweep in _active) sweep.Release();
            _active.Clear();
            _onEachHit = null;
        }

        private sealed class Sweep
        {
            internal readonly AuthoritativeWhipSweep Record;
            internal readonly float StartTime;
            internal int NextHit;

            private readonly GameObject _root;
            private readonly ParticleSystem _body;
            private readonly ParticleSystem _shadow;
            private readonly ParticleSystem _wake;
            private readonly ProceduralRakeSoulAccentRig _soulAccent;
            private readonly ParticleSystem.Particle[] _bodyParticles;
            private readonly ParticleSystem.Particle[] _shadowParticles;
            private readonly ParticleSystem.Particle[] _wakeParticles =
                new ParticleSystem.Particle[10];
            private readonly Color[] _colors;
            private readonly Vector3 _origin;
            private readonly Vector3 _left;
            private readonly Vector3 _right;
            private readonly float _shaftLength;
            private readonly float _lifetime;
            private readonly float _thickness;
            private bool _released;

            internal Sweep(Transform parent, Vector3 origin,
                AuthoritativeWhipSweep record, float startTime,
                float duration, float thickness, VfxProfile profile)
            {
                Record = record;
                StartTime = startTime;
                _origin = origin;
                _thickness = thickness;
                _lifetime = duration + .2f;

                // Only captured positions define the travel envelope. No live
                // formation, enemy index or camera-selected target is queried.
                float left = record.Hits[0].Position.x;
                float right = left;
                Vector3 center = Vector3.zero;
                foreach (AuthoritativeWhipHit hit in record.Hits)
                {
                    left = Mathf.Min(left, hit.Position.x);
                    right = Mathf.Max(right, hit.Position.x);
                    center += hit.Position;
                }
                center /= record.Hits.Count;
                float planeZ = center.z - .25f;
                _origin.z = planeZ;
                _left = new Vector3(left - .38f, center.y, planeZ);
                _right = new Vector3(right + .38f, center.y, planeZ);
                _shaftLength = Mathf.Clamp(Vector3.Distance(_origin, center), 2.3f, 3.6f);

                _root = new GameObject($"NineToothRake-{record.AttackInstanceIndex}");
                _root.transform.SetParent(parent, false);
                _bodyParticles = BuildBody();
                _colors = new Color[_bodyParticles.Length];
                _shadowParticles = new ParticleSystem.Particle[_bodyParticles.Length];
                for (int i = 0; i < _bodyParticles.Length; i++)
                {
                    _colors[i] = _bodyParticles[i].startColor;
                    _shadowParticles[i] = _bodyParticles[i];
                    _shadowParticles[i].position += new Vector3(.035f, -.025f, .03f);
                    _shadowParticles[i].startSize3D *= 1.12f;
                }
                _shadow = CreateSystem("RakeShadow", _bodyParticles.Length,
                    ProceduralParticleMaterial.GetAlphaBlendedBox(), false, 80);
                _body = CreateSystem("RigidNineToothRake", _bodyParticles.Length,
                    ProceduralParticleMaterial.GetAlphaBlendedBox(), false, 81);
                _wake = CreateSystem("ShortRakeAirArc", _wakeParticles.Length,
                    ProceduralParticleMaterial.GetAlphaBlended(), true, 79);
                _soulAccent = ProceduralRakeSoulAccentRig.Create(
                    _root.transform, profile, duration);
            }

            internal float HitMoment(int index)
            {
                float x = Mathf.InverseLerp(_left.x, _right.x,
                    Record.Hits[index].Position.x);
                // The head uses SmoothStep along the row. Invert it so feedback
                // occurs when the same head crosses each captured anchor.
                float low = 0f, high = 1f;
                for (int i = 0; i < 12; i++)
                {
                    float middle = (low + high) * .5f;
                    if (Mathf.SmoothStep(0f, 1f, middle) < x) low = middle;
                    else high = middle;
                }
                return Mathf.Lerp(LeftContactMoment, RightContactMoment,
                    (low + high) * .5f);
            }

            internal void Render(float motion)
            {
                Vector3 head = HeadPosition(motion);
                Vector3 axis = head - _origin;
                axis.z = 0f;
                axis = axis.sqrMagnitude > .001f ? axis.normalized : Vector3.up;
                _root.transform.position = head - axis * _shaftLength;
                _root.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis);
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .1f, motion)) *
                    (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.86f, 1f, motion)));
                for (int i = 0; i < _bodyParticles.Length; i++)
                {
                    Color color = _colors[i];
                    color.a *= fade;
                    _bodyParticles[i].startColor = color;
                    _shadowParticles[i].startColor = new Color(.025f, .03f, .035f, fade * .32f);
                }
                _body.SetParticles(_bodyParticles, _bodyParticles.Length);
                _shadow.SetParticles(_shadowParticles, _shadowParticles.Length);
                int wakeCount = motion >= LeftContactMoment && motion <= .82f
                    ? _wakeParticles.Length : 0;
                for (int i = 0; i < wakeCount; i++)
                {
                    float lag = (i + 1f) / _wakeParticles.Length;
                    _wakeParticles[i] = Particle(HeadPosition(Mathf.Max(0f, motion - lag * .07f)),
                        new Vector3(.075f, .075f, 1f),
                        new Color(.82f, .77f, .59f, (1f - lag) * .16f * fade));
                }
                _wake.SetParticles(_wakeParticles, wakeCount);
                _soulAccent?.Render(head, motion, fade);
            }

            private Vector3 HeadPosition(float motion)
            {
                if (motion <= LeftContactMoment)
                {
                    float cast = Mathf.SmoothStep(0f, 1f, motion / LeftContactMoment);
                    Vector3 start = _origin + new Vector3(-.6f, 1f, 0f);
                    return Vector3.Lerp(start, _left, cast) +
                        Vector3.up * (.22f * Mathf.Sin(cast * Mathf.PI));
                }
                if (motion <= RightContactMoment)
                {
                    float across = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(LeftContactMoment, RightContactMoment, motion));
                    return Vector3.Lerp(_left, _right, across);
                }
                float retract = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(RightContactMoment, 1f, motion));
                return Vector3.Lerp(_right, _origin + new Vector3(.8f, .8f, 0f), retract) +
                    Vector3.up * (.18f * Mathf.Sin(retract * Mathf.PI));
            }

            private ParticleSystem.Particle[] BuildBody()
            {
                var pieces = new List<ParticleSystem.Particle>();
                Color wood = new Color(.27f, .145f, .065f, 1f);
                Color iron = new Color(.25f, .30f, .34f, 1f);
                Color edge = new Color(.57f, .64f, .67f, 1f);
                Color bronze = new Color(.69f, .46f, .20f, 1f);
                AddBar(pieces, 0f, _shaftLength * .5f, _thickness, _shaftLength, wood);
                AddBar(pieces, -.022f, _shaftLength * .5f, _thickness * .22f,
                    _shaftLength * .95f, new Color(.47f, .29f, .14f, 1f));
                for (int i = 0; i < 3; i++)
                    AddBar(pieces, 0f, _shaftLength - .17f - i * .15f,
                        _thickness * 1.55f, .065f, bronze);
                AddBar(pieces, 0f, _shaftLength, 1.36f, _thickness * 1.42f, iron);
                AddBar(pieces, 0f, _shaftLength + .034f, 1.29f, .034f, edge);
                for (int tooth = 0; tooth < 9; tooth++)
                {
                    float x = -.60f + tooth * .15f;
                    AddBar(pieces, x, _shaftLength - .20f, .074f, .38f, iron);
                    AddBar(pieces, x - .017f, _shaftLength - .20f, .023f, .34f, edge);
                    AddBar(pieces, x + .038f, _shaftLength - .383f, .13f, .057f, iron);
                }
                return pieces.ToArray();
            }

            private void AddBar(List<ParticleSystem.Particle> pieces,
                float x, float y, float width, float height, Color color)
            {
                pieces.Add(Particle(new Vector3(x, y, -.01f),
                    new Vector3(width, height, 1f), color));
            }

            private ParticleSystem.Particle Particle(Vector3 position, Vector3 size, Color color)
            {
                return new ParticleSystem.Particle
                {
                    position = position, velocity = Vector3.zero,
                    startLifetime = _lifetime, remainingLifetime = _lifetime,
                    startSize3D = size, startColor = color
                };
            }

            private ParticleSystem CreateSystem(string name, int count,
                Material material, bool world, int order)
            {
                var child = new GameObject(name);
                child.transform.SetParent(_root.transform, false);
                var system = child.AddComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.loop = false;
                main.playOnAwake = false;
                main.simulationSpace = world ? ParticleSystemSimulationSpace.World
                    : ParticleSystemSimulationSpace.Local;
                main.startLifetime = _lifetime;
                main.startSpeed = 0f;
                main.startSize3D = true;
                main.maxParticles = count;
                var emission = system.emission;
                emission.enabled = false;
                var shape = system.shape;
                shape.enabled = false;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = world ? ParticleSystemRenderSpace.View
                    : ParticleSystemRenderSpace.Local;
                renderer.sharedMaterial = material;
                renderer.sortingOrder = order;
                system.Play();
                return system;
            }

            internal void Release()
            {
                if (_released) return;
                _released = true;
                if (_root == null) return;
                foreach (ParticleSystem system in new[] { _body, _shadow, _wake })
                    if (system != null)
                        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _soulAccent?.Clear();
                Destroy(_root);
            }
        }
    }
}

