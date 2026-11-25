using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Two native colors, one persistent non-hero instance per key. Empty Unity
    /// shells are absent; version 1 explicitly enables payment. Trigger colors,
    /// Wild continuation and names never substitute for the authored card color.
    /// </summary>
    [Serializable]
    public sealed class LevelInteractionDoubleKeyContract
    {
        public int version;
        public CardColor firstColor;
        public CardColor secondColor;
        public bool IsSpecified => version != 0 || firstColor != 0 || secondColor != 0;

        internal void Validate(LevelInteractionEffect effect, List<string> errors)
        {
            if (version != 1 || !IsKeyColor(firstColor) || !IsKeyColor(secondColor) || firstColor == secondColor)
                errors.Add($"双钥 {effect.effectId} 必须配置 version=1 和两个不同的非英灵本色。");
            if (effect.kind != LevelInteractionEffectKind.ChooseSeal ||
                !string.IsNullOrWhiteSpace(effect.sealRewardPoolId) || effect.cashOutJade <= 0)
                errors.Add($"双钥 {effect.effectId} 必须使用带化玉出口的固定 ChooseSeal 奖励。");
        }

        private static bool IsKeyColor(CardColor color)
            => Enum.IsDefined(typeof(CardColor), color) && color != CardColor.Jin && color != CardColor.Yang;
    }

    public readonly struct DungeonRunDoubleKeyCandidate
    {
        public int RuntimeInstanceId { get; }
        public string ContentId { get; }
        internal DungeonRunDoubleKeyCandidate(DungeonRunCardSaveData card)
        { RuntimeInstanceId = card.runtimeInstanceId; ContentId = card.contentId; }
    }

    /// <summary>Read-only payment menu. A paid box resumes Interactions, even with an empty deck.</summary>
    public sealed class DungeonRunDoubleKeyPaymentOffer
    {
        public CardColor FirstColor { get; }
        public CardColor SecondColor { get; }
        public IReadOnlyList<DungeonRunDoubleKeyCandidate> FirstCandidates { get; }
        public IReadOnlyList<DungeonRunDoubleKeyCandidate> SecondCandidates { get; }
        public bool IsPaid { get; }
        public bool CanPay => !IsPaid && FirstCandidates.Count > 0 && SecondCandidates.Count > 0;
        public bool AllowsLeave => !IsPaid;
        internal DungeonRunDoubleKeyPaymentOffer(LevelInteractionDoubleKeyContract contract,
            List<DungeonRunDoubleKeyCandidate> first, List<DungeonRunDoubleKeyCandidate> second, bool paid)
        {
            FirstColor = contract.firstColor; SecondColor = contract.secondColor;
            FirstCandidates = first.AsReadOnly(); SecondCandidates = second.AsReadOnly(); IsPaid = paid;
        }
    }

    /// <summary>Payment acknowledgement, separate from the later seal/cash-out receipt.</summary>
    public readonly struct DungeonRunDoubleKeyPaymentReceipt
    {
        public string FloorContentId { get; }
        public string NodeContentId { get; }
        public string EffectId { get; }
        public int FirstRuntimeInstanceId { get; }
        public int SecondRuntimeInstanceId { get; }
        public bool NewlyPaid { get; }
        internal DungeonRunDoubleKeyPaymentReceipt(DungeonRunDoubleKeyPaymentEntry entry, bool newlyPaid)
        {
            FloorContentId = entry.floorContentId; NodeContentId = entry.nodeContentId; EffectId = entry.effectId;
            FirstRuntimeInstanceId = entry.firstRuntimeInstanceId;
            SecondRuntimeInstanceId = entry.secondRuntimeInstanceId; NewlyPaid = newlyPaid;
        }
    }

    [Serializable]
    internal sealed class DungeonRunDoubleKeyPaymentSaveData
    {
        // Run schema 17 requires this envelope; schema <=16 migrates to an empty
        // ledger, never to an implicitly paid box. Formation schema 16 is unchanged.
        public int version;
        public List<DungeonRunDoubleKeyPaymentEntry> payments;
    }

    [Serializable]
    internal sealed class DungeonRunDoubleKeyPaymentEntry
    {
        public string floorContentId;
        public string nodeContentId;
        public string effectId;
        public CardColor firstColor;
        public CardColor secondColor;
        public int firstRuntimeInstanceId;
        public int secondRuntimeInstanceId;
        public string firstCardContentId;
        public string secondCardContentId;
    }
}
