using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanjieScroll.UI.Components
{
    /// <summary>
    /// 挂在 UGUI 元素上的悬停缩放效果。
    /// 统一替代散落在各 Controller 中的 ButtonHoverEffect / PortalHoverEffect。
    /// <para>支持缩放 + 可选提亮。</para>
    /// </summary>
    [AddComponentMenu("UI/Hover Scale Effect")]
    public class HoverScaleEffect : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private float _normalScale = 1f;
        [SerializeField] private float _hoverScale = 1.1f;
        [SerializeField] private bool _brightenOnHover = false;
        [SerializeField] private float _normalBrightness = 0.85f;
        [SerializeField] private float _hoverBrightness = 1f;

        /// <summary>正常缩放倍率。</summary>
        public float NormalScale { get => _normalScale; set => _normalScale = value; }
        /// <summary>悬停缩放倍率。</summary>
        public float HoverScale { get => _hoverScale; set => _hoverScale = value; }
        /// <summary>是否在悬停时提亮。</summary>
        public bool BrightenOnHover { get => _brightenOnHover; set => _brightenOnHover = value; }

        // ── 向后兼容（旧代码用 camelCase + Vector3 赋值） ──
        [Obsolete("使用 NormalScale 替代")]
        public Vector3 normalScale
        {
            get => Vector3.one * _normalScale;
            set => _normalScale = value.x;
        }
        [Obsolete("使用 HoverScale 替代")]
        public Vector3 hoverScale
        {
            get => Vector3.one * _hoverScale;
            set => _hoverScale = value.x;
        }

        private RectTransform _rt;
        private Image _image;
        private Vector3 _originalScale;

        private void Awake()
        {
            _rt = GetComponent<RectTransform>();
            _image = GetComponent<Image>();
            _originalScale = _rt != null ? _rt.localScale : Vector3.one;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            if (_rt != null)
                _rt.localScale = _originalScale * _hoverScale;

            if (_brightenOnHover && _image != null)
                _image.color = new Color(_hoverBrightness, _hoverBrightness, _hoverBrightness, 1f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            RestoreNormalVisual();
        }

        private void OnDisable() => RestoreNormalVisual();

        private void RestoreNormalVisual()
        {
            if (_rt != null)
                _rt.localScale = _originalScale * _normalScale;

            if (_brightenOnHover && _image != null)
                _image.color = new Color(_normalBrightness, _normalBrightness, _normalBrightness, 1f);
        }
    }

    /// <summary>
    /// 向后兼容别名。旧代码引用 ButtonHoverEffect 的地方保持可用，
    /// 新代码请直接使用 HoverScaleEffect。
    /// </summary>
    [Obsolete("使用 HoverScaleEffect 替代")]
    [AddComponentMenu("")] // 不在菜单中显示
    public sealed class ButtonHoverEffect : HoverScaleEffect { }
}
