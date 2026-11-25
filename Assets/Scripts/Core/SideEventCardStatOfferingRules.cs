using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// One explicit tuple returned by the physical card's GetModifiedStats equivalent.
    /// Unknown evidence is representable, but cannot be used to prepare an offering.
    /// SourceStat is the native ID, not a project StatType ordinal.
    /// </summary>
    public sealed class SideEventCardModifiedStat
    {
        public int? SourceStat { get; }
        public int? Amount { get; }

        public SideEventCardModifiedStat(int? sourceStat, int? amount)
        { SourceStat = sourceStat; Amount = amount; }
    }

    /// <summary>An ordered, immutable requested modifier. Applying it and measuring saturation belong to the Run stat owner.</summary>
    public sealed class SideEventCardStatOfferingReward
    {
        public int ModifiedStatIndex { get; }
        public int SourceStat { get; }
        public int ModifiedAmount { get; }
        public int BaseChangeAmount { get; }
        public float Modifier { get; }
        public float Binary32Product { get; }
        public int RequestedAmount { get; }

        internal SideEventCardStatOfferingReward(int index, int stat, int amount,
            int baseline, float modifier, float product, int requested)
        {
            ModifiedStatIndex = index; SourceStat = stat; ModifiedAmount = amount;
            BaseChangeAmount = baseline; Modifier = modifier;
            Binary32Product = product; RequestedAmount = requested;
        }
    }

    public sealed class SideEventCardStatOfferingResult
    {
        public IReadOnlyList<SideEventCardStatOfferingReward> Rewards { get; }
        public int CardsDestroyed => 1;
        public int GoldCost => 0;
        public int CashOutAmount => 0;
        public bool CompletesEvent => true;

        internal SideEventCardStatOfferingResult(List<SideEventCardStatOfferingReward> rewards)
        { Rewards = Array.AsReadOnly(rewards.ToArray()); }
    }

    /// <summary>The independent CashOutCoins(200, false) branch; it neither selects nor destroys a card.</summary>
    public sealed class SideEventCardStatOfferingCashOut
    {
        public int RequestedAmount => 200;
        public bool UsesGreed => false;
        public int CardsDestroyed => 0;
        public int GoldCost => 0;
        public bool CompletesEvent => true;
        public string SourceRuntimeMetricId => "GoldEarned";
        public int SourceRuntimeMetricAmount => RequestedAmount;
        internal SideEventCardStatOfferingCashOut() { }
    }

    /// <summary>
    /// Executes the captured CardStatOffering selection and requested reward arithmetic.
    /// The complete immutable source specification owns the support table; this module
    /// does not introduce another table, project attribute mapping, account gate, or ledger.
    ///
    /// CardConfig.GetModifiedStats appends one (native stat, int amount) per effect.
    /// The modal keeps that order and keeps repeated stat IDs. For each supported tuple,
    /// it replaces the setting's AdditionalChangeAmount with the tuple amount, then uses
    /// max(1, floor(binary32(binary32(base + amount) * modifier))). It does not aggregate
    /// tuples, apply unsupported stats, or add the original AdditionalChangeAmount.
    ///
    /// The captured selection excludes FCC and does not require the offering flag.
    /// Unknown FCC/stat/amount evidence fails closed; an unknown offering flag is unused.
    /// Nonfinite arithmetic and integer/conversion overflow are explicit project guards;
    /// native unchecked overflow is not reproduced. Nothing here mutates a card or stat,
    /// grants permanent progress, persists, or authorizes a source adapter to execute.
    /// </summary>
    public static class SideEventCardStatOfferingRules
    {
        public static SideEventCardStatOfferingResult Prepare(
            SideEventCardStatOfferingSpecification specification, bool? isFcc,
            bool? allowInOfferingTable, IReadOnlyList<SideEventCardModifiedStat> modifiedStats)
        {
            var settings = RequireSpecification(specification);
            if (!isFcc.HasValue)
                throw new InvalidOperationException("CardStatOffering FCC identity is unknown.");
            if (isFcc.Value)
                throw new InvalidOperationException("FCC cards cannot be used in CardStatOffering.");
            if (modifiedStats == null) throw new ArgumentNullException(nameof(modifiedStats));

            // RequireOfferingFlag is false for this captured mechanism. In particular,
            // Unknown or ExcludedByFlag admission is not silently changed to Allowed.
            var rewards = new List<SideEventCardStatOfferingReward>();
            for (int i = 0; i < modifiedStats.Count; i++)
            {
                var tuple = modifiedStats[i];
                if (tuple == null || !tuple.SourceStat.HasValue || !tuple.Amount.HasValue)
                    throw new InvalidOperationException("CardStatOffering ModifiedStat evidence is incomplete.");
                RequireSourceStat(tuple.SourceStat.Value);
                if (!settings.TryGetValue(tuple.SourceStat.Value, out var setting)) continue;

                int sum = checked(setting.BaseChangeAmount + tuple.Amount.Value);
                float scalar = Binary32((float)sum);
                float product = Binary32(scalar * setting.Modifier);
                if (!IsFinite(product))
                    throw new OverflowException("CardStatOffering product exceeded finite binary32 arithmetic.");
                double floored = Math.Floor((double)product);
                if (floored < int.MinValue || floored > int.MaxValue)
                    throw new OverflowException("CardStatOffering reward exceeded the signed 32-bit conversion range.");
                int requested = Math.Max(1, (int)floored);
                rewards.Add(new SideEventCardStatOfferingReward(i, setting.SourceStat,
                    tuple.Amount.Value, setting.BaseChangeAmount, setting.Modifier, product, requested));
            }
            if (rewards.Count == 0)
                throw new InvalidOperationException("The card provides no supported CardStatOffering ModifiedStat.");
            return new SideEventCardStatOfferingResult(rewards);
        }

        public static SideEventCardStatOfferingCashOut PrepareCashOut(
            SideEventCardStatOfferingSpecification specification)
        {
            RequireSpecification(specification);
            return new SideEventCardStatOfferingCashOut();
        }

        private static Dictionary<int, SideEventStatSetting> RequireSpecification(
            SideEventCardStatOfferingSpecification specification)
        {
            if (specification == null) throw new ArgumentNullException(nameof(specification));
            var selection = specification.Selection;
            if (selection.CardsRequired != 1 || !selection.DestroySelected || !selection.ExcludeFcc
                || selection.RequireOfferingFlag || selection.OutsideClickCloses
                || !selection.RequireSupportedModifiedStat)
                throw new ArgumentException("Unsupported CardStatOffering selection contract.", nameof(specification));
            if (specification.CashOutAmount != 200 || specification.CashOutUsesGreed)
                throw new ArgumentException("CardStatOffering requires its independent 200-gold, no-Greed branch.", nameof(specification));
            if (specification.StatSettings.Count == 0)
                throw new ArgumentException("CardStatOffering support settings are empty.", nameof(specification));

            var settings = new Dictionary<int, SideEventStatSetting>();
            foreach (var setting in specification.StatSettings)
            {
                RequireSourceStat(setting.SourceStat);
                if (!IsFinite(setting.Modifier)) throw new ArgumentException("Nonfinite source stat modifier.");
                // The immutable specification itself already rejects duplicate IDs.
                settings.Add(setting.SourceStat, setting);
            }
            return settings;
        }

        private static void RequireSourceStat(int stat)
        {
            // Native PlayerStats.Stats is 0..19. None=999999 is omitted by
            // GetModifiedStats and cannot serve as actual returned stat evidence.
            if (stat < 0 || stat > 19)
                throw new ArgumentOutOfRangeException(nameof(stat), "Unknown native PlayerStats.Stats ID.");
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Binary32(float value) => BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
    }
}
