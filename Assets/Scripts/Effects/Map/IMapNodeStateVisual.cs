using System;
using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    /// <summary>
    /// Deep visual contract for a map node whose authored prefab owns its
    /// content-specific state presentation. Map rules provide semantic states
    /// only; particle topology, sprite treatment and completion timing stay
    /// inside the visual module. MapPresentation may layer one group-level
    /// availability cue above it so every interactable node shares the same
    /// Available / Hovered affordance.
    /// </summary>
    public interface IMapNodeStateVisual
    {
        MapVfxState State { get; }

        /// <summary>
        /// Explicit exception for a spent landmark that remains in the scene.
        /// One-use visuals otherwise disappear with their pointer target.
        /// </summary>
        bool RetainsCompletedVisual { get; }

        void SetState(MapVfxState state, bool immediate = true);

        bool PlayCompletion(Action onComplete = null);
    }

    public interface IMapNodeInteractionAnchor
    {
        bool TryGetInteractionWorldAnchor(out Vector3 worldPosition);
    }

    /// <summary>A completed facility can expose a separately saved pickup.</summary>
    public interface IMapNodePickupStateVisual
    {
        void SetPickupAvailable(bool available);
    }
}
