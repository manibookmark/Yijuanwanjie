using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;

namespace WanjieScroll.Bootstrap
{
    /// <summary>Wires map settlement callbacks to the domain source owner and strict save.
    /// Publication swaps the current floor's directory; rebuild cached map choices after commit.</summary>
    internal sealed class DungeonRunSideEventLifecycleCoordinator : MonoBehaviour
    {
        private DungeonRunSession _run;
        private MetaProgression _account;
        private GameSaveService _save;
        private Func<bool> _canPersist;
        private Action _reload;
        private Action<string> _reportFailure;
        private LevelMapEntryCoordinator _entry;
        private LevelVictoryFlowCoordinator _victory;
        private bool _reloading;

        public void Bind(DungeonRunSession run, MetaProgression account, GameSaveService save,
            Func<bool> canPersist, Action reload, Action<string> reportFailure,
            LevelMapEntryCoordinator entry, LevelVictoryFlowCoordinator victory)
        {
            _run = run; _account = account; _save = save; _canPersist = canPersist;
            _reload = reload; _reportFailure = reportFailure; _entry = entry; _victory = victory;
            if (_entry != null) _entry.InteractionCompleted += OnInteractionCompleted;
            if (_victory != null) _victory.VictoryPresentationCompleted += OnSettlementCompleted;
        }

        private void Start() => TryPrepareMapInput();
        private void OnSettlementCompleted() => TryPrepareMapInput();
        private void OnInteractionCompleted(IReadOnlyList<LevelInteractionReceipt> _) => TryPrepareMapInput();

        public bool TryPrepareMapInput()
        {
            if (_reloading) return false;
            if (_run == null || !ReferenceEquals(_run, DungeonRunSessionStore.Current)) return false;
            var result = DungeonRunSideEventActivation.TryActivateReady(_run, Persist, out string error);
            if (result == DungeonRunSideEventActivationResult.Activated)
            { _reloading = true; _reload(); return false; }
            if (result == DungeonRunSideEventActivationResult.Failed)
            { _reportFailure(error); return false; }
            return true;
        }

        private void Persist()
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy ||
                !ReferenceEquals(_run, DungeonRunSessionStore.Current) || _save == null || _account == null ||
                _canPersist == null || !_canPersist())
                throw new InvalidOperationException("Source publication requires the current map and a durable save.");
            _save.SaveTransactional(_account, _run.Serialize());
        }

        private void OnDestroy()
        {
            if (_entry != null) _entry.InteractionCompleted -= OnInteractionCompleted;
            if (_victory != null) _victory.VictoryPresentationCompleted -= OnSettlementCompleted;
        }
    }
}
