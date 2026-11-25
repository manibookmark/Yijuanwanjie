using System;
using UnityEngine;

namespace WanjieScroll.UI.Presentations
{
    /// <summary>
    /// 驱动角色演出内部的聚散灵光时间线。所有粒子由单个 UI Graphic
    /// 批量绘制；本组件只拥有播放、停止与层级生命周期。
    /// </summary>
    public sealed class CharacterMaterializationCanvasEffect : MonoBehaviour
    {
        private RectTransform _layer;
        private CharacterMaterializationGraphic _graphic;
        private float _elapsed;
        private float _duration;
        private bool _reverse;
        private bool _playing;

        public bool IsPlaying => _playing;

        public void Build(
            RectTransform root,
            RectTransform actor,
            int particleCount = 0,
            float sizeScale = 1f)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));

            Clear();
            Canvas.ForceUpdateCanvases();

            Vector2 rootSize = root.rect.size;
            if (rootSize.x <= 0f || rootSize.y <= 0f)
                rootSize = new Vector2(1920f, 1080f);

            Vector2 actorSize = actor.rect.size;
            if (actorSize.x <= 0f || actorSize.y <= 0f)
                actorSize = new Vector2(420f, 600f);

            Vector2 target = new Vector2(
                (actor.anchorMin.x - 0.5f) * rootSize.x +
                actor.anchoredPosition.x,
                (actor.anchorMin.y - 0.5f) * rootSize.y +
                actor.anchoredPosition.y);

            var layerObject = new GameObject(
                "ScrollSpiritMaterialization",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(CharacterMaterializationGraphic));
            layerObject.transform.SetParent(root, false);
            _layer = layerObject.GetComponent<RectTransform>();
            Stretch(_layer);
            _layer.SetSiblingIndex(actor.GetSiblingIndex());

            _graphic = layerObject.GetComponent<
                CharacterMaterializationGraphic>();
            _graphic.raycastTarget = false;
            _graphic.Configure(
                rootSize,
                target,
                actorSize,
                particleCount,
                sizeScale);
            _layer.gameObject.SetActive(false);
        }

        public void PlayEnter(float duration)
        {
            Begin(false, duration);
        }

        public void PlayExit(float duration)
        {
            Begin(true, duration);
        }

        private void Begin(bool reverse, float duration)
        {
            if (_layer == null || _graphic == null ||
                !_graphic.HasParticles)
            {
                return;
            }

            _reverse = reverse;
            _duration = Mathf.Max(0.05f, duration);
            _elapsed = 0f;
            _playing = true;
            _layer.gameObject.SetActive(true);
            _graphic.SetProgress(0f, _reverse);
        }

        private void Update()
        {
            if (!_playing)
                return;

            _elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(_elapsed / _duration);
            _graphic.SetProgress(normalized, _reverse);
            if (normalized < 1f)
                return;

            _playing = false;
            if (_layer != null)
                _layer.gameObject.SetActive(false);
        }

        public void Clear()
        {
            _playing = false;
            if (_layer != null)
            {
                _layer.gameObject.SetActive(false);
                Destroy(_layer.gameObject);
            }
            _layer = null;
            _graphic = null;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}
