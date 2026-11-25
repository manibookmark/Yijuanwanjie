using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanjieScroll.UI.Daozang
{
    /// <summary>
    /// 道藏图标按钮的 hover 反馈：缩放 + 阴影，不改颜色。
    /// </summary>
    public sealed class DaozangIconButtonFeedback : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private float _hoverScale = 1.06f;

        private RectTransform _rt;
        private Vector3 _restScale;
        private GameObject _shadowGo;
        private Image _shadowImg;

        public void Bind(Graphic targetGraphic)
        {
            _rt = GetComponent<RectTransform>();
            _restScale = _rt != null ? _rt.localScale : Vector3.one;

            // 创建阴影 Image（在按钮下方）
            if (_shadowGo == null)
            {
                _shadowGo = new GameObject("HoverShadow", typeof(RectTransform), typeof(Image));
                _shadowGo.transform.SetParent(transform, false);
                _shadowGo.transform.SetAsFirstSibling(); // 渲染在按钮下方
                var sRt = _shadowGo.GetComponent<RectTransform>();
                sRt.anchorMin = new(0.5f, 0.5f);
                sRt.anchorMax = new(0.5f, 0.5f);
                sRt.pivot = new(0.5f, 0.5f);
                sRt.sizeDelta = new(0f, 0f); // 跟随目标大小
                sRt.offsetMin = new(-8f, -8f);
                sRt.offsetMax = new(8f, 8f);
                _shadowImg = _shadowGo.GetComponent<Image>();
                _shadowImg.color = new Color(0f, 0f, 0f, 0f);
                _shadowImg.raycastTarget = false;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            if (_rt != null)
                _rt.localScale = _restScale * _hoverScale;
            if (_shadowImg != null)
                _shadowImg.color = new Color(0f, 0f, 0f, 0.25f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            RestoreNormalVisual();
        }

        private void OnDisable() => RestoreNormalVisual();

        private void RestoreNormalVisual()
        {
            if (_rt != null)
                _rt.localScale = _restScale;
            if (_shadowImg != null)
                _shadowImg.color = new Color(0f, 0f, 0f, 0f);
        }
    }
}
