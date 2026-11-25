using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// A detached source-price observation. CompletedFloorCount is the count of
    /// completed source floors, not the current project floor or encounter count.
    /// Capturing this result does not grant a hire, pay a wallet, or resolve FCC eligibility.
    /// </summary>
    public sealed class SideEventGunForHireQuote
    {
        public int RawBuyPrice { get; }
        public int RawPityPrice { get; }
        public int CompletedFloorCount { get; }
        public float CompletedFloorCountBinary32 { get; }
        public float FloorSurchargeBinary32 { get; }
        public int FloorSurcharge { get; }
        public int PityPriceAfterFirstRounding { get; }
        public int BuyPriceBeforeFinalRounding { get; }
        public int PityPriceBeforeFinalRounding { get; }
        public int BuyPrice { get; }
        public int PityPrice { get; }

        internal SideEventGunForHireQuote(int rawBuy, int rawPity, int completed,
            float completedBinary32, float surchargeBinary32, int surcharge,
            int firstRoundedPity, int buyBeforeFinal, int pityBeforeFinal, int buy, int pity)
        {
            RawBuyPrice = rawBuy;
            RawPityPrice = rawPity;
            CompletedFloorCount = completed;
            CompletedFloorCountBinary32 = completedBinary32;
            FloorSurchargeBinary32 = surchargeBinary32;
            FloorSurcharge = surcharge;
            PityPriceAfterFirstRounding = firstRoundedPity;
            BuyPriceBeforeFinalRounding = buyBeforeFinal;
            PityPriceBeforeFinalRounding = pityBeforeFinal;
            BuyPrice = buy;
            PityPrice = pity;
        }
    }

    /// <summary>
    /// Owns only the captured GunForHire price arithmetic: inclusive integer raw
    /// Buy 150..250 and Pity 400..1000; binary32(completed floors) * 20; then
    /// Buy=Round10(raw+surcharge), Pity=Round10(Round10(raw)+surcharge).
    /// Round10 converts the integer to binary32, divides by binary32 10, promotes
    /// that quotient to double, and rounds ties to even. The native CVTTSD2SI
    /// indefinite result and 32-bit addition/multiplication wrap are explicit.
    /// Consequently an extreme source quote may be negative; it is an arithmetic
    /// observation, not an assertion that a future payment owner may accept it.
    ///
    /// Quote is replayable without RNG. DrawQuote consumes only Buy then Pity;
    /// the native modal's earlier optional FCC candidate draw belongs elsewhere.
    /// IRandomSource stream identity is not claimed equal to the native generator.
    /// This source policy is not a production Hire adapter or project price policy.
    /// </summary>
    public static class SideEventGunForHirePriceRules
    {
        public const int CapturedSourcePolicyVersion = 1;
        public const string CapturedSourcePolicyId = "vc-gun-for-hire-price/binary32-ties-even-int32/v1";

        public static void ValidateSpecification(SideEventGunForHireSpecification specification)
        {
            if (specification == null) throw new ArgumentNullException(nameof(specification));
            RequirePrice(specification.BuyPrice, 150, 250, false, "Buy");
            RequirePrice(specification.PityPrice, 400, 1000, true, "Pity");
            if (specification.PityUsesGreed)
                throw new ArgumentException("Captured GunForHire pity price does not use Greed.", nameof(specification));
        }

        public static SideEventGunForHireQuote Quote(SideEventGunForHireSpecification specification,
            int rawBuyPrice, int rawPityPrice, int completedFloorCount)
        {
            ValidateSpecification(specification);
            RequireCompletedCount(completedFloorCount);
            RequireDraw(rawBuyPrice, specification.BuyPrice, "Buy");
            RequireDraw(rawPityPrice, specification.PityPrice, "Pity");

            float completed = Binary32(completedFloorCount);
            float surchargeFloat = Binary32(completed * 20f);
            int surcharge = RoundToNativeInt32(surchargeFloat);
            int firstPity = RoundToTen(rawPityPrice);
            int buyBefore = unchecked(rawBuyPrice + surcharge);
            int pityBefore = unchecked(firstPity + surcharge);
            return new SideEventGunForHireQuote(rawBuyPrice, rawPityPrice, completedFloorCount,
                completed, surchargeFloat, surcharge, firstPity, buyBefore, pityBefore,
                RoundToTen(buyBefore), RoundToTen(pityBefore));
        }

        public static SideEventGunForHireQuote DrawQuote(SideEventGunForHireSpecification specification,
            int completedFloorCount, IRandomSource random)
        {
            ValidateSpecification(specification);
            RequireCompletedCount(completedFloorCount);
            if (random == null) throw new ArgumentNullException(nameof(random));
            int buy = random.Range(150, 251);
            RequireDraw(buy, specification.BuyPrice, "Buy");
            int pity = random.Range(400, 1001);
            RequireDraw(pity, specification.PityPrice, "Pity");
            return Quote(specification, buy, pity, completedFloorCount);
        }

        private static void RequirePrice(SideEventHirePrice price, int minimum, int maximum,
            bool roundBefore, string branch)
        {
            if (price == null || price.MinimumInclusive != minimum || price.MaximumInclusive != maximum ||
                price.PerCompletedFloor != 20 || price.RoundMultiple != 10 ||
                price.RoundBeforeFloorAddition != roundBefore || !price.RoundAfterFloorAddition)
                throw new ArgumentException(branch + " price contradicts the captured GunForHire contract.");
        }

        private static void RequireCompletedCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Completed floor count cannot be negative.");
        }

        private static void RequireDraw(int draw, SideEventHirePrice price, string branch)
        {
            if (draw < price.MinimumInclusive || draw > price.MaximumInclusive)
                throw new ArgumentOutOfRangeException(nameof(draw), branch + " raw price is outside the inclusive source interval.");
        }

        private static int RoundToTen(int value)
        {
            float quotient = Binary32(Binary32(value) / 10f);
            int rounded = RoundToNativeInt32(quotient);
            return unchecked(rounded * 10);
        }

        private static int RoundToNativeInt32(float value)
        {
            double rounded = Math.Round((double)value, MidpointRounding.ToEven);
            // CVTTSD2SI r32 returns 0x80000000 on an invalid conversion. Never
            // depend on a managed runtime's platform-specific out-of-range cast.
            if (double.IsNaN(rounded) || rounded < int.MinValue || rounded >= 2147483648d)
                return int.MinValue;
            return (int)rounded;
        }

        private static float Binary32(float value)
            => BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
    }
}
