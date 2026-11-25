using System;
using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// A source-video close-up candidate, separate from ordinary battlefield
    /// clips. Resource paths keep its large atlases out of the initial load.
    /// Off-screen close-up framing is intentional; no missing pixels are rebuilt.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBossAttackCloseup", menuName = "万界长卷/战斗/BOSS攻击近景候选")]
    public sealed class BossAttackCloseupProfile : ScriptableObject
    {
        public const string DefaultResourcePath = "BossCloseups/g4-frontal-thrust-candidate";

        public string contentId;
        public string actionId;
        public string[] frameAtlasResourcePaths = Array.Empty<string>();
        public string audioResourcePath;
        [Min(1)] public int frameCount = 107;
        [Min(1)] public int atlasColumns = 6;
        [Min(1)] public int atlasRows = 3;
        [Min(0.1f)] public float framesPerSecond = 24f;
        [Min(0)] public int hitFrame = 32;
        [Range(0f, 1f)] public float audioVolume = 1f;

        [Header("Source-canvas framing (top-origin coordinates)")]
        [Min(1f)] public float closeupCanvasScale = 1.04f;
        [Range(0f, 1f)] public float sourceFootY = 0.878f;
        [Tooltip("枪尖接触点在完整源画布中的UV；左下角为(0,0)。")]
        public Vector2 impactAnchorSourceUv = new Vector2(0.5824f, 0.4063f);
        [Min(0)] public int approachStartFrame = 2;
        [Min(1)] public int approachEndFrame = 10;
        [Min(1)] public int holdThroughFrame = 81;
        [Min(1)] public int returnEndFrame = 99;
        [Range(0f, 1f)] public float backgroundDim = 0.38f;
        public int backgroundSortingOrder = 90;
        public int actorSortingOrder = 92;

        public float Duration => frameCount / framesPerSecond;

        public bool TryValidate(out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(contentId) || string.IsNullOrWhiteSpace(actionId) ||
                string.IsNullOrWhiteSpace(audioResourcePath))
                error = "Close-up requires stable content/action IDs and source audio.";
            else if (frameCount <= 0 || atlasColumns <= 0 || atlasRows <= 0 ||
                     !Finite(framesPerSecond) || framesPerSecond <= 0f || hitFrame < 0 || hitFrame >= frameCount)
                error = "Close-up frame/timing parameters are invalid.";
            else if (frameAtlasResourcePaths == null || frameAtlasResourcePaths.Length == 0 ||
                     (long)frameAtlasResourcePaths.Length * atlasColumns * atlasRows < frameCount)
                error = "Close-up atlas pages do not contain every source frame.";
            else if (!Finite(closeupCanvasScale) || closeupCanvasScale < 1f ||
                     !Normalized(sourceFootY) || !Normalized(impactAnchorSourceUv.x) ||
                     !Normalized(impactAnchorSourceUv.y) ||
                     !Normalized(backgroundDim) || !Normalized(audioVolume))
                error = "Close-up canvas or audio parameters are invalid.";
            else if (approachStartFrame < 0 || approachEndFrame <= approachStartFrame ||
                     holdThroughFrame < approachEndFrame || returnEndFrame <= holdThroughFrame ||
                     returnEndFrame >= frameCount || actorSortingOrder <= backgroundSortingOrder + 1)
                error = "Close-up approach/hold/return or sorting parameters are invalid.";
            if (error != null) return false;
            foreach (string path in frameAtlasResourcePaths)
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "Close-up atlas page path is empty.";
                    return false;
                }
            return true;
        }

        public float GetCloseupWeight(float frame)
        {
            if (frame <= approachEndFrame)
                return Smooth((frame - approachStartFrame) / (approachEndFrame - approachStartFrame));
            if (frame <= holdThroughFrame) return 1f;
            return 1f - Smooth((frame - holdThroughFrame) / (returnEndFrame - holdThroughFrame));
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Normalized(float value) => Finite(value) && value >= 0f && value <= 1f;
    }
}
