using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.UI.Presentations;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 结算页英灵缩略演出的唯一所有者。只复用正式角色登场配置的透明序列，
    /// 不创建完整演出 Overlay，不发布任何语音或音效请求，也不读取战斗状态。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunSummaryCrawlerAnimator : MonoBehaviour
    {
        private CharacterPresentationProfile _profile;
        private RawImage _actor;
        private RectTransform _actorRect;
        private float _delay;
        private float _frameElapsed;
        private float _entryElapsed;
        private int _frameIndex = -1;
        private Coroutine _entryRoutine;
        private bool _entering;
        private bool _entryStarted;
        private bool _entryCompleted;
        private Vector2 _settledPosition;

        public static bool TryAttach(
            RectTransform tile,
            CharacterPresentationProfile profile,
            float delaySeconds)
        {
            if (tile == null || profile == null || profile.frameAtlas == null)
                return false;

            RunSummaryCrawlerAnimator animator =
                tile.gameObject.AddComponent<RunSummaryCrawlerAnimator>();
            animator.Initialize(profile, delaySeconds);
            return true;
        }

        private void Initialize(
            CharacterPresentationProfile profile,
            float delaySeconds)
        {
            _profile = profile;
            _delay = Mathf.Max(0f, delaySeconds);

            var frame = new GameObject(
                "AnimatedCrawlerFrame",
                typeof(RectTransform));
            frame.transform.SetParent(transform, false);
            RectTransform frameRect = frame.GetComponent<RectTransform>();
            Place(
                frameRect,
                new Vector2(0.04f, 0.05f),
                new Vector2(0.96f, 0.98f));

            var actorObject = new GameObject(
                "AnimatedCrawler",
                typeof(RectTransform),
                typeof(RawImage),
                typeof(AspectRatioFitter));
            actorObject.transform.SetParent(frame.transform, false);
            _actorRect = actorObject.GetComponent<RectTransform>();
            Place(_actorRect, Vector2.zero, Vector2.one);
            _actor = actorObject.GetComponent<RawImage>();
            _actor.texture = profile.ResolvedFrameAtlas;
            _actor.raycastTarget = false;
            AspectRatioFitter fitter = actorObject.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = Mathf.Max(0.01f, profile.FrameAspect);

            ApplyFrame(0);
            SetActorAlpha(0f);
            StartEntry();
        }

        private void StartEntry()
        {
            if (!isActiveAndEnabled || _profile == null || _actorRect == null)
                return;
            StopEntryRoutine();
            _frameElapsed = 0f;
            ApplyFrame(0);
            SetActorAlpha(0f);
            _entryRoutine = StartCoroutine(BeginAfterLayout());
        }

        private IEnumerator BeginAfterLayout()
        {
            yield return null;
            if (_delay > 0f)
                yield return new WaitForSecondsRealtime(_delay);
            if (_profile == null || _actorRect == null)
            {
                _entryRoutine = null;
                yield break;
            }

            Canvas.ForceUpdateCanvases();
            if (!_entryStarted)
                _settledPosition = _actorRect.anchoredPosition;
            _entryStarted = true;
            _entryElapsed = 0f;
            _entering = true;
            _entryRoutine = null;
            ApplyEntryPose(0f);
        }

        private void Update()
        {
            if (_profile == null || _actor == null)
                return;

            _frameElapsed += Time.unscaledDeltaTime;
            int count = Mathf.Max(1, _profile.ResolvedFrameCount);
            int rawFrame = Mathf.FloorToInt(
                _frameElapsed * Mathf.Max(1f, _profile.ResolvedFramesPerSecond));
            int frame = _profile.ResolvedPlaybackMode ==
                        CharacterPresentationPlaybackMode.Loop
                ? rawFrame % count
                : Mathf.Min(rawFrame, count - 1);
            ApplyFrame(frame);

            if (!_entering)
                return;

            float duration = Mathf.Clamp(
                _profile.ResolvedEnterDuration,
                0.24f,
                0.48f);
            _entryElapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(_entryElapsed / duration);
            ApplyEntryPose(normalized);
            if (normalized >= 1f)
            {
                _entering = false;
                _entryCompleted = true;
                RestoreSettledPose();
            }
        }

        private void StopEntryRoutine()
        {
            if (_entryRoutine != null)
                StopCoroutine(_entryRoutine);
            _entryRoutine = null;
        }

        private void RestoreSettledPose()
        {
            if (_actorRect == null) return;
            _actorRect.localScale = Vector3.one;
            if (_entryStarted)
                _actorRect.anchoredPosition = _settledPosition;
            SetActorAlpha(_entryCompleted ? 1f : 0f);
        }

        private void OnDisable()
        {
            StopEntryRoutine();
            _entering = false;
            RestoreSettledPose();
        }

        private void OnEnable()
        {
            if (_entryCompleted)
                RestoreSettledPose();
            else
                StartEntry();
        }

        private void OnDestroy() => StopEntryRoutine();

        private void ApplyEntryPose(float normalized)
        {
            float eased = 1f - Mathf.Pow(1f - normalized, 3f);
            const float startScale = 0.84f;
            float scale = Mathf.Lerp(startScale, 1f, eased);
            scale += Mathf.Sin(normalized * Mathf.PI) * 0.025f;
            _actorRect.localScale = Vector3.one * scale;
            _actorRect.anchoredPosition = Vector2.LerpUnclamped(
                _settledPosition + new Vector2(-18f, -4f),
                _settledPosition,
                eased);
            float alpha = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.52f, 0.90f, normalized));
            SetActorAlpha(alpha);
        }

        private void ApplyFrame(int frameIndex)
        {
            if (_actor == null || _profile == null || frameIndex == _frameIndex)
                return;
            _frameIndex = frameIndex;
            _actor.uvRect = _profile.GetUvRect(frameIndex);
        }

        private void SetActorAlpha(float alpha)
        {
            Color color = _actor.color;
            color.a = Mathf.Clamp01(alpha);
            _actor.color = color;
        }

        private static void Place(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
