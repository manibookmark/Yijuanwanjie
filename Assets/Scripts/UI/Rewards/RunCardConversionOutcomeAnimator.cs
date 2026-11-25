using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 归元提交成功后的唯一 Canvas 演出所有者。Overlay 只交付卡牌与已冻结
    /// 的地图碑芯锚点；本组件负责居中停留、卡面解形和结束回调，粒子网格细节完全封装在
    /// RunCardConversionParticleGraphic 中。
    /// </summary>
    public sealed class RunCardConversionOutcomeAnimator : MonoBehaviour
    {
        private RectTransform _host;
        private CanvasGroup _cardGroup;
        private RectTransform _cardRect;
        private RectTransform _targetAnchor;
        private RunCardConversionParticleGraphic _particles;
        private Coroutine _routine;
        private Action _onComplete;
        private Vector3 _cardScale = Vector3.one;
        private Quaternion _cardRotation = Quaternion.identity;
        private const float CardHoldDuration = 0.5f;

        public void Prepare(
            RectTransform host,
            CanvasGroup cardGroup,
            RectTransform cardRect,
            RectTransform targetAnchor,
            bool manaConversion = false)
        {
            StopAndClear();
            _host = host;
            _cardGroup = cardGroup;
            _cardRect = cardRect;
            _targetAnchor = targetAnchor;

            if (_cardRect != null)
            {
                _cardScale = _cardRect.localScale;
                _cardRotation = _cardRect.localRotation;
            }
            if (_host == null)
                return;
            GameObject particleObject = new GameObject(
                "GuiyuanParticleField",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RunCardConversionParticleGraphic));
            particleObject.transform.SetParent(_host, false);
            RectTransform particleRect =
                particleObject.GetComponent<RectTransform>();
            RewardPanelFactory.Stretch(particleRect);
            _particles = particleObject.GetComponent<
                RunCardConversionParticleGraphic>();
            _particles.raycastTarget = false;
            _particles.UseManaPalette = manaConversion;
            particleObject.transform.SetAsLastSibling();
        }

        public bool Play(
            RunCardConversionOverlayIntentKind kind,
            Action onComplete)
        {
            if (_particles == null || _targetAnchor == null)
                return false;
            if (kind == RunCardConversionOverlayIntentKind.ConvertCard &&
                (_cardRect == null || _cardGroup == null))
                return false;

            if (_routine != null)
                StopCoroutine(_routine);
            _onComplete = onComplete;
            _routine = StartCoroutine(PlayRoutine(kind));
            return true;
        }

        public void StopAndClear()
        {
            if (_routine != null)
                StopCoroutine(_routine);
            _routine = null;
            _onComplete = null;
            if (_particles != null)
                Destroy(_particles.gameObject);
            _particles = null;
        }

        private IEnumerator PlayRoutine(
            RunCardConversionOverlayIntentKind kind)
        {
            bool convertsCard =
                kind == RunCardConversionOverlayIntentKind.ConvertCard;
            Canvas.ForceUpdateCanvases();
            if (convertsCard)
            {
                float holdElapsed = 0f;
                while (holdElapsed < CardHoldDuration)
                {
                    holdElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                _particles.PlayCardConvergence(_cardRect, _targetAnchor);
            }
            else
            {
                _particles.PlayJadeConvergence(_targetAnchor);
            }
            float duration = _particles.Duration;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                if (convertsCard)
                    AnimateCard(progress);
                yield return null;
            }

            if (convertsCard && _cardGroup != null)
                _cardGroup.alpha = 0f;
            _routine = null;
            Action complete = _onComplete;
            _onComplete = null;
            complete?.Invoke();
        }

        private void AnimateCard(float progress)
        {
            if (_cardGroup == null || _cardRect == null)
                return;

            float dissolve = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.06f, 0.72f, progress));
            _cardGroup.alpha = 1f - dissolve;
            _cardRect.localScale = _cardScale *
                Mathf.Lerp(1f, 0.93f, dissolve);
            _cardRect.localRotation = _cardRotation * Quaternion.Euler(
                0f, 0f, Mathf.Lerp(0f, -4f, dissolve));
        }

        private void OnDisable()
        {
            StopAndClear();
        }
    }
}
