using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// A committed Copy seal slot, published after all its cards enter the hand.
    /// It is not CardPlayed or CardsDrawn. Consumers may present/own the new cards,
    /// but their absence never delays rule completion. Slot indexes are zero-based.
    /// </summary>
    public sealed class CombatCardsCopiedEvent
    {
        public int SourceCardInstanceId { get; }
        public string SealContentId { get; }
        public int SealSlotIndex { get; }
        public IReadOnlyList<CardConfig> Cards { get; }
        public IReadOnlyList<int> CardInstanceIds { get; }
        public int Count => Cards.Count;

        internal CombatCardsCopiedEvent(CardConfig source, CardSealPostPlayAction action,
            IReadOnlyList<CardConfig> cards)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            SourceCardInstanceId = source.runtimeInstanceId;
            SealContentId = action.SealContentId;
            SealSlotIndex = action.SealSlotIndex;
            var committed = new List<CardConfig>(cards);
            Cards = committed.AsReadOnly();
            var identities = new List<int>(committed.Count);
            foreach (CardConfig card in committed) identities.Add(card.runtimeInstanceId);
            CardInstanceIds = identities.AsReadOnly();
        }
    }
}
