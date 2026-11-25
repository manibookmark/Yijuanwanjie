using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    public enum EnemyActionPresentationSignalKind
    {
        Started = 0,
        SpellRelease = 1,
        Completed = 2,
        Cancelled = 3,
        Impact = 4
    }

    /// <summary>Read-only actor presentation information. It does not grant cards or expose renderers.</summary>
    public readonly struct EnemyActionPresentationSignal
    {
        public EnemyActionPresentationSignal(
            EnemyActionPresentationSignalKind kind,
            long generation,
            int enemyIndex,
            string actionId,
            bool willEmitSpellRelease,
            bool hasSourceAnchor = false,
            Vector3 sourceAnchorWorld = default)
        {
            Kind = kind;
            Generation = generation;
            EnemyIndex = enemyIndex;
            ActionId = actionId ?? string.Empty;
            WillEmitSpellRelease = willEmitSpellRelease;
            HasSourceAnchor = hasSourceAnchor;
            SourceAnchorWorld = sourceAnchorWorld;
        }

        public EnemyActionPresentationSignalKind Kind { get; }
        public long Generation { get; }
        public int EnemyIndex { get; }
        public string ActionId { get; }
        public bool WillEmitSpellRelease { get; }
        public bool HasSourceAnchor { get; }
        public Vector3 SourceAnchorWorld { get; }
    }

    /// <summary>
    /// One presentation's non-UI delivery ledger. Its presenter owns it; cancellation
    /// retires callbacks without inventing an impact/completion or rolling back committed rules.
    /// </summary>
    public sealed class EnemyActionPresentationLifetime
    {
        public EnemyActionPresentationLifetime(
            long generation, int enemyIndex, string actionId, bool willEmitSpellRelease)
        {
            Generation = generation;
            EnemyIndex = enemyIndex;
            ActionId = actionId ?? string.Empty;
            WillEmitSpellRelease = willEmitSpellRelease;
        }

        public long Generation { get; }
        public int EnemyIndex { get; }
        public string ActionId { get; }
        public bool WillEmitSpellRelease { get; }
        public bool IsTerminal { get; private set; }
        public bool ImpactDelivered { get; private set; }
        public bool ReleaseDelivered { get; private set; }

        public bool TryDeliverImpact()
        {
            if (IsTerminal || ImpactDelivered) return false;
            ImpactDelivered = true;
            return true;
        }

        /// <summary>Publishes the same unique impact boundary used by rule
        /// callbacks. A cancelled or repeated delivery cannot create a cue.</summary>
        public bool TryCreateImpactSignal(Vector3? sourceAnchorWorld,
            out EnemyActionPresentationSignal signal)
        {
            signal = default;
            if (!TryDeliverImpact()) return false;
            signal = new EnemyActionPresentationSignal(EnemyActionPresentationSignalKind.Impact,
                Generation, EnemyIndex, ActionId, WillEmitSpellRelease,
                sourceAnchorWorld.HasValue, sourceAnchorWorld.GetValueOrDefault());
            return true;
        }

        public bool TryDeliverRelease()
        {
            if (IsTerminal || !WillEmitSpellRelease || !ImpactDelivered || ReleaseDelivered)
                return false;
            ReleaseDelivered = true;
            return true;
        }

        public bool TryComplete()
        {
            if (IsTerminal) return false;
            IsTerminal = true;
            return true;
        }

        public bool TryCancel()
        {
            if (IsTerminal) return false;
            IsTerminal = true;
            return true;
        }
    }
}
