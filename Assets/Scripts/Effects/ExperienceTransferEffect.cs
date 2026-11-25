using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// A single world-to-HUD experience transfer. The caller supplies only
    /// generic Unity endpoints, so Effects never references project UI code.
    /// </summary>
    public sealed class ExperienceTransferEffect : MonoBehaviour
    {
        private const int BaseMoteCount = 5;
        private const int MaximumMoteCount = 24;
        private const float LeadMoteSize = 42f;
        private const float MinimumMoteSize = 24f;
        private const float MaximumMoteSize = 34f;

        private static readonly Color CoreColor =
            new Color(1f, 0.78f, 0.22f, 1f);
        private static readonly Color TailColor =
            new Color(1f, 0.48f, 0.06f, 0.94f);
        private static Sprite _moteSprite;
        private sealed class Transfer
        {
            public Canvas Canvas;
            public Camera WorldCamera;
            public Vector3 WorldOrigin;
            public RectTransform Target;
            public int ExperienceAmount;
            public int Generation;
            public GameObject Overlay;
            public Action Completion;
        }

        private readonly HashSet<Transfer> _transfers = new HashSet<Transfer>();
        private IEffectOneShotAudio _oneShotAudio;

        public bool IsPaused { get; private set; }
        public int ActiveTransferCount => _transfers.Count;

        /// <summary>
        /// 注入宿主拥有的一次性音频端口；Effects 不读取音频总线或 AudioSource。
        /// </summary>
        public void SetOneShotAudio(IEffectOneShotAudio oneShotAudio)
        {
            _oneShotAudio = oneShotAudio;
        }

        /// <summary>
        /// Experience uses unscaled time so hit-stop cannot swallow the
        /// collection beat. The level-up checkpoint therefore pauses it
        /// explicitly while its ordinary reward panel owns the scene.
        /// </summary>
        public void SetPaused(bool paused)
        {
            IsPaused = paused;
        }

        /// <summary>
        /// A deliberately simple, capped relationship: every point of actual
        /// experience adds one visible mote above the five-mote base. The cap
        /// protects fill-rate and keeps large rewards readable.
        /// </summary>
        public static int CalculateMoteCount(int experienceAmount)
        {
            return Mathf.Clamp(
                BaseMoteCount + Mathf.Max(1, experienceAmount),
                BaseMoteCount + 1,
                MaximumMoteCount);
        }

        public void Play(
            Canvas canvas,
            Camera worldCamera,
            Vector3 worldOrigin,
            RectTransform target,
            int experienceAmount,
            Action onComplete)
        {
            if (canvas == null || target == null)
            {
                onComplete?.Invoke();
                return;
            }

            var transfer = new Transfer
            {
                Canvas = canvas,
                WorldCamera = worldCamera,
                WorldOrigin = worldOrigin,
                Target = target,
                ExperienceAmount = experienceAmount,
                Completion = onComplete
            };
            _transfers.Add(transfer);
            if (isActiveAndEnabled) StartTransfer(transfer);
        }

        private void StartTransfer(Transfer transfer)
        {
            int generation = ++transfer.Generation;
            StartCoroutine(PlayRoutine(transfer, generation));
        }

        private IEnumerator PlayRoutine(Transfer transfer, int generation)
        {
            try
            {
                Canvas canvas = transfer.Canvas;
                Camera worldCamera = transfer.WorldCamera;
                Vector3 worldOrigin = transfer.WorldOrigin;
                RectTransform target = transfer.Target;
                int experienceAmount = transfer.ExperienceAmount;
                if (!IsCurrent(transfer, generation) || canvas == null || target == null) yield break;
                RectTransform canvasRect = canvas.transform as RectTransform;
                if (canvasRect == null)
                {
                    yield break;
                }

                var overlay = new GameObject(
                    "ExperienceTransfer",
                    typeof(RectTransform));
                transfer.Overlay = overlay;
                overlay.transform.SetParent(canvas.transform, false);
                overlay.transform.SetAsLastSibling();
                RectTransform overlayRect = overlay.GetComponent<RectTransform>();
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
                overlayRect.SetAsLastSibling();

                Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : canvas.worldCamera;
                Vector2 originScreen = RectTransformUtility.WorldToScreenPoint(
                    worldCamera,
                    worldOrigin);
                Vector2 targetScreen = RectTransformUtility.WorldToScreenPoint(
                    canvasCamera,
                    target.position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    overlayRect,
                    originScreen,
                    canvasCamera,
                    out Vector2 start);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    overlayRect,
                    targetScreen,
                    canvasCamera,
                    out Vector2 end);

                int count = CalculateMoteCount(experienceAmount);
                var motes = new List<RectTransform>(count);
                for (int i = 0; i < count; i++)
                {
                    var moteObject = new GameObject(
                        $"ExperienceMote_{i}",
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(Image));
                    moteObject.transform.SetParent(overlayRect, false);
                    RectTransform mote = moteObject.GetComponent<RectTransform>();
                    float normalizedIndex = count > 1
                        ? i / (float)(count - 1)
                        : 0f;
                    float size = i == 0
                        ? LeadMoteSize
                        : Mathf.Lerp(
                            MaximumMoteSize,
                            MinimumMoteSize,
                            normalizedIndex);
                    mote.sizeDelta = new Vector2(size, size);
                    mote.anchoredPosition = start;
                    Image image = moteObject.GetComponent<Image>();
                    image.sprite = GetMoteSprite();
                    image.color = Color.Lerp(
                        CoreColor,
                        TailColor,
                        normalizedIndex);
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                    motes.Add(mote);
                }

                const float duration = 0.44f;
                float stagger = Mathf.Min(
                    0.026f,
                    0.28f / Mathf.Max(1, count - 1));
                float total = duration + stagger * (count - 1);
                float elapsed = 0f;
                Vector2 direction = end - start;
                Vector2 perpendicular = direction.sqrMagnitude > 0.01f
                    ? new Vector2(-direction.y, direction.x).normalized
                    : Vector2.up;

                while (elapsed < total)
                {
                    if (!IsCurrent(transfer, generation) || canvas == null || target == null) yield break;
                    if (IsPaused)
                    {
                        yield return null;
                        continue;
                    }
                    elapsed += Time.unscaledDeltaTime;
                    for (int i = 0; i < motes.Count; i++)
                    {
                        RectTransform mote = motes[i];
                        if (mote == null) continue;
                        float local = Mathf.Clamp01(
                            (elapsed - stagger * i) / duration);
                        float eased = local * local * (3f - 2f * local);
                        float arc = Mathf.Sin(local * Mathf.PI) *
                            (72f + i * 7f) * (i % 2 == 0 ? 1f : -0.7f);
                        mote.anchoredPosition =
                            Vector2.LerpUnclamped(start, end, eased) +
                            perpendicular * arc;
                        float scale = local < 0.18f
                            ? Mathf.Lerp(0.35f, 1f, local / 0.18f)
                            : Mathf.Lerp(1f, 0.35f, (local - 0.18f) / 0.82f);
                        mote.localScale = Vector3.one * scale;
                        mote.localRotation = Quaternion.Euler(
                            0f,
                            0f,
                            local * 120f * (i % 2 == 0 ? 1f : -1f));
                    }
                    yield return null;
                }

                if (!IsCurrent(transfer, generation) || canvas == null || target == null) yield break;
                var pulseObject = new GameObject(
                    "ExperienceArrivalPulse",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                pulseObject.transform.SetParent(overlayRect, false);
                RectTransform pulse = pulseObject.GetComponent<RectTransform>();
                pulse.anchoredPosition = end;
                pulse.sizeDelta = new Vector2(48f, 48f);
                Image pulseImage = pulseObject.GetComponent<Image>();
                pulseImage.sprite = GetMoteSprite();
                pulseImage.preserveAspect = true;
                pulseImage.raycastTarget = false;

                // 经验粒子到达经验条时播放音效
                PlayArrivalSfx();
                float pulseDuration = 0.14f;
                elapsed = 0f;
                while (elapsed < pulseDuration)
                {
                    if (!IsCurrent(transfer, generation) || canvas == null || target == null) yield break;
                    if (IsPaused)
                    {
                        yield return null;
                        continue;
                    }
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / pulseDuration);
                    float eased = 1f - Mathf.Pow(1f - t, 3f);
                    pulse.localScale = Vector3.one *
                        Mathf.Lerp(0.35f, 1.65f, eased);
                    pulseImage.color = new Color(
                        CoreColor.r,
                        CoreColor.g,
                        CoreColor.b,
                        Mathf.Lerp(0.42f, 0f, t));
                    yield return null;
                }

                Complete(transfer, generation, naturallyCompleted: true);
            }
            finally
            {
                Complete(transfer, generation, naturallyCompleted: false);
            }
        }

        private bool IsCurrent(Transfer transfer, int generation) => isActiveAndEnabled &&
            transfer.Generation == generation && _transfers.Contains(transfer);

        private void Complete(Transfer transfer, int generation, bool naturallyCompleted)
        {
            if (transfer.Generation != generation || !_transfers.Remove(transfer)) return;
            Action callback = transfer.Completion;
            transfer.Completion = null;
            DestroyOverlay(transfer);
            // Destroy/explicit cancellation removes presentation only; it must
            // not impersonate an arrival/settlement into a later encounter.
            if (naturallyCompleted && isActiveAndEnabled) callback?.Invoke();
        }

        private static void DestroyOverlay(Transfer transfer)
        {
            if (transfer.Overlay != null)
            {
                transfer.Overlay.SetActive(false);
                Destroy(transfer.Overlay);
                transfer.Overlay = null;
            }
        }

        /// <summary>Abandons this encounter's transfers without arrival callbacks.</summary>
        public void CancelAll()
        {
            foreach (Transfer transfer in new List<Transfer>(_transfers))
                Complete(transfer, transfer.Generation, naturallyCompleted: false);
            StopAllCoroutines();
            IsPaused = false;
        }

        private void OnDisable()
        {
            // Hiding a retained encounter is suspension, not an XP arrival or
            // cancellation. Keep its same callback and immutable hand-off data.
            foreach (Transfer transfer in _transfers)
            {
                transfer.Generation++;
                DestroyOverlay(transfer);
            }
            StopAllCoroutines();
        }

        private void OnEnable()
        {
            foreach (Transfer transfer in new List<Transfer>(_transfers))
                StartTransfer(transfer);
        }

        private void OnDestroy() => CancelAll();

        /// <summary>
        /// RuntimeArtLoader's ink and paper sprites contain dark RGB pixels;
        /// UI tinting multiplies those pixels and therefore cannot turn them
        /// gold. This small white radial sprite has its shape in alpha, so the
        /// semantic gold colors above remain exact on every mote and pulse.
        /// </summary>
        private static Sprite GetMoteSprite()
        {
            if (_moteSprite != null)
                return _moteSprite;

            const int size = 32;
            var texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "ExperienceMoteTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float radial = Mathf.Clamp01(1f - distance);
                    float alpha = Mathf.SmoothStep(0f, 1f, radial);
                    alpha = Mathf.Pow(alpha, 0.72f);
                    pixels[y * size + x] = new Color(
                        1f,
                        1f,
                        1f,
                        alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _moteSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f);
            _moteSprite.name = "ExperienceMoteSprite";
            _moteSprite.hideFlags = HideFlags.HideAndDontSave;
            return _moteSprite;
        }

        private static AudioClip _arrivalClip;
        private static bool _arrivalClipLoaded;
        private void PlayArrivalSfx()
        {
            if (_oneShotAudio == null) return;
            if (!_arrivalClipLoaded)
            {
                _arrivalClip = Resources.Load<AudioClip>("Audio/SFX/xp-bar-arrive-01");
                _arrivalClipLoaded = true;
            }
            if (_arrivalClip != null)
            {
                _oneShotAudio.Play(
                    _arrivalClip,
                    EffectOneShotKind.ExperienceArrival);
            }
        }
    }
}
