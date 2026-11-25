using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 卡牌假投影系统（炉石/杀戮尖塔风格）。
    /// 复制卡牌形状的纯黑剪影，压扁、拉大、偏移、降透明度。
    /// 随卡牌旋转而自然变化，hover 时投影拉远变大变淡。
    /// </summary>
    [ExecuteAlways]
    public class CardDropShadow : MonoBehaviour
    {
        [Header("投影基础参数")]
        [Range(0f, 1f)] public float maxAlpha = 0.3f;
        public Vector2 shadowOffset = new Vector2(0f, -15f);
        [Range(1f, 1.2f)] public float shadowScale = 1.05f;

        [Header("悬浮状态参数")]
        public float hoverOffsetMultiplier = 2.5f;
        public float hoverScaleMultiplier = 1.01f;
        public float hoverAlphaMultiplier = 1.15f;

        private Image _shadowImage;
        private RectTransform _shadowRect;
        private RectTransform _parentRect;

        private bool _isHovering;
        private float _currentAlpha;
        private Vector2 _currentOffset;
        private float _currentScale;

        void Awake()
        {
            _shadowImage = GetComponent<Image>();
            _shadowRect = GetComponent<RectTransform>();
            _parentRect = _shadowRect.parent as RectTransform;

            _currentAlpha = 0f;
            _currentOffset = shadowOffset;
            _currentScale = shadowScale;
            SetAlpha(0f);
        }

        void LateUpdate()
        {
            UpdateShadowTransform();
        }

        public void OnHoverEnter()
        {
            _isHovering = true;
            _currentAlpha = maxAlpha * hoverAlphaMultiplier;
            _currentOffset = shadowOffset * hoverOffsetMultiplier;
            _currentScale = shadowScale * hoverScaleMultiplier;
        }

        public void OnHoverExit()
        {
            _isHovering = false;
            _currentAlpha = maxAlpha;
            _currentOffset = shadowOffset;
            _currentScale = shadowScale;
        }

        public void Show()
        {
            _currentAlpha = _isHovering ? maxAlpha * hoverAlphaMultiplier : maxAlpha;
            _currentOffset = _isHovering ? shadowOffset * hoverOffsetMultiplier : shadowOffset;
            _currentScale = _isHovering ? shadowScale * hoverScaleMultiplier : shadowScale;
        }

        public void Hide()
        {
            _currentAlpha = 0f;
        }

        private void SetAlpha(float alpha)
        {
            if (_shadowImage != null)
            {
                Color c = _shadowImage.color;
                c.a = Mathf.Clamp01(alpha);
                _shadowImage.color = c;
            }
        }

        private void UpdateShadowTransform()
        {
            if (_parentRect == null || _shadowRect == null) return;

            // 同步父节点旋转和缩放
            _shadowRect.localRotation = _parentRect.localRotation;
            _shadowRect.localScale = Vector3.one * (_parentRect.localScale.x * _currentScale);

            // 偏移量根据父节点旋转修正，让投影看起来像打在水平面上
            Vector2 finalOffset = _parentRect.localRotation * _currentOffset;
            _shadowRect.anchoredPosition = finalOffset;

            // Z 轴在卡牌后方
            Vector3 pos = _shadowRect.localPosition;
            pos.z = 10f;
            _shadowRect.localPosition = pos;

            // 平滑过渡 alpha
            if (_shadowImage != null)
            {
                Color c = _shadowImage.color;
                c.a = Mathf.Lerp(c.a, _currentAlpha, Time.deltaTime * 10f);
                _shadowImage.color = c;
            }
        }
    }
}
