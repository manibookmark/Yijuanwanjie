using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 真实战斗的 BOSS 动作特效窄入口。调用方只提交权威敌人 ID 和意图；
    /// 预设解析、目标选择、程序化视觉与镜头反馈均封装在 Effects 内。
    /// </summary>
    public sealed class BossActionEffectPlayer : MonoBehaviour
    {
        private readonly List<BossActionEffectPlayback> _playbacks =
            new List<BossActionEffectPlayback>();
        private readonly List<Transform> _enemyTargets =
            new List<Transform>();
        private Transform _effectRoot;
        private Transform _playerAnchor;
        private Camera _presentationCamera;

        public int PlayedCount { get; private set; }
        public string LastEffectId { get; private set; }

        public void Configure(
            Transform effectRoot,
            Camera presentationCamera,
            Transform playerAnchor)
        {
            _effectRoot = effectRoot != null ? effectRoot : transform;
            _presentationCamera = presentationCamera;
            _playerAnchor = playerAnchor;
        }

        public void RegisterEnemy(
            int enemyIndex,
            Transform target,
            Transform unusedEntranceMotionRoot = null)
        {
            if (enemyIndex < 0)
                return;
            while (_enemyTargets.Count <= enemyIndex)
                _enemyTargets.Add(null);
            _enemyTargets[enemyIndex] = target;
        }

        public bool TryPlay(
            string enemyContentId,
            EnemyIntent intent,
            int enemyIndex)
        {
            return TryPlayScoped(enemyContentId, intent, enemyIndex, out _);
        }

        /// <summary>Returns only the caller's playback cancellation lease.</summary>
        public bool TryPlayScoped(
            string enemyContentId,
            EnemyIntent intent,
            int enemyIndex,
            out IDisposable playback)
        {
            playback = null;
            if (!isActiveAndEnabled)
                return false;
            if (!BossActionEffectCatalog.TryResolve(
                    enemyContentId,
                    intent,
                    out BossActionEffectPreset preset))
            {
                return false;
            }

            return TryPlayPresetScoped(preset, enemyIndex, null, out playback);
        }

        /// <summary>
        /// Plays a caller-selected contact cue at a world anchor without changing
        /// catalog fallback bindings, the player anchor or combat settlement.
        /// </summary>
        public bool TryPlayPresetAtWorldAnchorScoped(
            BossActionEffectPreset preset,
            int enemyIndex,
            Vector3 impactWorld,
            out IDisposable playback)
        {
            return TryPlayPresetScoped(preset, enemyIndex, impactWorld, out playback);
        }

        private bool TryPlayPresetScoped(
            BossActionEffectPreset preset,
            int enemyIndex,
            Vector3? impactWorld,
            out IDisposable playback)
        {
            playback = null;
            if (!isActiveAndEnabled || preset == null || !preset.TryValidate(out _))
                return false;
            if (impactWorld.HasValue &&
                (!Finite(impactWorld.Value.x) || !Finite(impactWorld.Value.y) ||
                 !Finite(impactWorld.Value.z)))
                return false;

            Transform enemyTarget = GetEnemyTarget(enemyIndex);
            Transform planeAnchor = preset.usage == BossActionEffectUsage.Defend
                ? enemyTarget
                : _playerAnchor;
            if (_effectRoot == null || _presentationCamera == null ||
                (!impactWorld.HasValue && planeAnchor == null) ||
                !_effectRoot.gameObject.activeInHierarchy)
            {
                return false;
            }

            Vector3 planeWorld = impactWorld ?? planeAnchor.position;
            Vector3? projectedImpactAnchor = impactWorld.HasValue
                ? _presentationCamera.WorldToViewportPoint(impactWorld.Value)
                : (Vector3?)null;

            Renderer sourceRenderer = enemyTarget != null
                ? enemyTarget.GetComponentInChildren<Renderer>()
                : null;
            Rect sourceViewportRect;
            if (!BossActionEffectLayout.TryGetRendererViewportRect(
                    _presentationCamera,
                    sourceRenderer,
                    out sourceViewportRect))
            {
                sourceViewportRect =
                    BossActionEffectLayout.BuildFallbackSourceRect(
                        _presentationCamera,
                        enemyTarget != null
                            ? enemyTarget.position
                            : planeWorld);
            }

            if (projectedImpactAnchor.HasValue &&
                !BossActionEffectLayout.TryResolveAtViewportAnchor(
                    preset.visualKind, sourceViewportRect,
                    projectedImpactAnchor.Value, out _))
                return false;

            _playbacks.RemoveAll(value => value == null);
            var scopeObject = new GameObject("BossActionEffectPlayback");
            scopeObject.transform.SetParent(_effectRoot, false);
            var owner = scopeObject.AddComponent<BossActionEffectPlayback>();
            try
            {
                GameObject effect = BossActionEffectInstance.Play(
                    scopeObject.transform,
                    _presentationCamera,
                    planeWorld.z,
                    sourceViewportRect,
                    preset,
                    projectedImpactAnchor);
                if (effect == null)
                {
                    owner.Dispose();
                    return false;
                }
                ScreenShake shake =
                    _presentationCamera.GetComponent<ScreenShake>();
                IDisposable motion = shake != null
                    ? shake.PlayScreenMotionScoped(
                        preset.screenMotion,
                        preset.screenMotionDuration,
                        preset.screenMotionAmplitude)
                    : null;
                owner.Arm(motion);
                _playbacks.Add(owner);
                playback = owner;
            }
            catch
            {
                owner.Dispose();
                throw;
            }

            PlayedCount++;
            LastEffectId = preset.effectId;
            return true;
        }

        private void OnDisable() => CancelOwnedPlaybacks();
        private void OnDestroy() => CancelOwnedPlaybacks();

        private void CancelOwnedPlaybacks()
        {
            foreach (BossActionEffectPlayback playback in _playbacks)
                if (playback != null) playback.Dispose();
            _playbacks.Clear();
        }

        private Transform GetEnemyTarget(int enemyIndex)
        {
            return enemyIndex >= 0 && enemyIndex < _enemyTargets.Count
                ? _enemyTargets[enemyIndex]
                : null;
        }

        private static bool Finite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
