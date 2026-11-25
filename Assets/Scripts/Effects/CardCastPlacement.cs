using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns the hand-card clearance and camera framing policy for brief support
    /// casts. Effect players own their transforms and supply actual body bounds;
    /// receipt-backed HUD/resident destinations are never repositioned here.
    /// </summary>
    internal static class CardCastPlacement
    {
        /// <summary>
        /// All support bodies use a 204-pixel longest edge on a 1080-high screen.
        /// Callers supply their real body bounds; ornament reach is only for
        /// placement and must never determine the body's display size.
        /// </summary>
        public static float SupportScale(Vector3 source, VfxProfile profile, Vector2 bodySize)
        {
            float authoredScale = Mathf.Clamp(profile != null ? profile.selfEffectScale : 1f, 0.4f, 2f);
            Camera camera = Camera.main;
            if (camera == null || camera.pixelHeight <= 0 || camera.pixelWidth <= 0) return authoredScale;
            Vector3 screen = camera.WorldToScreenPoint(source);
            if (screen.z <= 0f) return authoredScale;
            float pixels = Mathf.Min(204f * Mathf.Max(0.1f, Screen.height / 1080f),
                Mathf.Min(camera.pixelWidth * 0.24f, camera.pixelHeight * 0.28f));
            Vector3 edge = camera.ScreenToWorldPoint(screen + Vector3.up * pixels);
            float longestEdge = Mathf.Max(0.1f, Mathf.Max(bodySize.x, bodySize.y));
            return Vector3.Distance(camera.ScreenToWorldPoint(screen), edge) * authoredScale / longestEdge;
        }

        public static Vector3 AboveCard(Vector3 source, VfxProfile profile = null,
            float scale = 1f, Vector3? bodyReach = null)
        {
            if (profile != null && (profile.selfEffectStyle == CardSelfEffectStyle.ArmorWard ||
                profile.selfEffectStyle == CardSelfEffectStyle.VitalityPill ||
                profile.selfEffectStyle == CardSelfEffectStyle.ManaScript))
                return source; // Character armour and HP/MP arrivals keep their anchors.

            Vector3 offset = profile != null ? profile.selfEffectOffset : Vector3.zero;
            Vector3 candidate = source + offset;
            // Conservative local half-width, lower reach and upper reach. These
            // cover the silhouette and local ornament, not the body sizing policy.
            Vector3 reach = (bodyReach ?? Reach(
                profile != null ? profile.selfEffectStyle : CardSelfEffectStyle.None)) *
                Mathf.Max(0.4f, scale);
            Camera camera = Camera.main;
            if (camera == null || camera.pixelRect.width <= 0f || camera.pixelRect.height <= 0f)
                return source + new Vector3(offset.x, Mathf.Max(offset.y, 1.65f + reach.y), offset.z);

            Vector3 sourceView = camera.WorldToViewportPoint(source);
            Vector3 view = camera.WorldToViewportPoint(candidate);
            if (sourceView.z <= 0f || view.z <= 0f) return candidate;
            float unitHeight = Mathf.Abs(camera.WorldToViewportPoint(source + Vector3.up).y - sourceView.y);
            float unitWidth = Mathf.Abs(camera.WorldToViewportPoint(source + Vector3.right).x - sourceView.x);
            float below = unitHeight * reach.y;
            float above = unitHeight * reach.z;
            float halfWidth = unitWidth * reach.x;
            float referencePixels = profile != null ? profile.castingArtifactCardClearancePixels : 160f;
            float clearance = (referencePixels + 18f) * Mathf.Max(0.1f, Screen.height / 1080f) /
                camera.pixelRect.height;
            float minimum = Mathf.Max(sourceView.y + clearance, 0.36f) + below;
            float maximum = Mathf.Max(below + 0.02f, 0.97f - above);
            // Explicit profile offsets are candidates, not a second fixed lift.
            // Preserve depth and use viewport coordinates for inset cameras too.
            view.y = Mathf.Clamp(Mathf.Max(view.y, minimum), below + 0.02f, maximum);
            float inset = Mathf.Min(0.48f, halfWidth + 0.02f);
            view.x = Mathf.Clamp(view.x, inset, 1f - inset);
            return camera.ViewportToWorldPoint(view);
        }

        private static Vector3 Reach(CardSelfEffectStyle style)
        {
            switch (style)
            {
                case CardSelfEffectStyle.QuantityMechanism:
                    return new Vector3(0.98f, 0.92f, 1.12f);
                case CardSelfEffectStyle.WindOrb:
                    return new Vector3(0.63f, 0.64f, 0.83f);
                case CardSelfEffectStyle.ManaBook:
                    return new Vector3(0.62f, 0.42f, 0.4f);
                case CardSelfEffectStyle.SandSpirit:
                case CardSelfEffectStyle.DealScript:
                    return new Vector3(0.55f, 0.55f, 0.55f);
                case CardSelfEffectStyle.TianjiMirror:
                    return new Vector3(1.1f, 1.1f, 1.1f);
                case CardSelfEffectStyle.JadeSpiral:
                case CardSelfEffectStyle.FortuneBloom:
                    return new Vector3(1.3f, 1.25f, 1.4f);
                default:
                    return new Vector3(1.3f, 0.65f, 1.15f);
            }
        }
    }
}
