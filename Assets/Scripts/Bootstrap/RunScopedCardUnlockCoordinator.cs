using System;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.Progression;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Assembles successful production card-play/resume boundaries with the
    /// Progression transaction and existing Run checkpoint saver. Eligibility,
    /// counters and rollback remain owned by Progression/Run, never this adapter.
    /// F7 creates no instance; QuickPlay additionally cannot persist.
    /// </summary>
    public sealed class RunScopedCardUnlockCoordinator : MonoBehaviour
    {
        private CombatController _combat;
        private MetaProgression _account;
        private CardUnlockRequirements _requirements;

        public void Bind(CombatController combat, MetaProgression account,
            CardUnlockRequirements requirements)
        {
            Unbind();
            _combat = combat;
            _account = account;
            _requirements = requirements;
            if (_combat != null) _combat.OnStateChanged += OnStateChanged;
            RefreshEligibility();
        }

        private void OnStateChanged(CombatStateChangeReason change)
        {
            if (change == CombatStateChangeReason.CardPlayed) RefreshEligibility();
        }

        private void RefreshEligibility()
        {
            if (!EditorLevelMapQuickPlayRuntime.CanPersist || _account == null ||
                _requirements == null || _combat == null) return;
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run == null || !ReferenceEquals(run.RunProgression, _combat.RunProgression)) return;
            var acceptance = new CardUnlockAcceptance(run.RunId, run.CombatProgression.CurrentLevel,
                run.RunProgression.CardUnlockFacts.Capture(), run.LeaderCrawlerContentId);
            if (!CardUnlockLevelTransaction.HasPendingRunScopedUnlock(_account, acceptance, _requirements)) return;
            if (!CardUnlockLevelTransaction.TryRefreshRunScopedEligibility(_account, acceptance, _requirements,
                () =>
                {
                    if (!DungeonRunSessionStore.TryGetCombatPersistencePayload(out string payload))
                        throw new InvalidOperationException("单局解锁缺少正式战斗保存点。");
                    GameSaveService.CreateDefault().SaveTransactional(_account, payload);
                }, out _, out string error))
                Debug.LogError("[CardUnlock] " + error);
        }

        private void Unbind()
        {
            if (_combat != null) _combat.OnStateChanged -= OnStateChanged;
            _combat = null;
            _account = null;
            _requirements = null;
        }

        private void OnDestroy() => Unbind();
    }
}
