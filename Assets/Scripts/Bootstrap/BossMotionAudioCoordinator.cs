using UnityEngine;
using WanjieScroll.Audio;
using WanjieScroll.UI.Battle;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>Routes actor audio intent to its Audio owner. Area replays only
    /// valid current intent on subscription; this coordinator caches no playback.</summary>
    public sealed class BossMotionAudioCoordinator : MonoBehaviour
    {
        private EnemyAreaView _area;
        private BossMotionAudioPlayer _audio;
        private bool _subscribed;

        public void Bind(EnemyAreaView area, BossMotionAudioPlayer audio)
        {
            Unbind();
            _area = area;
            _audio = audio;
            Subscribe();
        }

        private void Subscribe()
        {
            if (_subscribed || !isActiveAndEnabled || _area == null || _audio == null) return;
            _subscribed = true;
            _area.BossAudioRequested += OnRequest;
        }

        private void OnRequest(BossAudioRequest request)
        {
            if (_audio == null) return;
            if (request.Kind == BossAudioRequestKind.Stop)
                _audio.Stop(request.OwnerKey, request.Generation);
            else
                _audio.Play(request.OwnerKey, request.Generation, request.Clip,
                    request.Loop, request.Volume, request.Pitch, request.StartOffsetSeconds);
        }

        private void Detach()
        {
            if (_subscribed && _area != null) _area.BossAudioRequested -= OnRequest;
            _subscribed = false;
            _audio?.StopAll();
        }

        public void Unbind()
        {
            Detach();
            _area = null;
            _audio = null;
        }
        private void OnEnable() => Subscribe();
        private void OnDisable() => Detach();
        private void OnDestroy() => Unbind();
    }
}
