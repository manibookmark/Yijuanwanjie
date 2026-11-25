using UnityEngine;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns the tunable Stage 1 Tianlu facility presentation. This is an
    /// asset rather than scene state so Inspector changes made during Play
    /// mode remain available after stopping the scene.
    /// </summary>
    [CreateAssetMenu(
        fileName = "hub-tianlu-presentation",
        menuName = "Wanjie Scroll/Hub/Tianlu Presentation")]
    public sealed class HubTianluPresentationProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _contentId =
            "presentation.hub.tianlu.stage01.v1";

        [Header("天箓摆位（1920×1080 母图坐标）")]
        [Tooltip("天箓中心，坐标原点位于母图左上角。")]
        [SerializeField] private Vector2 _centre = new Vector2(576f, 790f);
        [Tooltip("显示宽度（像素）；高度按序列帧宽高比自动计算，不会拉伸变形。")]
        [SerializeField, Min(60f)] private float _width = 500f;
        [Tooltip("绕中心的欧拉角旋转：X 前后倾角、Y 左右偏转、Z 平面内顺时针角度。")]
        [SerializeField] private Vector3 _rotationEuler = Vector3.zero;

        [Header("Hover 动画")]
        [Tooltip("指针进入后从首帧推进到尾帧的时长（秒）。")]
        [SerializeField, Range(0.5f, 8f)] private float _hoverPlayDuration = 4f;
        [Tooltip("指针移出后从当前帧回退到首帧的时长（秒）。")]
        [SerializeField, Range(0.2f, 4f)] private float _hoverRewindDuration = 1.5f;

        public string ContentId => _contentId;
        public Vector2 Centre => _centre;
        public float Width => _width;
        public Vector3 RotationEuler => _rotationEuler;
        public float HoverPlayDuration => _hoverPlayDuration;
        public float HoverRewindDuration => _hoverRewindDuration;

#if UNITY_EDITOR
        private void OnValidate()
        {
            _width = Mathf.Max(60f, _width);
            _hoverPlayDuration = Mathf.Clamp(_hoverPlayDuration, 0.5f, 8f);
            _hoverRewindDuration = Mathf.Clamp(_hoverRewindDuration, 0.2f, 4f);
        }
#endif
    }
}
