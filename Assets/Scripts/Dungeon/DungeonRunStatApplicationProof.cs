using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Immutable, canonical evidence of an ordered project stat application. The
    /// existing stat owners hold all mutable values; this document owns only the
    /// complete closed wire and detached snapshots. Parse through StatApplications
    /// to replay its sole mapping and arithmetic. A self-consistent document does
    /// not prove a genuine card, source specification, Run baseline, payment or
    /// earlier application. Those associations require the transaction journal.
    /// </summary>
    internal sealed class DungeonRunStatApplicationProof
    {
        public const int FormatVersion = 1;
        private static ExactJsonWire Wire => new ExactJsonWire(typeof(EnvelopeData),
            typeof(BaselineData), typeof(StateData), typeof(RewardData), typeof(ReceiptData));
        private readonly string _canonicalJson;
        public string PolicyVersion => DungeonRunStatApplications.PolicyVersion;
        public string Fingerprint { get; }
        public DungeonRunStatApplicationBaseline Baseline { get; }
        public DungeonRunStatApplicationState Before { get; }
        public DungeonRunStatApplicationState After { get; }
        public IReadOnlyList<DungeonRunStatApplicationRequest> OrderedRewards { get; }
        public IReadOnlyList<DungeonRunStatApplicationReceipt> Receipts { get; }

        internal DungeonRunStatApplicationProof(DungeonRunPlayerBaselineSaveData baseline,
            DungeonRunStatApplicationState before, DungeonRunStatApplicationState after,
            IReadOnlyList<DungeonRunStatApplicationRequest> rewards,
            IReadOnlyList<DungeonRunStatApplicationReceipt> receipts)
        {
            Baseline = new DungeonRunStatApplicationBaseline(baseline);
            Before = before; After = after;
            OrderedRewards = Copy(rewards); Receipts = Copy(receipts);
            if (OrderedRewards.Count == 0 || OrderedRewards.Count != Receipts.Count)
                throw new InvalidOperationException("Stat evidence requires a nonempty ordered reward/receipt pair.");
            _canonicalJson = Wire.Write(ToData());
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(_canonicalJson));
                Fingerprint = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }

        public string Serialize() => _canonicalJson;

        internal static DungeonRunStatApplicationProof Read(string json)
        {
            var data = Wire.Read<EnvelopeData>(json);
            if (data == null || data.formatVersion != FormatVersion || data.policyVersion != DungeonRunStatApplications.PolicyVersion ||
                data.baseline == null || data.before == null || data.after == null || data.rewards == null || data.receipts == null ||
                data.rewards.Count == 0 || data.rewards.Count != data.receipts.Count)
                throw new InvalidOperationException("Incomplete or unsupported stat proof envelope.");
            var rewards = new List<DungeonRunStatApplicationRequest>();
            var receipts = new List<DungeonRunStatApplicationReceipt>();
            int previousIndex = -1;
            for (int i = 0; i < data.rewards.Count; i++)
            {
                var reward = data.rewards[i]; var receipt = data.receipts[i];
                if (reward == null || receipt == null || reward.modifiedStatIndex <= previousIndex)
                    throw new InvalidOperationException("Reward tuple order is invalid.");
                var request = new DungeonRunStatApplicationRequest(reward.modifiedStatIndex, reward.sourceStat,
                    reward.modifiedAmount, reward.baseChangeAmount, reward.modifierBits, reward.productBits, reward.requestedAmount);
                previousIndex = request.ModifiedStatIndex;
                if (reward.mode != (int)request.Mode) throw new InvalidOperationException("Reward mode is not captured default mode.");
                rewards.Add(request);
                receipts.Add(new DungeonRunStatApplicationReceipt(request, request.Mode, receipt.ownedBefore,
                    receipt.ownedAfter, receipt.effectiveBefore, receipt.effectiveAfter));
            }
            var proof = new DungeonRunStatApplicationProof(BaselineFrom(data.baseline), StateFrom(data.before),
                StateFrom(data.after), rewards, receipts);
            // Checks every saved receipt identity/derived value, binary32 spelling,
            // field ordering and exact canonical envelope, before mathematical replay.
            if (!string.Equals(proof.Serialize(), json, StringComparison.Ordinal))
                throw new InvalidOperationException("Stat proof is not its complete canonical wire.");
            return proof;
        }

        internal static string WriteBaseline(DungeonRunPlayerBaselineSaveData baseline)
            => Wire.Write(BaselineTo(baseline ?? throw new ArgumentNullException(nameof(baseline))));

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> items) where T : class
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var copy = new T[items.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = items[i] ?? throw new InvalidOperationException("Null evidence item.");
            return Array.AsReadOnly(copy);
        }

        private EnvelopeData ToData()
        {
            var data = new EnvelopeData { formatVersion = FormatVersion, policyVersion = PolicyVersion,
                baseline = BaselineTo(Baseline.ToSaveData()), before = StateTo(Before), after = StateTo(After),
                rewards = new List<RewardData>(), receipts = new List<ReceiptData>() };
            foreach (var reward in OrderedRewards) data.rewards.Add(new RewardData {
                modifiedStatIndex = reward.ModifiedStatIndex, sourceStat = reward.SourceStat,
                modifiedAmount = reward.ModifiedAmount, baseChangeAmount = reward.BaseChangeAmount,
                modifierBits = reward.ModifierBits, productBits = reward.ProductBits,
                requestedAmount = reward.RequestedAmount, mode = (int)reward.Mode });
            foreach (var receipt in Receipts) data.receipts.Add(new ReceiptData {
                modifiedStatIndex = receipt.ModifiedStatIndex, sourceStat = receipt.SourceStat, mode = (int)receipt.Mode,
                requestedAmount = receipt.RequestedAmount, ownedBefore = receipt.OwnedBefore, ownedAfter = receipt.OwnedAfter,
                actualOwnedDelta = receipt.ActualOwnedDelta, effectiveBefore = receipt.EffectiveBefore, effectiveAfter = receipt.EffectiveAfter,
                acceptedEffectiveDelta = receipt.AcceptedEffectiveDelta, baseDelta = receipt.BaseDelta,
                currentDelta = receipt.CurrentDelta, policyVersion = receipt.PolicyVersion });
            return data;
        }

        private static BaselineData BaselineTo(DungeonRunPlayerBaselineSaveData b) => new BaselineData {
            baseArmorPerTurn = b.baseArmorPerTurn, fengRui = b.fengRui, amount = b.amount, bengLie = b.bengLie,
            ningShen = b.ningShen, shouPai = b.shouPai, yinJuan = b.yinJuan, chouPai = b.chouPai,
            tianYun = b.tianYun, lianYu = b.lianYu, mingWu = b.mingWu, hunHun = b.hunHun,
            faLi = b.faLi, zhaoSha = b.zhaoSha, recovery = b.recovery };
        private static DungeonRunPlayerBaselineSaveData BaselineFrom(BaselineData b) => new DungeonRunPlayerBaselineSaveData {
            baseArmorPerTurn = b.baseArmorPerTurn, fengRui = b.fengRui, amount = b.amount, bengLie = b.bengLie,
            ningShen = b.ningShen, shouPai = b.shouPai, yinJuan = b.yinJuan, chouPai = b.chouPai,
            tianYun = b.tianYun, lianYu = b.lianYu, mingWu = b.mingWu, hunHun = b.hunHun,
            faLi = b.faLi, zhaoSha = b.zhaoSha, recovery = b.recovery };

        private static StateData StateTo(DungeonRunStatApplicationState state)
        {
            var m = state.Modifiers; var p = state.Progression; var a = state.Additional; var r = state.Revival;
            return new StateData { baseManaBonus = m.BaseManaBonus, baseArmorPerTurnBonus = m.BaseArmorPerTurnBonus,
                mightBonus = m.MightBonus, handLimitBonus = m.HandLimitBonus, areaBonus = m.AreaBonus,
                rewardRerollsRemaining = m.RewardRerollsRemaining, luckBonus = p.LuckBonus, greedBonus = p.GreedBonus,
                jadeEarned = p.JadeEarned, growthBonus = p.GrowthBonus, amountBonus = a.AmountBonus,
                magnetBonus = a.MagnetBonus, curseBonus = a.CurseBonus, revivalInitialized = r.Initialized,
                revivalCapacityPercent = r.CapacityPercent, revivalInitialPercent = r.InitialPercent,
                revivalAwardedPercent = r.AwardedPercent, revivalSpentLives = r.SpentLives, revivalRemainingPercent = r.RemainingPercent };
        }
        private static DungeonRunStatApplicationState StateFrom(StateData s)
            => new DungeonRunStatApplicationState(new DungeonRunModifierSnapshot(s.baseManaBonus, s.baseArmorPerTurnBonus,
                s.mightBonus, s.handLimitBonus, s.areaBonus, s.rewardRerollsRemaining),
                new RunProgressionSnapshot(s.luckBonus, s.greedBonus, s.jadeEarned, s.growthBonus),
                new RunAdditionalStatSnapshot(s.amountBonus, s.magnetBonus, s.curseBonus),
                new RunRevivalSnapshot(s.revivalInitialized, s.revivalCapacityPercent, s.revivalInitialPercent,
                    s.revivalAwardedPercent, s.revivalSpentLives, s.revivalRemainingPercent));

        // Only this closed DTO family is registered; no Core graph reflection,
        // arbitrary type names, Unity serializer defaults or mutable DTO escape.
        private sealed class EnvelopeData
        {
            public int formatVersion; public string policyVersion;
            public BaselineData baseline; public StateData before; public StateData after;
            public List<RewardData> rewards; public List<ReceiptData> receipts;
        }
        private sealed class BaselineData
        {
            public int baseArmorPerTurn, fengRui, amount, bengLie, ningShen, shouPai, yinJuan, chouPai;
            public int tianYun, lianYu, mingWu, hunHun, faLi, zhaoSha, recovery;
        }
        private sealed class StateData
        {
            public int baseManaBonus, baseArmorPerTurnBonus, mightBonus, handLimitBonus, areaBonus, rewardRerollsRemaining;
            public int luckBonus, greedBonus, jadeEarned, growthBonus, amountBonus, magnetBonus, curseBonus;
            public bool revivalInitialized;
            public int revivalCapacityPercent, revivalInitialPercent, revivalAwardedPercent, revivalSpentLives, revivalRemainingPercent;
        }
        private sealed class RewardData
        {
            public int modifiedStatIndex, sourceStat, modifiedAmount, baseChangeAmount;
            public string modifierBits, productBits;
            public int requestedAmount, mode;
        }
        private sealed class ReceiptData
        {
            public int modifiedStatIndex, sourceStat, mode, requestedAmount, ownedBefore, ownedAfter, actualOwnedDelta;
            public int? effectiveBefore, effectiveAfter, acceptedEffectiveDelta;
            public int baseDelta; public int? currentDelta; public string policyVersion;
        }
    }

    /// <summary>The complete entry values, copied without projecting or recapturing a combat player.</summary>
    internal sealed class DungeonRunStatApplicationBaseline
    {
        private readonly DungeonRunPlayerBaselineSaveData _values;
        public int BaseArmorPerTurn => _values.baseArmorPerTurn;
        public int Might => _values.fengRui;
        public int Amount => _values.amount;
        public int Area => _values.bengLie;
        public int Duration => _values.ningShen;
        public int HandLimit => _values.shouPai;
        public int Magnet => _values.yinJuan;
        public int DrawCount => _values.chouPai;
        public int Luck => _values.tianYun;
        public int Greed => _values.lianYu;
        public int Growth => _values.mingWu;
        public int Revival => _values.hunHun;
        public int Mana => _values.faLi;
        public int Curse => _values.zhaoSha;
        public int Recovery => _values.recovery;
        internal DungeonRunStatApplicationBaseline(DungeonRunPlayerBaselineSaveData values)
        { _values = (values ?? throw new ArgumentNullException(nameof(values))).Clone(); }
        internal DungeonRunPlayerBaselineSaveData ToSaveData() => _values.Clone();
    }

    /// <summary>Original supported tuple evidence, including binary32 bits and index gaps.
    /// The arithmetic checker reuses Core's rule, without inventing omitted tuples,
    /// changing offering admission or claiming a captured source specification.</summary>
    internal sealed class DungeonRunStatApplicationRequest
    {
        public int ModifiedStatIndex { get; }
        public int SourceStat { get; }
        public int ModifiedAmount { get; }
        public int BaseChangeAmount { get; }
        public string ModifierBits { get; }
        public string ProductBits { get; }
        public int RequestedAmount { get; }
        public SideEventStatModifierMode Mode => SideEventStatApplicationRules.DefaultMode(SourceStat);

        internal DungeonRunStatApplicationRequest(int index, int stat, int amount, int baseline,
            string modifierBits, string productBits, int requested)
        {
            ModifiedStatIndex = index; SourceStat = stat; ModifiedAmount = amount; BaseChangeAmount = baseline;
            ModifierBits = modifierBits; ProductBits = productBits; RequestedAmount = requested;
        }
        internal static DungeonRunStatApplicationRequest Capture(SideEventCardStatOfferingReward reward)
        {
            if (reward == null) throw new InvalidOperationException("A source reward is required.");
            return new DungeonRunStatApplicationRequest(reward.ModifiedStatIndex, reward.SourceStat, reward.ModifiedAmount,
                reward.BaseChangeAmount, Bits(reward.Modifier), Bits(reward.Binary32Product), reward.RequestedAmount);
        }
        internal void ValidateArithmetic()
        {
            if (ModifiedStatIndex < 0 || RequestedAmount <= 0) throw new InvalidOperationException("Invalid source request.");
            float modifier = Float(ModifierBits); float product = Float(ProductBits);
            // This is arithmetic replay only; the false FCC input supplies Core's
            // pure formula precondition, and grants no physical-card eligibility.
            var specification = new SideEventCardStatOfferingSpecification(
                new SideEventOfferingSelection(1, true, true, false, false, true),
                new[] { new SideEventStatSetting(SourceStat, 0, modifier, BaseChangeAmount) }, 200, false);
            var computed = SideEventCardStatOfferingRules.Prepare(specification, false, null,
                new[] { new SideEventCardModifiedStat(SourceStat, ModifiedAmount) }).Rewards[0];
            if (Bits(computed.Binary32Product) != Bits(product) || computed.RequestedAmount != RequestedAmount)
                throw new InvalidOperationException("Source requested reward does not match captured tuple arithmetic.");
            SideEventStatApplicationRules.DefaultMode(SourceStat);
        }
        private static string Bits(float value)
            => BitConverter.ToUInt32(BitConverter.GetBytes(value), 0).ToString("x8", CultureInfo.InvariantCulture);
        private static float Float(string bits)
        {
            if (bits == null || bits.Length != 8 || !uint.TryParse(bits, NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out uint raw) || raw.ToString("x8", CultureInfo.InvariantCulture) != bits)
                throw new InvalidOperationException("Binary32 evidence requires canonical lower hex8 bits.");
            float value = BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Nonfinite source evidence.");
            return value;
        }
    }
}
