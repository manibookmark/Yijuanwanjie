using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Version 1 records the approved whole-Run roster adaptation.
    /// Source timing and payouts are fixed; a display name never authorizes it.</summary>
    [Serializable]
    public sealed class LevelInteractionCrystalContract
    {
        public int version;
        public string sourceEventId;
        public bool IsSpecified => version != 0 || !string.IsNullOrEmpty(sourceEventId);
    }

    public static class CrystalEventContracts
    {
        public const string SourceEventId = "PassiveEvent_Crystal";
        public static SideEventCrystalSpecification GetSpecification(LevelInteractionEffect effect)
        {
            var errors = new List<string>(); ValidateEffect(effect, errors);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join(" ", errors));
            return CreateSpecification();
        }

        /// <summary>The complete adopted source parameter contract. Binary32 bits,
        /// including signed zero, must match; an unknown total never becomes a fixed XP reward.
        /// This permits the approved whole-Run roster projection, not Global metric equivalence.</summary>
        public static bool MatchesSourceSpecification(SideEventCrystalSpecification source)
        {
            if (source == null) return false;
            var expected = CreateSpecification();
            var actualTiming = source.Timing; var timing = expected.Timing;
            var actualReward = source.Reward; var reward = expected.Reward;
            return SameBits(actualTiming.EventTime, timing.EventTime) &&
                SameBits(actualTiming.BaseRunSpeed, timing.BaseRunSpeed) &&
                SameBits(actualTiming.RunSpeedIncrease, timing.RunSpeedIncrease) &&
                SameBits(actualTiming.MinimumWait, timing.MinimumWait) &&
                SameBits(actualTiming.MaximumWait, timing.MaximumWait) &&
                SameBits(actualTiming.ExitTime, timing.ExitTime) &&
                SameBits(actualTiming.PeriodicDropInterval, timing.PeriodicDropInterval) &&
                SameBits(actualTiming.AutoThrowDelay, timing.AutoThrowDelay) &&
                SameBits(actualTiming.JumpTime, timing.JumpTime) &&
                SameBits(actualTiming.JumpHeight, timing.JumpHeight) &&
                actualReward.MinimumHitXpInclusive == reward.MinimumHitXpInclusive &&
                actualReward.MaximumHitXpExclusive == reward.MaximumHitXpExclusive &&
                actualReward.MultiplyByCompletedFloorPlusOneCappedToFloorCount == reward.MultiplyByCompletedFloorPlusOneCappedToFloorCount &&
                actualReward.ApplyGrowthThroughPlayerOwner == reward.ApplyGrowthThroughPlayerOwner &&
                SameBits(actualReward.TreasureChestChanceMultiplier, reward.TreasureChestChanceMultiplier) &&
                actualReward.MinimumFloorForChest == reward.MinimumFloorForChest &&
                !actualReward.TotalXp.IsKnown && !source.SacrificesPersistentCards;
        }

        private static bool SameBits(float first, float second)
            => BitConverter.ToInt32(BitConverter.GetBytes(first), 0) == BitConverter.ToInt32(BitConverter.GetBytes(second), 0);

        private static SideEventCrystalSpecification CreateSpecification()
            => new SideEventCrystalSpecification(
                new SideEventCrystalTiming(5f, 4f, 1f, 0f, .1f, 2f, 999f, .3f, .29f, .7f),
                new SideEventCrystalReward(2, 5, true, true, 0f, 10, SideEventOptionalInt.Unknown));

        public static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        {
            if (effect == null || effect.kind != LevelInteractionEffectKind.CrystalThrowing ||
                !effect.HasCrystalThrowingContract || effect.crystalThrowing.version != 1 ||
                effect.crystalThrowing.sourceEventId != SourceEventId ||
                effect.amount != 0 || effect.amountMax != 0 || !string.IsNullOrEmpty(effect.targetContentId) ||
                (effect.cardChoices?.Count ?? 0) != 0 || (effect.sealChoices?.Count ?? 0) != 0 ||
                !string.IsNullOrEmpty(effect.sealRewardPoolId) || effect.cashOutJade != 0 ||
                effect.HasCrawlerInvitationContract || effect.HasFixedEvolutionContract || effect.HasTargetLevelResultContract || effect.HasDoubleKeyPayment ||
                effect.HasArcanaChoiceContract || effect.HasChickenOfferingContract || effect.HasManaOfferingContract ||
                effect.HasManaStatueContract || effect.HasAbandonedCartContract)
                errors.Add("Crystal requires its exact versioned whole-Run lifecycle contract, without generic rewards or payment.");
        }
    }
}
