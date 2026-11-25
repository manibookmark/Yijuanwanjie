using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using Object = UnityEngine.Object;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns only the transient definitions and physical cards created for one
    /// battle. The caller transfers freshly created factory results; persistent
    /// Editor assets remain borrowed even if present in a transferred graph.
    /// Tracking played and copied cards preserves ownership after consumed cards
    /// leave all piles. Disposal detaches Core events before releasing objects;
    /// it never modifies Core, Run data, presentation or shared asset caches.
    /// </summary>
    public sealed class CombatEncounterResourceLease : IDisposable
    {
        private readonly HashSet<Object> _owned = new HashSet<Object>();
        private CombatController _combat;
        private bool _disposed;

        public void Own(Object item)
        {
            ThrowIfDisposed();
            Track(item);
        }

        public void OwnCards(IReadOnlyList<CardConfig> cards)
        {
            ThrowIfDisposed();
            TrackCards(cards);
        }

        public void OwnEnemyDefinitions(IEnumerable<EnemyStats> enemies)
        {
            ThrowIfDisposed();
            if (enemies == null)
                return;
            foreach (EnemyStats enemy in enemies)
            {
                EnemyBehaviorDefinition behavior = enemy?.behaviorDefinition;
                if (behavior == null)
                    continue;
                foreach (EnemyBehaviorAction action in behavior.EnumerateActions())
                {
                    Track(action.TemporaryCard.Definition);
                    if (action.IntentType == EnemyIntentType.Compound)
                        Track(action.Compound.SecondaryTemporaryCard.Definition);
                }
            }
        }

        public void AttachCombat(CombatController combat)
        {
            ThrowIfDisposed();
            if (combat == null)
                throw new ArgumentNullException(nameof(combat));
            if (_combat != null)
                throw new InvalidOperationException(
                    "一次资源所有权只能绑定一场战斗。");
            _combat = combat;
            TrackCombatCards();
            combat.OnCardPlayedVFX += TrackPlayedCard;
            combat.OnCardsCopied += TrackCopiedCards;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_combat != null)
            {
                _combat.OnCardPlayedVFX -= TrackPlayedCard;
                _combat.OnCardsCopied -= TrackCopiedCards;
                // Rewards and injected cards may have entered a pile after
                // construction without ever being played or copied.
                TrackCombatCards();
                _combat = null;
            }
            foreach (Object item in _owned)
            {
                if (item == null || IsPersistentAsset(item))
                    continue;
                if (Application.isPlaying)
                    Object.Destroy(item);
                else
                    Object.DestroyImmediate(item, allowDestroyingAssets: false);
            }
            _owned.Clear();
        }

        private void TrackCombatCards()
        {
            TrackCards(_combat.Hand);
            TrackCards(_combat.Deck);
            TrackCards(_combat.Discard);
        }

        private void TrackCards(IReadOnlyList<CardConfig> cards)
        {
            if (cards == null)
                return;
            for (int i = 0; i < cards.Count; i++)
                Track(cards[i]);
        }

        private void Track(Object item)
        {
            if (item != null && !IsPersistentAsset(item))
                _owned.Add(item);
        }

        private static bool IsPersistentAsset(Object item)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.IsPersistent(item);
#else
            return false;
#endif
        }

        private void TrackPlayedCard(CardConfig card, int ignoredTarget)
            => Track(card);

        private void TrackCopiedCards(CombatCardsCopiedEvent copied)
            => TrackCards(copied.Cards);

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CombatEncounterResourceLease));
        }
    }
}
