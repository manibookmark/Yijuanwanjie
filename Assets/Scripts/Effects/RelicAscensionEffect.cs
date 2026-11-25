using System.Collections;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Foreground relic ceremony aligned with the map-node collection
    /// language: warm gold light converges onto the treasure ("灵物入匣")
    /// while spirit motes drift upward, with a slow orbit as accent.
    /// All three systems share one direction family (inward + upward).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RelicAscensionEffect : MonoBehaviour
    {
        private ParticleSystem _halo;
        private ParticleSystem _blessingRays;
        private ParticleSystem _orbitingMotes;
        private Coroutine _sequence;
        private Color _gold;

        private static Material _material;
        private static Texture2D _softSpark;

        public int SystemCount { get; private set; }

        public void Initialize(int sortingOrder)
        {
            _gold = new Color(1f, 0.78f, 0.28f, 0.94f);
            _halo = CreateSystem(
                "RelicDivineHalo", sortingOrder, 0.62f, 0.15f);
            _blessingRays = CreateSystem(
                "RelicBlessingRays", sortingOrder + 1, 0.86f, 0.10f);
            _orbitingMotes = CreateSystem(
                "RelicFortuneOrbit", sortingOrder + 2, 1.10f, 0.08f);
            SystemCount = 3;
            _sequence = StartCoroutine(PlayAscension());
        }

        public void PlayHoverPulse()
        {
            EmitConverge(8, 0.62f, 0.9f);
            EmitOrbit(8, 0.48f);
        }

        private IEnumerator PlayAscension()
        {
            EmitConverge(16, 0.72f, 1.08f);
            EmitRise(14);
            yield return new WaitForSecondsRealtime(0.22f);
            EmitConverge(10, 0.52f, 0.78f);
            EmitOrbit(12, 0.5f);
            yield return new WaitForSecondsRealtime(0.22f);
            EmitRise(10);
            _sequence = null;
        }

        private ParticleSystem CreateSystem(
            string objectName,
            int sortingOrder,
            float lifetime,
            float size)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            system.Stop(true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1.2f;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = _gold;
            main.maxParticles = 180;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.18f),
                    new Keyframe(0.22f, 1f),
                    new Keyframe(0.75f, 0.70f),
                    new Keyframe(1f, 0f)));
            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(
                CreateFadeGradient(_gold));
            ParticleSystemRenderer renderer =
                child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GetMaterial();
            renderer.sortingOrder = sortingOrder;
            renderer.alignment = ParticleSystemRenderSpace.View;
            return system;
        }

        private void EmitConverge(int count, float innerRadius, float outerRadius)
        {
            if (_halo == null) return;
            _halo.Play();
            Vector3 center = _halo.transform.position;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, count) +
                              Random.Range(-0.12f, 0.12f);
                Vector3 radial = new Vector3(
                    Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                float radius = Random.Range(innerRadius, outerRadius);
                _halo.Emit(new ParticleSystem.EmitParams
                {
                    position = center + radial * radius,
                    velocity = -radial * Random.Range(0.55f, 0.85f),
                    startLifetime = Random.Range(0.52f, 0.72f),
                    startSize = Random.Range(0.08f, 0.14f),
                    startColor = Color.Lerp(_gold, Color.white,
                        Random.Range(0.18f, 0.40f))
                }, 1);
            }
        }

        private void EmitRise(int count)
        {
            if (_blessingRays == null) return;
            _blessingRays.Play();
            Vector3 center = _blessingRays.transform.position;
            for (int i = 0; i < count; i++)
            {
                _blessingRays.Emit(new ParticleSystem.EmitParams
                {
                    position = center + new Vector3(
                        Random.Range(-0.16f, 0.16f),
                        Random.Range(-0.06f, 0.10f),
                        0f),
                    velocity = new Vector3(
                        Random.Range(-0.08f, 0.08f),
                        Random.Range(0.45f, 0.72f),
                        0f),
                    startLifetime = Random.Range(0.72f, 1.0f),
                    startSize = Random.Range(0.06f, 0.12f),
                    startColor = Color.Lerp(_gold, Color.white, 0.42f)
                }, 1);
            }
        }

        private void EmitOrbit(int count, float radius)
        {
            if (_orbitingMotes == null) return;
            _orbitingMotes.Play();
            Vector3 center = _orbitingMotes.transform.position;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, count) +
                              Random.Range(-0.10f, 0.10f);
                Vector3 radial = new Vector3(
                    Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 tangent = new Vector3(-radial.y, radial.x, 0f);
                _orbitingMotes.Emit(new ParticleSystem.EmitParams
                {
                    position = center + radial * Random.Range(
                        radius * 0.82f, radius),
                    velocity = tangent * Random.Range(0.22f, 0.36f) +
                               Vector3.up * 0.06f,
                    startLifetime = Random.Range(0.9f, 1.3f),
                    startSize = Random.Range(0.05f, 0.09f),
                    startColor = Color.Lerp(_gold, Color.white,
                        Random.Range(0.24f, 0.48f))
                }, 1);
            }
        }

        private static Gradient CreateFadeGradient(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(Color.white, 0.45f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(color.a, 0.12f),
                    new GradientAlphaKey(color.a * 0.76f, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Material GetMaterial()
        {
            if (_material != null) return _material;
            Shader shader = Shader.Find("Sprites/Default") ??
                            Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;
            _material = new Material(shader)
            {
                name = "RelicAscensionParticleMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = GetSoftSpark()
            };
            return _material;
        }

        private static Texture2D GetSoftSpark()
        {
            if (_softSpark != null) return _softSpark;
            const int size = 32;
            _softSpark = new Texture2D(
                size, size, TextureFormat.RGBA32, false)
            {
                name = "RelicAscensionSoftSpark",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float center = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - center) / center;
                float dy = Mathf.Abs(y + 0.5f - center) / center;
                float radial = Mathf.Pow(Mathf.Clamp01(
                    1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.15f);
                float star = Mathf.Pow(
                    Mathf.Clamp01(1f - Mathf.Min(dx, dy)), 7f) * 0.55f;
                _softSpark.SetPixel(x, y,
                    new Color(1f, 1f, 1f, Mathf.Max(radial, star)));
            }
            _softSpark.Apply();
            return _softSpark;
        }

        private void OnDisable()
        {
            if (_sequence == null) return;
            StopCoroutine(_sequence);
            _sequence = null;
        }
    }
}
