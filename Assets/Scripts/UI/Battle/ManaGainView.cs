using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// Owns resource-particle flight, cancellation and one arrival receipt.
    /// Core already owns the resource amount; this controller only holds its
    /// HUD display until the shared MP/maximum-health particle stream arrives.
    /// All transient graphics belong to one root and this lifecycle owner.
    /// </summary>
    public sealed class ManaGainView : MonoBehaviour
    {
        private ResourceGainPresentation _presentation = ResourceGainPresentation.Mana;
        private Image _target;
        private Coroutine _routine;
        private readonly List<GameObject> _transients = new List<GameObject>();
        private Action _onArrived;
        private bool _awaitingArrival;
        private int _receiptVersion;
        private static Sprite _sparkSprite;
        private static Sprite _neutralSparkSprite;
        private static Texture2D _neutralSparkTexture;

        // Immutable choreography sampled once per MP receipt. Animation never
        // consumes gameplay randomness or resamples a jittering path per frame.
        private struct ManaFlight
        {
            internal Vector2 Source, Destination, ControlA, ControlB, Side, Drift;
            internal float Delay, Duration, Acceleration, Phase, Sway, Turns, Spin, Opacity, EndScale,
                Reveal, FadeStart;
        }

        private struct ManaArrival
        {
            internal Vector2 Origin, Direction;
            internal float InnerRadius, OuterRadius, Speed, Opacity, Delay, Duration, EndScale;
        }

        public bool IsAwaitingArrival => _awaitingArrival;

        /// <summary>Invalidates the old encounter's receipt without arrival.</summary>
        public void CancelPendingPresentation()
        {
            CancelActive(commitPending: false);
        }

        public void Bind(Image target)
        {
            _target = target;
        }

        /// <summary>Shares particle choreography while injecting resource colours.</summary>
        internal void Configure(ResourceGainPresentation presentation)
        {
            _presentation = presentation ?? ResourceGainPresentation.Mana;
        }

        public void Play(int amount, Action onArrived)
        {
            if (amount <= 0 || !isActiveAndEnabled || _target == null)
            {
                onArrived?.Invoke();
                return;
            }

            CancelActive(commitPending: false);
            _onArrived = onArrived;
            _awaitingArrival = true;
            StartGainRoutine(amount);
        }

        /// <summary>Holds the resource number until its particle stream arrives.</summary>
        public Action<Vector2> Prepare(int amount, Action onArrived)
        {
            CancelActive(commitPending: false);
            int version = ++_receiptVersion;
            _onArrived = onArrived;
            _awaitingArrival = true;
            bool consumed = false;
            return screen =>
            {
                if (consumed) return;
                consumed = true;
                if (this == null || version != _receiptVersion || !_awaitingArrival) return;
                if (amount <= 0 || !isActiveAndEnabled || _target == null)
                { SignalArrived(); return; }
                StartGainRoutine(amount, screen);
            };
        }

        public void PlaySand(int amount, Vector2 sourceScreen, Action onArrived)
            => Prepare(amount, onArrived)?.Invoke(sourceScreen);

        private void StartGainRoutine(int amount, Vector2? sourceScreen = null)
        {
            int version = _receiptVersion;
            Coroutine started = StartCoroutine(GainRoutine(amount, sourceScreen));
            // A missing Canvas can finish synchronously. An arrival callback may
            // also start a newer receipt; never overwrite its coroutine handle.
            if (version == _receiptVersion && _awaitingArrival) _routine = started;
        }

        private IEnumerator GainRoutine(int amount, Vector2? sourceScreen)
        {
            int version = _receiptVersion;
            try
            {
                ResourceGainPresentation presentation = _presentation ?? ResourceGainPresentation.Mana;
                bool mana = presentation == ResourceGainPresentation.Mana;
                // A book or mana talisman supplies its actual manifestation
                // point. Scatter that transfer without changing the standard
                // seal-rebate stream or the health presentation.
                bool organicMana = mana && sourceScreen.HasValue;
                Canvas canvas = GetComponentInParent<Canvas>();
                RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
                if (canvasRect == null || _target == null) yield break;
                Sprite spark = mana ? GetNeutralSparkSprite() : GetSparkSprite();
                Canvas.ForceUpdateCanvases();
                RectTransform root = CreateRoot(canvas.transform, presentation.RootName);
                _transients.Add(root.gameObject);

                Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                Vector2 destination = ResolveCanvasPoint(canvasRect, _target.rectTransform, uiCamera);
                Rect bounds = canvasRect.rect;
                Vector2 start = new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, 0.5f),
                    Mathf.Lerp(bounds.yMin, bounds.yMax, 0.2f));
                if (sourceScreen.HasValue && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, sourceScreen.Value, uiCamera, out Vector2 manifestedPoint)) start = manifestedPoint;
                Vector2 control = (start + destination) * 0.5f + Vector2.up * 120f;

                // Counts describe visual density only, never resource amounts.
                int density = Mathf.Clamp(amount, 1, mana ? 3 : 5);
                int count = 36 + (density - 1) * (mana ? 12 : 6);
                var particles = new List<Image>(count);
                var manaFlights = organicMana ? new ManaFlight[count] : null;
                var random = organicMana ? new System.Random(unchecked(GetInstanceID() * 397 ^
                    version * 31 ^ Time.frameCount)) : null;
                for (int i = 0; i < count; i++)
                {
                    Image particle = CreateImage(root, presentation.ParticlePrefix + "Flight_" + i,
                        spark, ParticleColor(presentation, i, organicMana ? 0f : 1f),
                        organicMana ? Sample(random, 4.5f, 9.5f) : 8f + i % 3 * 2f);
                    if (organicMana)
                    {
                        manaFlights[i] = SampleManaFlight(start, destination, random, i == 0);
                        particle.rectTransform.localPosition = manaFlights[i].Source;
                    }
                    else particle.rectTransform.localPosition = start + SourceOffset(i, count);
                    particles.Add(particle);
                }

                // A few small sparks finish at the real resource target. No
                // resource icon, halo, label or background is created here.
                int arrivalCount = (organicMana ? 6 : 12) + Mathf.Min(2, density - 1) * 2;
                var arrivals = new List<Image>(arrivalCount);
                var manaArrivals = organicMana ? new ManaArrival[arrivalCount] : null;
                for (int i = 0; i < arrivalCount; i++)
                {
                    Image particle = CreateImage(root, presentation.ParticlePrefix + "Arrival_" + i,
                        spark, ParticleColor(presentation, i, organicMana ? 0f : 1f),
                        organicMana ? Sample(random, 4f, 8f) : 6f + i % 3 * 2f);
                    particle.rectTransform.localPosition = destination;
                    if (!organicMana) particle.gameObject.SetActive(false);
                    if (organicMana)
                    {
                        float angle = Sample(random, 0f, Mathf.PI * 2f);
                        // Use different flights, so a repeated random pick
                        // cannot light several arrival sparks at the same time.
                        ManaFlight arriving = manaFlights[i * manaFlights.Length / arrivalCount];
                        manaArrivals[i] = new ManaArrival
                        {
                            Origin = arriving.Destination,
                            Direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
                            InnerRadius = Sample(random, 1f, 3.5f), OuterRadius = Sample(random, 6f, 14f),
                            Speed = Sample(random, 0.8f, 1.8f), Opacity = Sample(random, 0.35f, 0.6f),
                            Delay = arriving.Delay + arriving.Duration,
                            Duration = Sample(random, 0.04f, 0.075f),
                            EndScale = Sample(random, 0.12f, 0.32f)
                        };
                    }
                    arrivals.Add(particle);
                }
                // MP arrivals follow individual flights, while later motes are
                // still travelling. There is no collective flight-to-burst cut.
                // The same owner still commits the resource receipt only once.
                const float flightDuration = 0.3f;
                const float arrivalDuration = 0.12f;
                float elapsed = 0f;
                float transferDuration = organicMana ? flightDuration + arrivalDuration : flightDuration;
                while (elapsed < transferDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / flightDuration);
                    for (int i = 0; i < particles.Count; i++)
                    {
                        if (organicMana)
                        {
                            RenderManaFlight(particles[i], manaFlights[i], elapsed, presentation, i);
                            continue;
                        }
                        float flight = t;
                        float phase = i * 2.399963f;
                        Vector2 lane = control + new Vector2(Mathf.Cos(phase) * 64f, (i % 5 - 2) * 32f);
                        RectTransform rect = particles[i].rectTransform;
                        float travel = 1f - (1f - flight) * (1f - flight);
                        rect.localPosition = Bezier(start + SourceOffset(i, count), lane, destination, travel);
                        rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.65f, flight);
                        rect.localRotation = Quaternion.Euler(0f, 0f, i * 31f + flight * 120f);
                        float fade = 1f - Smooth01(Mathf.InverseLerp(0.85f, 1f, flight));
                        particles[i].color = ParticleColor(presentation, i, fade);
                    }
                    if (organicMana)
                        for (int i = 0; i < arrivals.Count; i++)
                            RenderManaArrival(arrivals[i], manaArrivals[i], elapsed, presentation, i);
                    yield return null;
                }
                foreach (Image particle in particles) particle.gameObject.SetActive(false);
                if (!organicMana)
                {
                    foreach (Image particle in arrivals) particle.gameObject.SetActive(true);
                    elapsed = 0f;
                    while (elapsed < arrivalDuration)
                    {
                        elapsed += Time.unscaledDeltaTime;
                        float eased = Smooth01(Mathf.Clamp01(elapsed / arrivalDuration));
                        for (int i = 0; i < arrivals.Count; i++)
                        {
                            float phase = i * 2.399963f;
                            float radius = Mathf.Lerp(5f + i % 3 * 2f, 28f + i % 5 * 7f, eased);
                            arrivals[i].rectTransform.localPosition = destination +
                                new Vector2(Mathf.Cos(phase), Mathf.Sin(phase)) * radius;
                            arrivals[i].color = ParticleColor(presentation, i, 1f - eased);
                        }
                        yield return null;
                    }
                }
            }
            finally
            {
                if (version == _receiptVersion)
                {
                    _routine = null;
                    DestroyTransients();
                    // Clean old graphics before invoking a callback that can
                    // immediately start the next resource receipt.
                    SignalArrived();
                }
            }
        }

        private static Vector2 SourceOffset(int index, int count)
        {
            float phase = index * 2.399963f;
            float radius = 32f + 44f * Mathf.Sqrt((index + 0.5f) / count);
            return new Vector2(Mathf.Cos(phase) * radius, Mathf.Sin(phase) * radius * 0.72f);
        }

        private static ManaFlight SampleManaFlight(Vector2 start, Vector2 destination,
            System.Random random, bool first)
        {
            float angle = Sample(random, 0f, Mathf.PI * 2f);
            // A soft cloud has no uniform rim or rectangular spawning grid.
            // Sample once; the particle follows a continuous path afterward.
            float radius = Mathf.Min(120f, Mathf.Sqrt(-2f * Mathf.Log(
                Mathf.Max(0.0001f, (float)random.NextDouble()))) * 38f);
            Vector2 source = start + new Vector2(Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius * Sample(random, 0.55f, 0.9f));
            // Absorb within the MP centre instead of stacking every mote on
            // one exact pixel. This is visual scatter, not a resource offset.
            float arrivalAngle = Sample(random, 0f, Mathf.PI * 2f);
            Vector2 end = destination + new Vector2(Mathf.Cos(arrivalAngle), Mathf.Sin(arrivalAngle)) *
                (Mathf.Sqrt((float)random.NextDouble()) * 8f);
            Vector2 delta = end - source;
            Vector2 side = delta.sqrMagnitude > 1f ? new Vector2(-delta.y, delta.x).normalized : Vector2.right;
            float delay = first ? 0f : Sample(random, 0f, 0.10f);
            Vector2 bendA = SampleSoftOffset(random, 88f, 46f);
            Vector2 bendB = SampleSoftOffset(random, 58f, 38f);
            float launchAngle = Sample(random, 0f, Mathf.PI * 2f);
            Vector2 launch = new Vector2(Mathf.Cos(launchAngle), Mathf.Sin(launchAngle)) * Sample(random, 24f, 68f);
            return new ManaFlight
            {
                Source = source, Destination = end, Side = side,
                Drift = new Vector2(Sample(random, 8f, 28f), Sample(random, 8f, 24f)),
                ControlA = Vector2.Lerp(source, end, Sample(random, 0.16f, 0.34f)) +
                    launch + side * bendA.x + Vector2.up * bendA.y,
                ControlB = Vector2.Lerp(source, end, Sample(random, 0.58f, 0.82f)) +
                    side * bendB.x + Vector2.up * (18f + bendB.y),
                Delay = delay, Duration = Sample(random, 0.16f, 0.3f - delay),
                Acceleration = Sample(random, 0.8f, 1.85f), Phase = Sample(random, 0f, Mathf.PI * 2f),
                Sway = Sample(random, 8f, 24f), Turns = Sample(random, 0.55f, 1.8f),
                Spin = Sample(random, -150f, 150f), Opacity = Sample(random, 0.55f, 0.95f),
                EndScale = Sample(random, 0.3f, 0.65f), Reveal = Sample(random, 0.06f, 0.16f),
                FadeStart = Sample(random, 0.66f, 0.84f)
            };
        }

        private static Vector2 SampleSoftOffset(System.Random random, float width, float height)
        {
            float angle = Sample(random, 0f, Mathf.PI * 2f);
            float radius = Mathf.Min(3f, Mathf.Sqrt(-2f * Mathf.Log(
                Mathf.Max(0.0001f, (float)random.NextDouble()))));
            return new Vector2(Mathf.Cos(angle) * radius * width, Mathf.Sin(angle) * radius * height);
        }

        private static void RenderManaArrival(Image image, ManaArrival mote,
            float elapsed, ResourceGainPresentation presentation, int index)
        {
            float life = Mathf.Clamp01((elapsed - mote.Delay) / mote.Duration);
            float spread = Mathf.Clamp01(Smooth01(life) * mote.Speed);
            image.rectTransform.localPosition = mote.Origin + mote.Direction *
                Mathf.Lerp(mote.InnerRadius, mote.OuterRadius, spread);
            image.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, mote.EndScale, life);
            float reveal = Smooth01(Mathf.InverseLerp(0f, 0.18f, life));
            image.color = ParticleColor(presentation, index,
                reveal * (1f - Smooth01(life)) * mote.Opacity);
        }

        private static void RenderManaFlight(Image image, ManaFlight mote,
            float elapsed, ResourceGainPresentation presentation, int index)
        {
            float flight = Mathf.Clamp01((elapsed - mote.Delay) / mote.Duration);
            float travel = 1f - Mathf.Pow(1f - flight, mote.Acceleration);
            float inverse = 1f - travel;
            Vector2 point = inverse * inverse * inverse * mote.Source +
                3f * inverse * inverse * travel * mote.ControlA +
                3f * inverse * travel * travel * mote.ControlB + travel * travel * travel * mote.Destination;
            // Gentle independent bends vanish at both endpoints, so every
            // point still converges within the real MP centre rather than a lane.
            point += mote.Side * Mathf.Sin(travel * Mathf.PI) * mote.Sway *
                (Mathf.Sin(mote.Phase + travel * mote.Turns * Mathf.PI * 2f) - Mathf.Sin(mote.Phase));
            float driftPhase = mote.Phase + travel * mote.Turns * Mathf.PI * 2f;
            point += Mathf.Sin(travel * Mathf.PI) * new Vector2(
                (Mathf.Sin(driftPhase * 0.73f) - Mathf.Sin(mote.Phase * 0.73f)) * mote.Drift.x,
                (Mathf.Cos(driftPhase * 1.17f) - Mathf.Cos(mote.Phase * 1.17f)) * mote.Drift.y);
            image.rectTransform.localPosition = point;
            image.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, mote.EndScale, flight);
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
                mote.Phase * Mathf.Rad2Deg + flight * mote.Spin);
            float reveal = Smooth01(Mathf.InverseLerp(0f, mote.Reveal, flight));
            float fade = 1f - Smooth01(Mathf.InverseLerp(mote.FadeStart, 1f, flight));
            float glimmer = 0.85f + 0.15f * Mathf.Sin(mote.Phase + flight * mote.Turns * Mathf.PI * 2f);
            image.color = ParticleColor(presentation, index, reveal * fade * mote.Opacity * glimmer);
        }

        private static float Sample(System.Random random, float min, float max)
            => Mathf.Lerp(min, max, (float)random.NextDouble());

        private static Color ParticleColor(ResourceGainPresentation presentation, int index, float opacity)
        {
            Color color = presentation == ResourceGainPresentation.Mana
                ? presentation.MoteColor
                : Color.Lerp(presentation.MoteColor, presentation.HighlightColor, index % 4 * 0.22f);
            color.a *= Mathf.Clamp01(opacity);
            return color;
        }

        private static Sprite GetSparkSprite()
        {
            if (_sparkSprite != null) return _sparkSprite;
            _sparkSprite = Resources.Load<Sprite>("Sprites/VFX/spark_particle");
            if (_sparkSprite != null) return _sparkSprite;
            _sparkSprite = GetNeutralSparkSprite();
            return _sparkSprite;
        }

        private static Sprite GetNeutralSparkSprite()
        {
            if (_neutralSparkSprite != null) return _neutralSparkSprite;
            // The shared spark asset has a yellow/orange rim. MP uses white RGB
            // with soft alpha so the book's blue-white colour survives tinting.
            // This private runtime cache also serves the missing-HP-spark fallback.
            const int size = 32;
            _neutralSparkTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ResourceParticleSoftPoint", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size * 0.5f) / (size * 0.5f);
                    float dy = (y + 0.5f - size * 0.5f) / (size * 0.5f);
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - dx * dx - dy * dy), 2f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            _neutralSparkTexture.SetPixels(pixels);
            _neutralSparkTexture.Apply(false, true);
            _neutralSparkSprite = Sprite.Create(_neutralSparkTexture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
            _neutralSparkSprite.name = "ResourceParticleSoftPoint";
            _neutralSparkSprite.hideFlags = HideFlags.HideAndDontSave;
            return _neutralSparkSprite;
        }

        private void OnDisable() => CancelActive();

        private void CancelActive(bool commitPending = true)
        {
            _receiptVersion++;
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            DestroyTransients();
            if (commitPending) SignalArrived();
            else { _awaitingArrival = false; _onArrived = null; }
        }

        private void SignalArrived()
        {
            if (!_awaitingArrival) return;
            _awaitingArrival = false;
            Action callback = _onArrived;
            _onArrived = null;
            callback?.Invoke();
        }

        private void DestroyTransients()
        {
            foreach (GameObject transient in _transients)
                if (transient != null)
                {
                    if (Application.isPlaying) Destroy(transient);
                    else DestroyImmediate(transient);
                }
            _transients.Clear();
        }

        private static RectTransform CreateRoot(Transform parent, string name)
        {
            var rootObject = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            rootObject.transform.SetParent(parent, false);
            RectTransform rect = rootObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.localPosition = Vector3.zero;
            CanvasGroup group = rootObject.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            rootObject.transform.SetAsLastSibling();
            return rect;
        }

        private static Image CreateImage(Transform parent, string name, Sprite sprite, Color color, float size)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            item.transform.SetParent(parent, false);
            Image image = item.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax =
                image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.sizeDelta = Vector2.one * size;
            return image;
        }

        private static Vector2 ResolveCanvasPoint(RectTransform canvasRect, RectTransform target, Camera uiCamera)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, target.position);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCamera, out Vector2 result)
                ? result : Vector2.zero;
        }

        private static Vector2 Bezier(Vector2 start, Vector2 control, Vector2 end, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * start + 2f * inverse * t * control + t * t * end;
        }

        private static float Smooth01(float value) => value * value * (3f - 2f * value);
    }

    /// <summary>Resource palette and particle naming only; one controller owns all animation state.</summary>
    internal sealed class ResourceGainPresentation
    {
        internal static readonly ResourceGainPresentation Mana = new ResourceGainPresentation(
            "ManaGainTransfer", "ManaSpark_", new Color(0.6216f, 0.8632f, 1f, 0.8f),
            new Color(0.6216f, 0.8632f, 1f, 0.8f));

        internal static readonly ResourceGainPresentation Health = new ResourceGainPresentation(
            "MaxHealthGainTransfer", "VitalitySpark_", new Color(1f, 0.68f, 0.18f, 0.82f),
            new Color(1f, 0.94f, 0.72f, 0.95f));

        internal string RootName { get; }
        internal string ParticlePrefix { get; }
        internal Color MoteColor { get; }
        internal Color HighlightColor { get; }

        private ResourceGainPresentation(string rootName, string particlePrefix, Color moteColor, Color highlightColor)
        {
            RootName = rootName;
            ParticlePrefix = particlePrefix;
            MoteColor = moteColor;
            HighlightColor = highlightColor;
        }
    }
}
