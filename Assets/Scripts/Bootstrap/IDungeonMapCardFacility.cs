using System;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Bootstrap
{
    /// <summary>One reusable map choice lifecycle. Dungeon owns the durable choice
    /// and node completion; implementations own only selection and result views.
    /// Losing a view preserves a pending choice instead of granting or cancelling it.</summary>
    public interface IDungeonMapCardFacility
    {
        LevelInteractionEffectKind EffectKind { get; }
        bool CanBegin { get; }
        bool IsActive { get; }
        bool TryBegin(string nodeId, Action<LevelInteractionReceipt> onCommitted,
            Action onCancelled, out string error);
        void PlayCommitted(Action onClosed);
        void CloseSilently();
    }
}
