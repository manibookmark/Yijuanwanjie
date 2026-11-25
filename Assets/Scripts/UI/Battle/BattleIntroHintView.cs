using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 战斗入场教学提示条（教程关 §5.1 机制演示）：
    /// 入场淡入显示、10 秒后或首次击杀后淡出，非阻断、不遮挡点击。
    /// 文案来自 BattleScenarioProfile.introHintText，为空时整条不创建。
    /// </summary>
    public sealed class BattleIntroHintView : MonoBehaviour
    {
        private const float FadeInSeconds = 0.3f;
        private const float FadeOutSeconds = 0.6f;
        private const float AutoDismissSeconds = 10f;

        private Text _text;
        private CanvasGroup _group;
        private CombatController _combat;
        private Coroutine _fadeRoutine;
        private Coroutine _autoDismissRoutine;
        private bool _killHandlerDetached;

        public static BattleIntroHintView Create(Transform parent)
        {
            var go = new GameObject(
                "BattleIntroHintView", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -100f);
            rect.sizeDelta = new Vector2(820f, 58f);

            var view = go.AddComponent<BattleIntroHintView>();
            view._group = go.AddComponent<CanvasGroup>();
            view._group.blocksRaycasts = false;
            view._group.interactable = false;

            var background = new GameObject(
                "Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(go.transform, false);
            var backgroundImage = background.GetComponent<Image>();
            backgroundImage.color =
                new Color(0.07f, 0.05f, 0.04f, 0.62f);
            backgroundImage.raycastTarget = false;
            BattleHudFactory.Stretch(
                background.GetComponent<RectTransform>());

            var textObject = new GameObject(
                "HintText", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(go.transform, false);
            view._text = textObject.GetComponent<Text>();
            view._text.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            view._text.fontSize = 30;
            view._text.color = new Color(0.93f, 0.88f, 0.78f);
            view._text.alignment = TextAnchor.MiddleCenter;
            view._text.horizontalOverflow = HorizontalWrapMode.Wrap;
            view._text.verticalOverflow = VerticalWrapMode.Truncate;
            view._text.raycastTarget = false;
            BattleHudFactory.Stretch(
                textObject.GetComponent<RectTransform>());

            return view;
        }

        public void Bind(CombatController combat, string text)
        {
            _combat = combat;
            _text.text = text;
            _group.alpha = 0f;
            combat.OnEnemyKilled += HandleFirstKill;
            combat.OnCombatEnded += HandleCombatEnded;
            PlayFade(0f, 1f, FadeInSeconds);
            _autoDismissRoutine = StartCoroutine(AutoDismissAfter());
        }

        private IEnumerator AutoDismissAfter()
        {
            yield return new UnityEngine.WaitForSeconds(AutoDismissSeconds);
            if (!_killHandlerDetached && gameObject.activeSelf)
            {
                _killHandlerDetached = true;
                _combat.OnEnemyKilled -= HandleFirstKill;
                PlayFade(_group.alpha, 0f, FadeOutSeconds,
                    deactivateAtEnd: true);
            }
        }

        private void HandleFirstKill(EnemyStats enemy)
        {
            if (_killHandlerDetached) return;
            _killHandlerDetached = true;
            _combat.OnEnemyKilled -= HandleFirstKill;
            if (_autoDismissRoutine != null)
            {
                StopCoroutine(_autoDismissRoutine);
                _autoDismissRoutine = null;
            }
            if (gameObject.activeSelf)
                PlayFade(_group.alpha, 0f, FadeOutSeconds,
                    deactivateAtEnd: true);
        }

        private void HandleCombatEnded(CombatEndedEvent endedEvent)
        {
            if (_autoDismissRoutine != null)
            {
                StopCoroutine(_autoDismissRoutine);
                _autoDismissRoutine = null;
            }
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }
            _group.alpha = 0f;
            gameObject.SetActive(false);
        }

        private void PlayFade(
            float from,
            float to,
            float duration,
            bool deactivateAtEnd = false)
        {
            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(
                FadeRoutine(from, to, duration, deactivateAtEnd));
        }

        private IEnumerator FadeRoutine(
            float from,
            float to,
            float duration,
            bool deactivateAtEnd)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(
                    from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            _group.alpha = to;
            if (deactivateAtEnd)
                gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_combat == null) return;
            if (!_killHandlerDetached)
                _combat.OnEnemyKilled -= HandleFirstKill;
            _combat.OnCombatEnded -= HandleCombatEnded;
        }
    }
}
