using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// HUD 入场表现：隐藏、淡入和交互开关。用 CanvasGroup 管理整层战斗 UI。
    /// </summary>
    public sealed class BattleEntryPresentation : MonoBehaviour
    {
        private CanvasGroup _canvasGroup;
        private float _fadeDuration = 0.20f;

        /// <summary>初始化。在 Canvas 或其子物体上添加。</summary>
        public void Initialize(CanvasGroup group = null, float fadeDuration = 0.20f)
        {
            _canvasGroup = group;
            _fadeDuration = fadeDuration;
        }

        /// <summary>隐藏 HUD：alpha=0，不可交互，不拦截 raycast。</summary>
        public void PrepareHidden()
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        /// <summary>HUD 立即可见，但在首轮抽牌完成前不接收输入。</summary>
        public void PrepareVisibleNonInteractive()
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        /// <summary>淡入 HUD。</summary>
        public void Reveal(float duration, System.Action onComplete = null)
        {
            float d = duration > 0f ? duration : _fadeDuration;
            if (_canvasGroup == null || d <= 0f)
            {
                onComplete?.Invoke();
                return;
            }
            StartCoroutine(RevealRoutine(d, onComplete));
        }

        /// <summary>启用交互。</summary>
        public void SetInteractable(bool interactable)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.interactable = interactable;
            _canvasGroup.blocksRaycasts = interactable;
        }

        private IEnumerator RevealRoutine(float duration, System.Action onComplete)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _canvasGroup.alpha = t;
                yield return null;
            }
            _canvasGroup.alpha = 1f;
            onComplete?.Invoke();
        }
    }
}
