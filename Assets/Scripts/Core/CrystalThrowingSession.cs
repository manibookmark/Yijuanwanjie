using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum CrystalThrowingStage { Preparing, Throwing, Ending, Returned }

    /// <summary>A real card transfer, not a played card or a confirmed collision.</summary>
    public readonly struct CrystalCardThrow
    {
        public int RuntimeInstanceId { get; }
        public int ThrowIndex { get; }
        internal CrystalCardThrow(int id, int index) { RuntimeInstanceId = id; ThrowIndex = index; }
    }

    /// <summary>
    /// Detaches the throwing owner's clock and physical membership. Restoring this
    /// projection is a project capability; VC partial-event save support is unproven.
    /// XP, the RNG and the player's persistent card rows are not owned here.
    /// </summary>
    public sealed class CrystalThrowingSnapshot
    {
        public CrystalThrowingStage Stage { get; }
        public float EventElapsed { get; }
        public float AutoThrowElapsed { get; }
        public int HeldButtonCount { get; }
        public int ThrownCount { get; }
        public IReadOnlyList<int> DrawPileTopFirst { get; }
        public IReadOnlyList<int> HitCardInstanceIds { get; }
        public SideEventCrystalSpecification Source { get; }
        public CrystalThrowingSnapshot(SideEventCrystalSpecification source,
            CrystalThrowingStage stage, float eventElapsed, float autoThrowElapsed, int heldButtonCount,
            int thrownCount, IReadOnlyList<int> drawPileTopFirst, IReadOnlyList<int> hitCardInstanceIds)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Stage = stage; EventElapsed = eventElapsed; AutoThrowElapsed = autoThrowElapsed;
            HeldButtonCount = heldButtonCount;
            ThrownCount = thrownCount;
            DrawPileTopFirst = Array.AsReadOnly(CrystalThrowingSession.CopyCards(drawPileTopFirst));
            HitCardInstanceIds = Array.AsReadOnly(CrystalThrowingSession.CopyCards(hitCardInstanceIds));
        }
    }

    /// <summary>
    /// Owns the Crystal throwing window and physical pile partition. Setup moves
    /// draw-top cards to throw-top, then throws from the bottom: original top-first
    /// order survives, without shuffling or admission/FCC filters. A presentation
    /// caller reports frames and input; throwing does not execute card effects or
    /// remove persistent cards. Actual XP/progression stay with the player owner.
    /// Cleanup exposes exact membership, without claiming VC's final deck order.
    /// </summary>
    public sealed class CrystalThrowingSession
    {
        private readonly SideEventCrystalSpecification _source;
        private readonly int[] _cards;
        private int _thrown;
        private float _elapsed;
        private float _autoElapsed;
        private int _heldButtons;
        private readonly List<int> _hits = new List<int>();
        public CrystalThrowingStage Stage { get; private set; }
        public int RemainingCards => Stage == CrystalThrowingStage.Returned ? 0 : _cards.Length - _thrown;
        public int ThrownCount => _thrown;
        public float EventElapsed => _elapsed;

        public CrystalThrowingSession(SideEventCrystalSpecification source, IReadOnlyList<int> drawPileTopFirst)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (source.Timing.EventTime <= 0 || source.Timing.AutoThrowDelay <= 0)
                throw new ArgumentException("Crystal needs positive event and repeat intervals.", nameof(source));
            _cards = CopyCards(drawPileTopFirst);
            Stage = CrystalThrowingStage.Preparing;
        }

        /// <summary>Call only after setup transfer and global pause barriers finish.</summary>
        public void StartThrowing()
        {
            if (Stage != CrystalThrowingStage.Preparing)
                throw new InvalidOperationException("Setup can finish only once.");
            Stage = CrystalThrowingStage.Throwing;
        }

        /// <summary>
        /// One source frame, at most one repeated throw plus a new press. Holding/all accumulate
        /// float delta time and use strict > delay; no catch-up loop is synthesized.
        /// A press requests an immediate throw, not a hit. Paused frames do nothing
        /// by project policy; VC's Update has no direct pause guard proven here.
        /// </summary>
        public IReadOnlyList<CrystalCardThrow> AdvanceFrame(float deltaTime, bool paused,
            bool pressed, bool released, bool throwAll)
        {
            RequireTime(deltaTime);
            var throws = new List<CrystalCardThrow>();
            if (paused || Stage != CrystalThrowingStage.Throwing) return throws.AsReadOnly();
            if (pressed && !released && _heldButtons == int.MaxValue)
                throw new OverflowException("Crystal input hold count overflow.");
            float nextElapsed = (float)((double)_elapsed + deltaTime);
            if (!IsFinite(nextElapsed)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (_heldButtons > 0 || throwAll)
            {
                _autoElapsed = (float)((double)_autoElapsed + deltaTime);
                if (_autoElapsed > _source.Timing.AutoThrowDelay)
                { AddThrow(throws); _autoElapsed = 0; }
            }
            _elapsed = nextElapsed;
            if (pressed && !released)
            {
                if (_heldButtons == 0) AddThrow(throws);
                _heldButtons++;
            }
            else if (released && !pressed)
            {
                _heldButtons = Math.Max(0, _heldButtons - 1);
                if (_heldButtons == 0) _autoElapsed = 0;
            }
            if (_elapsed >= _source.Timing.EventTime) BeginEnding();
            return throws.AsReadOnly();
        }

        private void AddThrow(List<CrystalCardThrow> throws)
        { if (ThrowNext(out var item)) throws.Add(item); }

        private bool ThrowNext(out CrystalCardThrow cardThrow)
        {
            cardThrow = default;
            if (_thrown == _cards.Length) return false;
            cardThrow = new CrystalCardThrow(_cards[_thrown], _thrown);
            _thrown++;
            return true;
        }

        /// <summary>Membership query only: no collision or reward is inferred.</summary>
        public bool WasThrown(int runtimeInstanceId)
        {
            for (int i = 0; i < _thrown; i++) if (_cards[i] == runtimeInstanceId) return true;
            return false;
        }

        /// <summary>
        /// Registers a real collision before the separate player's XP transaction.
        /// EXITING rejects late callbacks. A replay returns newlyRegistered=false,
        /// so the consumer must not draw RNG, credit XP or count another hit.
        /// This guard is a project guarantee; VC's handler itself lacks deduplication.
        /// </summary>
        public bool TryRegisterHit(int runtimeInstanceId, out bool newlyRegistered)
        {
            newlyRegistered = false;
            if (Stage != CrystalThrowingStage.Throwing || !WasThrown(runtimeInstanceId)) return false;
            if (_hits.Contains(runtimeInstanceId)) return true;
            _hits.Add(runtimeInstanceId); newlyRegistered = true;
            if (_cards.Length > 0 && _thrown == _cards.Length && _hits.Count == _thrown)
                BeginEnding();
            return true;
        }

        private void BeginEnding()
        { Stage = CrystalThrowingStage.Ending; _elapsed = _source.Timing.EventTime; }

        /// <summary>
        /// Source membership/order before its normal discard cleanup: newest hit
        /// at top, unthrown cards moved top-to-top on ending, outstanding throws at
        /// bottom in throw order. Shuffle/Swift insertion belongs to the deck owner.
        /// </summary>
        public IReadOnlyList<int> CaptureEndingDiscardTopFirst()
        {
            if (Stage != CrystalThrowingStage.Ending && Stage != CrystalThrowingStage.Returned)
                throw new InvalidOperationException("The throwing window must finish or abort before cleanup.");
            var discard = new List<int>();
            for (int i = _hits.Count - 1; i >= 0; i--) discard.Add(_hits[i]);
            for (int i = _cards.Length - 1; i >= _thrown; i--) discard.Insert(0, _cards[i]);
            for (int i = 0; i < _thrown; i++) if (!_hits.Contains(_cards[i])) discard.Add(_cards[i]);
            return discard.AsReadOnly();
        }

        /// <summary>
        /// After the external exit-animation/cleanup barrier, finish (or explicit
        /// project abort) returns exact setup membership. The five-second cutoff
        /// alone does not call this or complete a production node. Returned is terminal;
        /// subsequent cleanup replays the same detached list and never grants XP.
        /// The list is capture order, not a promise about the resulting deck order.
        /// </summary>
        public IReadOnlyList<int> ReturnCards()
        {
            Stage = CrystalThrowingStage.Returned;
            return Array.AsReadOnly((int[])_cards.Clone());
        }

        public CrystalThrowingSnapshot Capture()
            => new CrystalThrowingSnapshot(_source, Stage, _elapsed, _autoElapsed, _heldButtons, _thrown, _cards, _hits);

        public static CrystalThrowingSession Restore(CrystalThrowingSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var source = snapshot.Source;
            var result = new CrystalThrowingSession(source, snapshot.DrawPileTopFirst);
            RequireTime(snapshot.EventElapsed); RequireTime(snapshot.AutoThrowElapsed);
            if (!Enum.IsDefined(typeof(CrystalThrowingStage), snapshot.Stage) ||
                snapshot.ThrownCount < 0 || snapshot.ThrownCount > result._cards.Length ||
                snapshot.HeldButtonCount < 0 ||
                snapshot.AutoThrowElapsed > source.Timing.AutoThrowDelay ||
                (snapshot.Stage == CrystalThrowingStage.Preparing &&
                    (snapshot.EventElapsed != 0 || snapshot.AutoThrowElapsed != 0 || snapshot.ThrownCount != 0 || snapshot.HeldButtonCount != 0)) ||
                (snapshot.Stage == CrystalThrowingStage.Throwing && snapshot.EventElapsed >= source.Timing.EventTime) ||
                (snapshot.Stage == CrystalThrowingStage.Throwing && result._cards.Length > 0 &&
                    snapshot.ThrownCount == result._cards.Length && snapshot.HitCardInstanceIds.Count == snapshot.ThrownCount) ||
                (snapshot.Stage == CrystalThrowingStage.Ending && snapshot.EventElapsed != source.Timing.EventTime))
                throw new ArgumentException("Crystal clock, stage and pile snapshot disagree.", nameof(snapshot));
            result.Stage = snapshot.Stage; result._elapsed = snapshot.EventElapsed;
            result._autoElapsed = snapshot.AutoThrowElapsed; result._thrown = snapshot.ThrownCount;
            result._heldButtons = snapshot.HeldButtonCount;
            foreach (int id in snapshot.HitCardInstanceIds)
            {
                if (!result.WasThrown(id)) throw new ArgumentException("A Crystal hit must belong to an actual thrown instance.", nameof(snapshot));
                result._hits.Add(id);
            }
            return result;
        }

        /// <summary>
        /// The supplied count is SOURCE completed floors, not the five-floor
        /// project's floor ordinal. This returns raw XP; the real progression owner
        /// must apply Growth once and record its actual result on a genuine hit.
        /// </summary>
        public static int DrawRawHitExperience(SideEventCrystalSpecification source,
            int completedSourceFloors, int sourceFloorCount, IRandomSource random)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (completedSourceFloors < 0 || sourceFloorCount <= 0 || completedSourceFloors > sourceFloorCount)
                throw new ArgumentOutOfRangeException(nameof(completedSourceFloors));
            var reward = source.Reward;
            int scale = reward.MultiplyByCompletedFloorPlusOneCappedToFloorCount
                ? (int)Math.Min((long)completedSourceFloors + 1, sourceFloorCount) : 1;
            if ((long)(reward.MaximumHitXpExclusive - 1) * scale > int.MaxValue)
                throw new OverflowException("Crystal raw hit XP exceeds the progression representation.");
            int rolled = random.Range(reward.MinimumHitXpInclusive, reward.MaximumHitXpExclusive);
            if (rolled < reward.MinimumHitXpInclusive || rolled >= reward.MaximumHitXpExclusive)
                throw new InvalidOperationException("The random source violated the Crystal hit interval.");
            return checked(rolled * scale);
        }

        /// <summary>
        /// Exact source hit policy: each single-precision Growth multiplication is
        /// rounded UP independently. This is distinct from the project's general
        /// fractional-carry policy; this function never changes that shared owner.
        /// The caller credits this result once and uses the same actual amount for
        /// PickupCrystal statistics and XP presentation. No fixed total is invented.
        /// </summary>
        public static int CalculateSourceAdjustedHitExperience(int rawExperience, float growthPercent)
        {
            if (rawExperience < 0) throw new ArgumentOutOfRangeException(nameof(rawExperience));
            if (!IsFinite(growthPercent)) throw new ArgumentOutOfRangeException(nameof(growthPercent));
            float ratio = (float)((double)growthPercent / 100.0);
            float multiplier = (float)((double)ratio + 1.0);
            float raw = (float)rawExperience;
            float adjusted = (float)((double)raw * multiplier);
            if (!IsFinite(adjusted) || adjusted < 0)
                throw new ArgumentOutOfRangeException(nameof(growthPercent));
            double rounded = Math.Ceiling((double)adjusted);
            if (rounded > int.MaxValue) throw new OverflowException("Crystal adjusted XP exceeds the player representation.");
            return (int)rounded;
        }

        internal static int[] CopyCards(IReadOnlyList<int> cards)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            var copy = new int[cards.Count]; var ids = new HashSet<int>();
            for (int i = 0; i < copy.Length; i++)
            {
                if (cards[i] <= 0 || !ids.Add(cards[i]))
                    throw new ArgumentException("The throwing pile requires distinct physical card IDs.", nameof(cards));
                copy[i] = cards[i];
            }
            return copy;
        }
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void RequireTime(float value)
        { if (!IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
    }
}
