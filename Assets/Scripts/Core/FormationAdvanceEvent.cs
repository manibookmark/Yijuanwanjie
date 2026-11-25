using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WanjieScroll.Core
{
    /// <summary>
    /// The authoritative boundary that caused living enemies to change their
    /// formation footprints. Consumers must branch on this typed cause rather
    /// than infer timing from logs or the current battle phase.
    /// </summary>
    public enum FormationAdvanceCause
    {
        DefeatBackfill,
        WaveDeploy,
        EnemyTurnPreparation,
        KnockbackBoundaryReturn,
        WithdrawalBackfill,
        EmptyFieldRecovery
    }

    /// <summary>
    /// Immutable occupied grid rectangle. Rows and columns use the formation
    /// origin; width and depth extend toward increasing columns and rows.
    /// </summary>
    public readonly struct FormationFootprint : IEquatable<FormationFootprint>
    {
        public FormationFootprint(int row, int column, int width, int depth)
        {
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (depth < 1)
                throw new ArgumentOutOfRangeException(nameof(depth));

            Row = row;
            Column = column;
            Width = width;
            Depth = depth;
        }

        public int Row { get; }
        public int Column { get; }
        public int Width { get; }
        public int Depth { get; }
        public int EndRowExclusive => Row + Depth;
        public int EndColumnExclusive => Column + Width;

        public bool Intersects(FormationFootprint other)
            => Row < other.EndRowExclusive &&
               EndRowExclusive > other.Row &&
               Column < other.EndColumnExclusive &&
               EndColumnExclusive > other.Column;

        public bool Equals(FormationFootprint other)
            => Row == other.Row &&
               Column == other.Column &&
               Width == other.Width &&
               Depth == other.Depth;

        public override bool Equals(object obj)
            => obj is FormationFootprint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Row;
                hash = (hash * 397) ^ Column;
                hash = (hash * 397) ^ Width;
                return (hash * 397) ^ Depth;
            }
        }

        public static bool operator ==(
            FormationFootprint left,
            FormationFootprint right)
            => left.Equals(right);

        public static bool operator !=(
            FormationFootprint left,
            FormationFootprint right)
            => !left.Equals(right);
    }

    /// <summary>
    /// One enemy's authoritative before/after movement. RuntimeId is the
    /// stable encounter identity; FormationIndex only locates its existing
    /// presentation actor and must not be used as identity.
    /// </summary>
    public readonly struct FormationAdvanceMove
    {
        public FormationAdvanceMove(
            int runtimeId,
            int formationIndex,
            FormationFootprint fromFootprint,
            FormationFootprint toFootprint)
        {
            RuntimeId = runtimeId;
            FormationIndex = formationIndex;
            FromFootprint = fromFootprint;
            ToFootprint = toFootprint;
        }

        public int RuntimeId { get; }
        public int FormationIndex { get; }
        public FormationFootprint FromFootprint { get; }
        public FormationFootprint ToFootprint { get; }
    }

    /// <summary>
    /// Immutable publication of one non-empty authoritative formation
    /// advance. AdvanceId is monotonic within its CombatController instance.
    /// </summary>
    public sealed class FormationAdvanceEvent
    {
        internal FormationAdvanceEvent(
            long advanceId,
            FormationAdvanceCause cause,
            IReadOnlyList<FormationAdvanceMove> moves)
        {
            if (advanceId < 1)
                throw new ArgumentOutOfRangeException(nameof(advanceId));
            if (moves == null)
                throw new ArgumentNullException(nameof(moves));
            if (moves.Count == 0)
                throw new ArgumentException(
                    "Formation advance events cannot be empty.",
                    nameof(moves));

            var snapshot = new FormationAdvanceMove[moves.Count];
            for (int i = 0; i < moves.Count; i++)
                snapshot[i] = moves[i];

            AdvanceId = advanceId;
            Cause = cause;
            Moves = new ReadOnlyCollection<FormationAdvanceMove>(snapshot);
        }

        public long AdvanceId { get; }
        public FormationAdvanceCause Cause { get; }
        public IReadOnlyList<FormationAdvanceMove> Moves { get; }
    }

    /// <summary>
    /// Internal hand-off from the layout owner to CombatController. A single
    /// resolution may yield multiple batches when ordinary column backfill
    /// and whole-wave deployment have different presentation contracts.
    /// </summary>
    internal sealed class FormationAdvanceBatch
    {
        internal FormationAdvanceBatch(
            FormationAdvanceCause cause,
            IReadOnlyList<FormationAdvanceMove> moves)
        {
            Cause = cause;
            Moves = moves ?? throw new ArgumentNullException(nameof(moves));
        }

        internal FormationAdvanceCause Cause { get; }
        internal IReadOnlyList<FormationAdvanceMove> Moves { get; }
    }
}
