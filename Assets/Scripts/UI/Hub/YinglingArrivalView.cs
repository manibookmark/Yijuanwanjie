using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.UI.Presentations;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// 独占群英殿角色的图集、帧时钟和等比尺寸。切换会立即替换旧演出；
    /// 名册与出战操作不依赖播放完成。打开名册可以直接显示实际末帧。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class YinglingArrivalView : MonoBehaviour
    {
        [SerializeField] private RawImage _actor;
        [SerializeField] private AspectRatioFitter _aspect;
        [SerializeField] private GameObject _empty;
        private CharacterPresentationProfile _profile;
        private float _elapsed;
        private bool _playing;

        private bool CanPlay => _profile != null && _profile.ResolvedFrameAtlas != null;

        public void Present(CharacterPresentationProfile profile, bool playArrival)
        {
            _profile = profile;
            _elapsed = 0f;
            _playing = playArrival && CanPlay;
            _actor.gameObject.SetActive(CanPlay);
            _empty.SetActive(!CanPlay);
            if (!CanPlay)
            {
                _actor.texture = null;
                return;
            }
            _actor.texture = _profile.ResolvedFrameAtlas;
            _aspect.aspectRatio = _profile.FrameAspect;
            ApplyFrame(playArrival ? 0 : _profile.ResolvedFrameCount - 1);
        }

        public void HoldLastFrame()
        {
            _playing = false;
            if (CanPlay)
                ApplyFrame(_profile.ResolvedFrameCount - 1);
        }

        private void Update()
        {
            if (!_playing || !CanPlay)
                return;
            _elapsed += Time.unscaledDeltaTime;
            int frame = Mathf.FloorToInt(_elapsed * _profile.ResolvedFramesPerSecond);
            ApplyFrame(frame);
            if (frame >= _profile.ResolvedFrameCount - 1)
                _playing = false;
        }

        private void OnDisable() => _playing = false;

        private void ApplyFrame(int frame) => _actor.uvRect = _profile.GetUvRect(frame);
    }
}
