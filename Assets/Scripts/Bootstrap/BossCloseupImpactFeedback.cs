using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Audio;
using WanjieScroll.Core;
using WanjieScroll.Effects;
using WanjieScroll.UI.Battle;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns close-up contact feedback for this encounter. Read-only actor signals
    /// choose the existing effect channel; the real rule callback is never called
    /// here. Each action generation owns its scoped visual/audio cancellation;
    /// the audio player owns sources and applies the existing SFX mix.
    /// </summary>
    public sealed class BossCloseupImpactFeedback : MonoBehaviour
    {
        public const string PresetResourcePath = "BossActionEffects/Presets/g4-spear-tip-screen-crack";
        public const string ContactAudioResourcePath = "Audio/BossCloseups/g4-spear-screen-crack";
        private const float ContactAudioTrim = 0.7f;

        private sealed class ActionCue
        {
            public long Generation;
            public bool ImpactDelivered;
            public IDisposable Playback;
            public IDisposable AudioPlayback;
        }

        /// <summary>Reserves the next generation for this playback's stop.
        /// A late lease cannot stop the following even-numbered Play request.</summary>
        private sealed class ContactAudioLease : IDisposable
        {
            private BossMotionAudioPlayer _player;
            private readonly long _owner;
            private readonly long _stopGeneration;

            public ContactAudioLease(BossMotionAudioPlayer player, long owner, long stopGeneration)
            {
                _player = player;
                _owner = owner;
                _stopGeneration = stopGeneration;
            }

            public void Dispose()
            {
                BossMotionAudioPlayer player = _player;
                _player = null;
                if (player != null) player.Stop(_owner, _stopGeneration);
            }
        }

        private readonly Dictionary<int, ActionCue> _actions = new Dictionary<int, ActionCue>();
        private EnemyAreaView _area;
        private CombatController _combat;
        private BossActionEffectPlayer _effects;
        private BossAttackCloseupProfile _profile;
        private BossActionEffectPreset _preset;
        private BossMotionAudioPlayer _contactAudio;
        private AudioClip _contactAudioClip;
        private long _contactAudioOwner;
        private long _contactAudioGeneration;
        private bool _subscribed;

        public bool HasContactAudio => _contactAudio != null && _contactAudioClip != null;
        public bool IsConfigured => _area != null && _combat != null && _effects != null &&
            _profile != null && _preset != null && _subscribed && HasContactAudio;

        public void Bind(EnemyAreaView area, CombatController combat,
            BossActionEffectPlayer effects, BossAttackCloseupProfile profile)
        {
            UnsubscribeAndCancel();
            _area = area;
            _combat = combat;
            _effects = effects;
            _profile = profile;
            _preset = Resources.Load<BossActionEffectPreset>(PresetResourcePath);
            if (_preset == null || !_preset.TryValidate(out string error))
                throw new InvalidOperationException("Close-up contact effect preset is missing or invalid.");
            Subscribe();
        }

        public void ConfigureContactAudio(BossMotionAudioPlayer player, AudioClip clip)
        {
            // Replacing only this dependency must not withdraw a live crack.
            foreach (ActionCue cue in new List<ActionCue>(_actions.Values))
            {
                IDisposable playback = cue.AudioPlayback;
                cue.AudioPlayback = null;
                try { playback?.Dispose(); }
                catch (Exception error) { Debug.LogException(error); }
            }
            _contactAudio = player;
            _contactAudioClip = clip;
            if (_contactAudioOwner == 0) _contactAudioOwner = BossAudioOwnerIdentity.Allocate();
        }

        public void Unbind()
        {
            UnsubscribeAndCancel();
            _area = null;
            _combat = null;
            _effects = null;
            _profile = null;
            _preset = null;
            _contactAudio = null;
            _contactAudioClip = null;
        }

        private void Subscribe()
        {
            if (_subscribed || !isActiveAndEnabled || _area == null) return;
            _area.ActionPresentationSignaled += OnSignal;
            _subscribed = true;
        }

        private void OnSignal(EnemyActionPresentationSignal signal)
        {
            if (!isActiveAndEnabled || _profile == null) return;
            int index = signal.EnemyIndex;
            if (signal.Kind == EnemyActionPresentationSignalKind.Started)
            {
                Retire(index);
                if (MatchesSource(signal))
                    _actions.Add(index, new ActionCue { Generation = signal.Generation });
                return;
            }
            if (!_actions.TryGetValue(index, out ActionCue cue) || cue.Generation != signal.Generation) return;
            if (signal.Kind == EnemyActionPresentationSignalKind.Completed ||
                signal.Kind == EnemyActionPresentationSignalKind.Cancelled)
            {
                Retire(index);
                return;
            }
            if (signal.Kind != EnemyActionPresentationSignalKind.Impact || cue.ImpactDelivered) return;
            cue.ImpactDelivered = true;
            if (!signal.HasSourceAnchor || !MatchesSource(signal) || _effects == null ||
                !_area.IsBossAttackCloseupActive(index)) return;
            if (_effects.TryPlayPresetAtWorldAnchorScoped(_preset, index,
                    signal.SourceAnchorWorld, out IDisposable playback))
            {
                // A presentation callback may retire/rebind this encounter.
                if (IsCurrentCue(index, cue))
                {
                    cue.Playback = playback;
                    PlayContactAudio(index, cue);
                }
                else playback?.Dispose();
            }
        }

        private bool IsCurrentCue(int index, ActionCue cue)
            => isActiveAndEnabled && _subscribed &&
               _actions.TryGetValue(index, out ActionCue current) && ReferenceEquals(current, cue);

        private void PlayContactAudio(int index, ActionCue cue)
        {
            if (!HasContactAudio || !IsCurrentCue(index, cue)) return;
            BossMotionAudioPlayer player = _contactAudio;
            AudioClip clip = _contactAudioClip;
            long playGeneration = checked(_contactAudioGeneration + 2);
            _contactAudioGeneration = playGeneration;
            var lease = new ContactAudioLease(player, _contactAudioOwner, checked(playGeneration + 1));
            try
            {
                player.Play(_contactAudioOwner, playGeneration, clip,
                    false, ContactAudioTrim, 1f);
                if (IsCurrentCue(index, cue) && player == _contactAudio && clip == _contactAudioClip)
                    cue.AudioPlayback = lease;
                else lease.Dispose();
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        private bool MatchesSource(EnemyActionPresentationSignal signal)
        {
            return _profile != null && _combat != null && signal.EnemyIndex >= 0 &&
                signal.EnemyIndex < _combat.Enemies.Count &&
                string.Equals(signal.ActionId, _profile.actionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_combat.Enemies[signal.EnemyIndex]?.contentId,
                    _profile.contentId, StringComparison.Ordinal);
        }

        private void Retire(int index)
        {
            if (!_actions.TryGetValue(index, out ActionCue cue)) return;
            _actions.Remove(index);
            ReleaseCue(cue);
        }

        private static void ReleaseCue(ActionCue cue)
        {
            IDisposable playback = cue.Playback;
            IDisposable audio = cue.AudioPlayback;
            cue.Playback = null;
            cue.AudioPlayback = null;
            try { playback?.Dispose(); }
            finally { audio?.Dispose(); }
        }

        private void UnsubscribeAndCancel()
        {
            if (_subscribed && _area != null) _area.ActionPresentationSignaled -= OnSignal;
            _subscribed = false;
            var old = new List<ActionCue>(_actions.Values);
            _actions.Clear();
            foreach (ActionCue cue in old)
            {
                try { ReleaseCue(cue); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => UnsubscribeAndCancel();
        private void OnDestroy() => Unbind();
    }
}
