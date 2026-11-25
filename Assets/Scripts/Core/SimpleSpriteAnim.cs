using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 简单精灵帧动画：在多帧之间循环切换
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class SimpleSpriteAnim : MonoBehaviour
    {
        [Tooltip("动画帧序列")]
        public Sprite[] frames;

        [Tooltip("每帧停留秒数")]
        public float frameInterval = 0.5f;

        [Tooltip("动画时间偏移（错开多角色同步）")]
        public float timeOffset = 0f;

        private Image _image;
        private float _timer;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void Update()
        {
            if (frames == null || frames.Length == 0 || _image == null)
                return;

            _timer += Time.deltaTime;
            float t = (_timer + timeOffset) / frameInterval;
            int idx = Mathf.FloorToInt(t) % frames.Length;
            _image.sprite = frames[idx];
        }
    }
}
