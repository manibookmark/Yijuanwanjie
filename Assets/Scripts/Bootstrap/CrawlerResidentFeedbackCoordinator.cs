using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Audio;
using WanjieScroll.Core;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>Assembles resident aura/voice with typed HUD gains. Resident
    /// feedback emits no extra labels. Unused Duration uses the buff icon;
    /// applied Duration uses each recipient's own remaining count.</summary>
    [DisallowMultipleComponent]
    public sealed class CrawlerResidentFeedbackCoordinator : MonoBehaviour
    {
        private CombatController _combat;
        private CrawlerResidentPresenter _residentPresenter;
        private BattleHudView _battleHud;
        public void Bind(CombatController combat, CrawlerResidentPresenter residents, BattleHudView hud)
        {
            Unbind(); _combat = combat; _residentPresenter = residents; _battleHud = hud;
            if (_combat != null)
            {
                _combat.OnCrawlerTriggered += OnCrawlerTriggered;
                _combat.OnCardPlayResolved += OnCardPlayResolved;
            }
            if (_residentPresenter != null) _residentPresenter.SummonVoiceRequested += OnSummonVoiceRequested;
        }
        public void ShowCardPreview(CardConfig card)
        {
            _residentPresenter?.ClearTriggerPreviews();
            if (_combat == null || _residentPresenter == null || card == null) return;
            foreach (CrawlerTriggerPreview preview in _combat.GetCrawlerTriggerPreviews(card))
                _residentPresenter.ShowTriggerPreview(preview.CrawlerRuntimeId, null);
            foreach (KeyValuePair<int, int> preview in _combat.GetCrawlerDurationPreview(card))
                _residentPresenter.ShowDurationPreview(preview.Key, preview.Value);
        }
        public void ClearCardPreview() => _residentPresenter?.ClearTriggerPreviews();
        public void Unbind()
        {
            if (_combat != null)
            {
                _combat.OnCrawlerTriggered -= OnCrawlerTriggered;
                _combat.OnCardPlayResolved -= OnCardPlayResolved;
            }
            if (_residentPresenter != null)
            {
                _residentPresenter.SummonVoiceRequested -= OnSummonVoiceRequested;
                _residentPresenter.ClearTriggerPreviews();
            }
            _combat = null; _residentPresenter = null; _battleHud = null;
        }
        // The effect already committed in Core. Refresh the current effective
        // conditions immediately; shared artifact animation never gates rules.
        private void OnCardPlayResolved(CombatCardResolvedEvent result)
        {
            if (result?.RandomizedCrawlerTriggerColors == true && _residentPresenter != null)
                _residentPresenter.ShowTriggerColorChanges(result.CrawlerTriggerColorChanges);
        }
        private void OnCrawlerTriggered(CombatCrawlerTriggeredEvent result)
        {
            if (result == null) return;
            RectTransform source = null;
            string voice = null;
            _residentPresenter?.TryPlayTriggerFeedback(result.CrawlerRuntimeId, null, out source, out voice);
            var changes = new List<CombatStatChange>();
            if (result.StatChanges != null)
                foreach (CombatStatChange change in result.StatChanges)
                    if (change.StatType != StatType.NingShen) changes.Add(change);
            if (result.DurationReserveGained > 0)
                changes.Add(new CombatStatChange(StatType.NingShen, result.DurationReserveGained,
                    result.ResultingDurationReserve));
            _battleHud?.PlayStatGains(changes, source);
            var durationArrival = _residentPresenter?.PrepareDurationGains(result.CrawlerDurationChanges);
            Vector2 origin = new Vector2(Screen.width * 0.5f, Screen.height * 0.2f);
            if (source != null)
            {
                Canvas canvas = source.GetComponentInParent<Canvas>();
                Camera camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null : canvas.worldCamera;
                origin = RectTransformUtility.WorldToScreenPoint(camera, source.position);
            }
            durationArrival?.Invoke(origin);
            if (!string.IsNullOrWhiteSpace(voice)) AudioManager.PlayVoiceCueSafe(voice);
        }
        private static void OnSummonVoiceRequested(string voice)
        { if (!string.IsNullOrWhiteSpace(voice)) AudioManager.PlayVoiceCueSafe(voice); }
        private void OnDestroy() => Unbind();
    }
}
