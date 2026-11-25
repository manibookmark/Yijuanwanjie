using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// 天箓序列帧的 Hover 驱动：默认停在首帧，指针进入后正向推进到尾帧，
    /// 移出后按回退时长退回首帧。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class HubTianluHoverAnim :
        MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        [Tooltip("动画帧序列")]
        public Sprite[] frames;

        [Tooltip("指针进入后从首帧推进到尾帧的时长（秒）")]
        public float playDuration = 4f;

        [Tooltip("指针移出后从当前帧回退到首帧的时长（秒）")]
        public float rewindDuration = 1.5f;

        private Image _image;
        private float _progress;
        private bool _hovering;
        private bool _awakening;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void Update()
        {
            if (_awakening)
                return;
            if (frames == null || frames.Length < 2 || _image == null)
                return;

            float target = _hovering ? 1f : 0f;
            float duration = _hovering ? playDuration : rewindDuration;
            _progress = Mathf.MoveTowards(
                _progress,
                target,
                Time.deltaTime / Mathf.Max(0.01f, duration));

            _image.sprite = frames[
                Mathf.RoundToInt(_progress * (frames.Length - 1))];
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            _hovering = true;
        }

        public void OnPointerExit(PointerEventData eventData) =>
            _hovering = false;

        /// <summary>Temporarily gives the Hub arrival timeline frame control.</summary>
        public void BeginAwakening()
        {
            _awakening = true;
            _hovering = false;
            SetAwakeningProgress(0f);
        }

        /// <summary>Sets the externally driven glow frame during materialization.</summary>
        public void SetAwakeningProgress(float normalizedProgress)
        {
            if (!_awakening || frames == null || frames.Length == 0 ||
                _image == null)
                return;

            float progress = Mathf.Clamp01(normalizedProgress);
            _image.sprite = frames[Mathf.RoundToInt(
                progress * (frames.Length - 1))];
        }

        /// <summary>Returns the facility to its normal first-frame hover state.</summary>
        public void EndAwakening()
        {
            _awakening = false;
            _hovering = false;
            _progress = 0f;
            if (_image != null && frames != null && frames.Length > 0)
                _image.sprite = frames[0];
        }
    }
}
