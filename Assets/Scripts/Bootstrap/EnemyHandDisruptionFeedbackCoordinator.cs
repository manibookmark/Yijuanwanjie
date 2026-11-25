using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Effects;
using WanjieScroll.UI.Battle;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>Joins actual grant receipts to the current actor release. Core
    /// owns grants; this owner captures immutable anchors/count and pairs once.
    /// Cancel/rebind/disable revoke pending pairs and the owned paper stream.</summary>
    public sealed class EnemyHandDisruptionFeedbackCoordinator : MonoBehaviour
    {
        private const string ConfuseId = "card.xiyou.confuse";
        private CombatController _combat;
        private HandView _hand;
        private DeckPileView _piles;
        private EnemyAreaView _enemies;
        private Camera _camera;
        private EnemyCardInjectionEffectPlayer _injection;
        private readonly EnemySpellReleaseLatch _release = new EnemySpellReleaseLatch();
        private Vector3 _source, _target;
        private int _actualCount;
        private bool _subscribed;

        public bool IsPresenting => _injection != null && _injection.IsPlaying;

        public void Bind(CombatController combat, HandView hand, DeckPileView piles,
            EnemyAreaView enemies, Camera camera, EnemyCardInjectionEffectPlayer injection)
        {
            Unbind();
            _combat = combat; _hand = hand; _piles = piles;
            _enemies = enemies; _camera = camera; _injection = injection;
            Subscribe();
        }

        private void Subscribe()
        {
            if (_subscribed || !isActiveAndEnabled || _combat == null) return;
            _subscribed = true;
            _combat.OnEnemyActionResolved += OnEnemyAction;
            _combat.OnCardPlayResolved += OnCardResolved;
            if (_enemies != null) _enemies.ActionPresentationSignaled += OnPresentation;
        }

        private void OnPresentation(EnemyActionPresentationSignal signal)
        {
            switch (signal.Kind)
            {
                case EnemyActionPresentationSignalKind.Started:
                    _release.Clear();
                    _actualCount = 0;
                    if (signal.WillEmitSpellRelease)
                        _release.Begin(signal.Generation, signal.EnemyIndex, signal.ActionId);
                    break;
                case EnemyActionPresentationSignalKind.SpellRelease:
                    if (signal.HasSourceAnchor &&
                        _release.Release(signal.Generation, signal.EnemyIndex, signal.ActionId))
                    {
                        _source = signal.SourceAnchorWorld;
                        TryRelease();
                    }
                    break;
                case EnemyActionPresentationSignalKind.Cancelled:
                    if (_release.Matches(signal.Generation, signal.EnemyIndex, signal.ActionId))
                    {
                        _release.Clear();
                        _actualCount = 0;
                        _injection?.Cancel();
                    }
                    break;
                case EnemyActionPresentationSignalKind.Completed:
                    // Flight, if already delivered, keeps its own lifetime. A late
                    // stale marker cannot manufacture it after completion.
                    if (_release.Matches(signal.Generation, signal.EnemyIndex, signal.ActionId))
                    {
                        _release.Clear();
                        _actualCount = 0;
                    }
                    break;
            }
        }

        private void OnEnemyAction(CombatEnemyActionResult result)
        {
            if (_injection == null || _camera == null || _piles == null ||
                _enemies == null || !result.TemporaryCardGrant.HasValue) return;
            EnemyTemporaryCardGrantResult grant = result.TemporaryCardGrant.Value;
            if (grant.ActualCount <= 0 || grant.CanceledByEncounterEnd) return;
            EnemyPresentationSnapshot source = _enemies.GetEnemyPresentation(result.EnemyIndex);
            if (!source.IsVisible) return;
            int count = 0;
            Vector3 target = default;
            foreach (CardConfig card in grant.Cards)
            {
                if (card == null || card.contentId != ConfuseId ||
                    !_piles.TryGetCardReturnTarget(card.runtimeInstanceId, out var destination)) continue;
                float depth = _camera.WorldToScreenPoint(source.WorldBounds.center).z;
                target = _camera.ScreenToWorldPoint(new Vector3(
                    destination.ScreenPoint.x, destination.ScreenPoint.y, depth));
                count++;
            }
            if (count <= 0) return;
            if (_release.MatchesReceipt(result.EnemyIndex, result.Intent.ActionId))
            {
                if (!_release.Grant(result.EnemyIndex, result.Intent.ActionId, count)) return;
                _target = target;
                _actualCount = count;
                TryRelease();
            }
            else
            {
                // Ordinary enemies and legacy actions have no release marker.
                _injection.Play(source.WorldBounds.center, target, count);
            }
        }

        private void TryRelease()
        {
            if (_release.TryDeliver())
                _injection?.PlayReleased(_source, _target, _actualCount);
        }

        private void OnCardResolved(CombatCardResolvedEvent result)
        {
            if (result?.Card?.contentId == ConfuseId && result.HandCostChanges.Count > 0)
                _hand?.PresentHandCostChanges(result.HandCostChanges);
        }

        private void Detach()
        {
            if (_subscribed && _combat != null)
            {
                _combat.OnEnemyActionResolved -= OnEnemyAction;
                _combat.OnCardPlayResolved -= OnCardResolved;
            }
            if (_subscribed && _enemies != null)
                _enemies.ActionPresentationSignaled -= OnPresentation;
            _subscribed = false;
            _release.Clear();
            _actualCount = 0;
            _injection?.Cancel();
            _hand?.ClearHandCostFeedback();
        }

        public void Unbind()
        {
            Detach();
            _combat = null; _hand = null; _piles = null;
            _enemies = null; _camera = null; _injection = null;
        }
        private void OnEnable() => Subscribe();
        private void OnDisable() => Detach();
        private void OnDestroy() => Unbind();
    }
}
