using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Effects;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Assembles one artifact manifestation with its authoritative HUD receipt.
    /// UI alone owns numeric holds and glyph flights; Effects alone owns the
    /// world shape. Captured callbacks also provide bounded cancellation fallback.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupportArtifactFeedbackCoordinator : MonoBehaviour
    {
        private CombatController _combat;
        private VisualEffectManager _effects;
        private BattleHudView _hud;
        private CrawlerResidentPresenter _residents;
        private Camera _worldCamera;
        private readonly Dictionary<int, Vector3> _castOrigins = new Dictionary<int, Vector3>();
        private readonly List<Action> _pendingTransfers = new List<Action>();

        public void PrepareCardOrigin(int cardRuntimeId, Vector3 worldPosition)
        {
            if (cardRuntimeId > 0) _castOrigins[cardRuntimeId] = worldPosition;
        }
        public Vector3? FindCardOrigin(int cardRuntimeId)
            => _castOrigins.TryGetValue(cardRuntimeId, out Vector3 origin) ? origin : (Vector3?)null;
        public void ForgetCardOrigin(int cardRuntimeId) => _castOrigins.Remove(cardRuntimeId);

        public void Bind(CombatController combat, VisualEffectManager effects,
            BattleHudView hud, Camera worldCamera, CrawlerResidentPresenter residents)
        {
            Unbind();
            _combat = combat;
            _effects = effects;
            _hud = hud;
            _residents = residents;
            _worldCamera = worldCamera;
            if (_combat != null)
            {
                _combat.OnCardPlayResolved += OnResolved;
            }
        }

        public void Unbind()
        {
            if (_combat != null)
            {
                _combat.OnCardPlayResolved -= OnResolved;
            }
            StopAllCoroutines();
            // Release every accepted hold, even if Effects was disabled before
            // its midpoint. Each callback is single-use and lifetime guarded.
            foreach (Action release in _pendingTransfers.ToArray())
            {
                try { release(); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
            _pendingTransfers.Clear();
            _castOrigins.Clear();
            _combat = null;
            _effects = null;
            _hud = null;
            _residents = null;
            _worldCamera = null;
        }

        public static IReadOnlyList<CombatStatChange> GetStandardHudGains(CombatCardResolvedEvent result)
        {
            var gains = new List<CombatStatChange>();
            if (result?.StatChanges == null) return gains;
            StatType? artifactStat = ArtifactStat(FindSupportProfile(result));
            foreach (CombatStatChange change in result.StatChanges)
                if (change.StatType != StatType.NingShen && change.StatType != artifactStat)
                    gains.Add(change);
            return gains;
        }

        public static int GetStandardManaGain(CombatCardResolvedEvent result)
        {
            CardSelfEffectStyle style = FindSupportProfile(result)?.selfEffectStyle ?? CardSelfEffectStyle.None;
            return style == CardSelfEffectStyle.ManaBook || style == CardSelfEffectStyle.SandSpirit
                ? 0 : result?.ManaGained ?? 0;
        }

        private static StatType? ArtifactStat(VfxProfile profile)
        {
            switch (profile?.selfEffectStyle)
            {
                case CardSelfEffectStyle.QuantityMechanism: return StatType.Amount;
                case CardSelfEffectStyle.DealScript: return StatType.ShouPai;
                case CardSelfEffectStyle.WindOrb: return StatType.NingShen;
                case CardSelfEffectStyle.TianjiMirror: return StatType.TianYun;
                default: return null;
            }
        }

        private static VfxProfile FindSupportProfile(CombatCardResolvedEvent result)
        {
            if (result?.Card == null) return null;
            VfxProfile profile = VfxProfileRegistry.Get(result.Card);
            switch (profile?.selfEffectStyle)
            {
                case CardSelfEffectStyle.QuantityMechanism:
                case CardSelfEffectStyle.WindOrb:
                case CardSelfEffectStyle.ManaBook:
                case CardSelfEffectStyle.DealScript:
                case CardSelfEffectStyle.SandSpirit:
                case CardSelfEffectStyle.TianjiMirror:
                case CardSelfEffectStyle.MuddleSigil: return profile;
                default: return null;
            }
        }

        private void OnResolved(CombatCardResolvedEvent result)
        {
            if (result?.Card == null) return;
            int id = result.Card.runtimeInstanceId;
            Vector3 origin = FindCardOrigin(id) ??
                (_effects?.playerAttackOrigin != null ? _effects.playerAttackOrigin.position : Vector3.zero);
            ForgetCardOrigin(id);
            VfxProfile profile = FindSupportProfile(result);
            StatType? artifactStat = ArtifactStat(profile);
            var gains = new List<CombatStatChange>();
            if (result.StatChanges != null)
                foreach (CombatStatChange change in result.StatChanges)
                    if (change.StatType != StatType.NingShen && change.StatType == artifactStat)
                        gains.Add(change);
            // Duration has two distinct recipients. Only the unused bonus is a
            // global buff; a summon base or per-resident gain is never that stat.
            if (result.DurationReserveGained > 0)
                gains.Add(new CombatStatChange(StatType.NingShen, result.DurationReserveGained,
                    result.ResultingDurationReserve));

            Action<Vector2> statArrival = _hud?.PrepareStatGains(gains);
            Action<Vector2> durationArrival = _residents?.PrepareDurationGains(result.CrawlerDurationChanges);
            Action<Vector2> manaArrival = GetStandardManaGain(result) == 0 && result.ManaGained > 0
                ? _hud?.PrepareManaGain(result.ManaGained) : null;
            Vector2 fallback = ScreenPosition(origin);
            bool completed = false;
            Action<Vector3> transfer = world =>
            {
                if (completed) return;
                completed = true;
                Vector2 screen = _worldCamera != null ? ScreenPosition(world) : fallback;
                // Each UI owner guards its own lifetime. A missing HUD or a
                // failing independent transfer must not strand resident holds.
                try { statArrival?.Invoke(screen); }
                finally
                {
                    try { manaArrival?.Invoke(screen); }
                    finally { durationArrival?.Invoke(screen); }
                }
            };
            Action release = () => transfer(origin);
            if (profile == null || !isActiveAndEnabled)
            { release(); return; }
            _pendingTransfers.Add(release);
            StartCoroutine(Present(result, profile, origin, transfer, release, () => completed));
        }

        private IEnumerator Present(CombatCardResolvedEvent result, VfxProfile profile, Vector3 origin,
            Action<Vector3> transfer, Action release, Func<bool> completed)
        {
            try
            {
                yield return null;
                try
                {
                    if (_effects == null || !_effects.PlaySupportArtifact(origin, profile, result.ManaGained, transfer))
                        release();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    release();
                }
                float elapsed = 0f;
                float timeout = Mathf.Max(0.5f, profile.selfEffectDuration) + 0.5f;
                while (!completed() && elapsed < timeout)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            finally
            {
                try { if (!completed()) release(); }
                finally { _pendingTransfers.Remove(release); }
            }
        }

        private Vector2 ScreenPosition(Vector3 world)
        {
            if (_worldCamera != null)
            {
                Vector3 point = _worldCamera.WorldToScreenPoint(world);
                if (point.z > 0f) return new Vector2(point.x, point.y);
            }
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.2f);
        }
        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();
    }
}
