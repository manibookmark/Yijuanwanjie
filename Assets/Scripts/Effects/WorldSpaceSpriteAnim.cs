using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// World Space 精灵帧动画（SpriteRenderer 版）
    /// 在多帧之间循环切换，用于战斗场景中的角色/敌人
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class WorldSpaceSpriteAnim : MonoBehaviour
    {
        [Tooltip("动画帧序列")]
        public Sprite[] frames;

        [Tooltip("每帧停留秒数")]
        public float frameInterval = 0.5f;

        [Tooltip("动画时间偏移（错开多角色同步）")]
        public float timeOffset = 0f;

        [Tooltip("是否暂停动画")]
        public bool paused = false;

        private SpriteRenderer _renderer;
        private float _timer;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            if (paused || frames == null || frames.Length == 0 || _renderer == null)
                return;

            _timer += Time.deltaTime;
            float t = (_timer + timeOffset) / frameInterval;
            int idx = Mathf.FloorToInt(t) % frames.Length;
            _renderer.sprite = frames[idx];
        }

        /// <summary>
        /// 手动设置帧并重置计时
        /// </summary>
        public void SetFrames(Sprite[] newFrames, float interval = 0.5f)
        {
            frames = newFrames;
            frameInterval = interval;
            _timer = 0f;
            if (newFrames != null && newFrames.Length > 0 && _renderer != null)
                _renderer.sprite = newFrames[0];
        }
    }
}
