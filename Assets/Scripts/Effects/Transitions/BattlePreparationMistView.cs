using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.Effects.Transitions
{
    /// <summary>
    /// 入战准备的独立视觉 owner：常驻复用背景预览、柔雾和超时弱提示。
    /// Begin 到 Reveal 之前，纯不透明底保证真实场景永远不可见；
    /// 只有外层收到 DestinationReady 后才能命令 Reveal。隐藏时释放场景纹理引用。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattlePreparationMistView : MonoBehaviour
    {
        private const float RevealDuration = 0.42f;
        // Normal combat construction is sub-second. Keep the visual language
        // free of a loading label; only a genuinely stalled entry gets a quiet
        // hint after the transition timeout has become perceptible.
        // private const float SlowPreparationHintDelay = 2.8f;
        private CanvasGroup _rootOpacity;
        private CanvasGroup _previewOpacity;
        private RawImage _background;
        private BattleMistGraphic _mist;
        private Text _hint;
        private bool _visible;
        private bool _revealing;
        private float _elapsed;
        private float _revealTime;
        private float _backgroundAspect = 16f / 9f;
        private float _backgroundHeight;

        public bool IsRevealed => !_visible;

        public void Initialize(Font font)
        {
            _rootOpacity = gameObject.AddComponent<CanvasGroup>();
            _rootOpacity.alpha = 0f;
            _rootOpacity.blocksRaycasts = false;
            _rootOpacity.interactable = false;

            GameObject preview = CreateRect("OpaqueBattlePreview", transform);
            _previewOpacity = preview.AddComponent<CanvasGroup>();
            Image backing = preview.AddComponent<Image>();
            // The cached background itself is opaque; this backing is only a
            // cool ink-blue veil. Keeping it translucent lets the individual
            // paper-cloud puffs read as fog instead of becoming a flat black
            // mask while still hiding any unready scene behind the preview.
            backing.color = new Color(0.08f, 0.14f, 0.17f, 0.26f);
            backing.raycastTarget = true;

            GameObject background = CreateRect("CachedBattleBackground", preview.transform);
            _background = background.AddComponent<RawImage>();
            _background.raycastTarget = false;
            _background.rectTransform.anchorMin = _background.rectTransform.anchorMax =
                new Vector2(0.5f, 0.5f);

            // Tuanjie's UI bridge does not consistently materialize
            // RequireComponent(CanvasRenderer) when a MaskableGraphic is
            // added dynamically.  Without an explicit renderer the mesh is
            // populated but never reaches the canvas, which leaves only the
            // dark cached background and makes the transition look like a
            // black mask.
            GameObject mist = new GameObject(
                "DriftingBattleMist",
                typeof(RectTransform),
                typeof(CanvasRenderer));
            mist.transform.SetParent(transform, false);
            var mistRect = (RectTransform)mist.transform;
            mistRect.anchorMin = Vector2.zero;
            mistRect.anchorMax = Vector2.one;
            mistRect.offsetMin = mistRect.offsetMax = Vector2.zero;
            _mist = mist.AddComponent<BattleMistGraphic>();
            _mist.raycastTarget = false;
            // The procedural fog is intentionally disabled for the shipped
            // transition. It held on its first frame while combat prepared,
            // which read as a frozen overlay instead of a continuous reveal.
            // Keep the component available for a future authored effect, but
            // use the stable cached-battle preview and paper tear now.
            _mist.enabled = false;
            // The mist must sit over the cached battle floor. The previous
            // first-sibling order placed it behind OpaqueBattlePreview, leaving
            // only the darkened background visible and making the effect look
            // like a black mask.
            mist.transform.SetAsLastSibling();

            GameObject hint = CreateRect("SlowPreparationHint", transform);
            _hint = hint.AddComponent<Text>();
            _hint.font = font;
            _hint.fontSize = 22;
            _hint.text = "正在布阵…";
            _hint.alignment = TextAnchor.MiddleCenter;
            _hint.raycastTarget = false;
            RectTransform hintRect = _hint.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0.15f);
            hintRect.sizeDelta = new Vector2(480f, 48f);
            _hint.color = new Color(0.94f, 0.95f, 0.91f, 0f);
        }

        public void Begin(Sprite background, Color tint)
        {
            _visible = true;
            _revealing = false;
            _elapsed = 0f;
            _revealTime = 0f;
            _rootOpacity.alpha = 1f;
            _rootOpacity.blocksRaycasts = true;
            _previewOpacity.alpha = 1f;
            _background.texture = background != null ? background.texture : null;
            _background.uvRect = GetSpriteUv(background);
            _backgroundAspect = background != null && background.rect.height > 0f
                ? background.rect.width / background.rect.height : 16f / 9f;
            _backgroundHeight = 0f;
            FitBackground();
            tint.a = 1f;
            _background.color = background != null
                ? tint * new Color(0.84f, 0.87f, 0.86f, 1f) : Color.clear;
            _hint.color = new Color(0.94f, 0.95f, 0.91f, 0f);
            _mist.SetMotion(0f, 0f);
        }

        public void Reveal()
        {
            if (!_visible || _revealing)
                return;
            // The cached preview exists only to cover the preparation hitch.
            // Once the destination is ready, expose the real combat scene in
            // the same frame. Fading this layer made the battlefield and its
            // enemies look as if they were slowly materializing after load.
            _hint.color = new Color(0.94f, 0.95f, 0.91f, 0f);
            Hide();
        }

        public void Hide()
        {
            _visible = false;
            _revealing = false;
            if (_rootOpacity != null)
            {
                _rootOpacity.alpha = 0f;
                _rootOpacity.blocksRaycasts = false;
            }
            if (_background != null)
                _background.texture = null;
        }

        private void Update()
        {
            if (!_visible)
                return;
            FitBackground();
            _elapsed += Time.unscaledDeltaTime;
            float reveal = 0f;
            if (_revealing)
            {
                _revealTime += Time.unscaledDeltaTime;
                reveal = Mathf.Clamp01(_revealTime / RevealDuration);
                // 先拨动雾，再让预览退去。未经 Ready 的准备阶段永远不会走这里。
                float fade = Mathf.Clamp01((reveal - 0.10f) / 0.58f);
                _previewOpacity.alpha = 1f - fade * fade * (3f - 2f * fade);
            }
            else
            {
                // float hintAlpha = Mathf.Clamp01((_elapsed - SlowPreparationHintDelay) / 0.3f);
                // 试玩复审：暂不显示“正在布阵”Loading 文案；保留原计时和就绪流程。
                // _hint.color = new Color(0.94f, 0.95f, 0.91f, hintAlpha * 0.67f);
                _hint.color = new Color(0.94f, 0.95f, 0.91f, 0f);
            }

            _mist.SetMotion(_elapsed, reveal);
            if (_revealing && reveal >= 1f)
                Hide();
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            var rect = (RectTransform)result.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return result;
        }

        private void FitBackground()
        {
            // 与真实 BattlefieldView 一致：固定垂直舞台，按原比例显露/裁切两侧。
            // 不能把宽版背景拉成屏幕比例，否则散雾时会发生明显的画面跳变。
            float height = ((RectTransform)transform).rect.height;
            if (height <= 0f || Mathf.Approximately(height, _backgroundHeight))
                return;
            _backgroundHeight = height;
            _background.rectTransform.sizeDelta = new Vector2(height * _backgroundAspect, height);
        }

        private static Rect GetSpriteUv(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
                return new Rect(0f, 0f, 1f, 1f);
            Rect rect = sprite.textureRect;
            return new Rect(rect.x / sprite.texture.width, rect.y / sprite.texture.height,
                rect.width / sprite.texture.width, rect.height / sprite.texture.height);
        }

        private void OnDisable() => Hide();
    }
}
