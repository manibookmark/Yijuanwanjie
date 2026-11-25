using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    /// <summary>One source segment's presentation markers; no second rule transaction lives here.</summary>
    [Serializable]
    public class BossUtilityStepTiming
    {
        public bool resolveImpact = true;
        [Range(0f, 1f)] public float impactNormalized = 0.46f;
        public bool emitSpellRelease;
        [Range(0f, 1f)] public float releaseNormalized;
        [Tooltip("Anchor in the FINAL sliced frame, lower-left UV; not the uncropped source UV.")]
        public Vector2 sourceAnchorNormalized = new Vector2(0.5f, 0.5f);
    }

    [Serializable]
    public sealed class BossUtilityStep : BossUtilityStepTiming
    {
        public BossAnimationClip clip = new BossAnimationClip();
    }

    [Serializable]
    public sealed class BossUtilityVariant
    {
        public string actionId;
        public List<BossUtilityStep> steps = new List<BossUtilityStep>();
    }

    [Serializable]
    public sealed class BossAnimationRuntimeUtilityStepDescriptor : BossUtilityStepTiming
    {
        public BossAnimationRuntimeClipDescriptor clip =
            new BossAnimationRuntimeClipDescriptor();

        public BossUtilityStep CreateStep()
        {
            return new BossUtilityStep
            {
                clip = clip.CreateClip(),
                resolveImpact = resolveImpact,
                impactNormalized = impactNormalized,
                emitSpellRelease = emitSpellRelease,
                releaseNormalized = releaseNormalized,
                sourceAnchorNormalized = sourceAnchorNormalized
            };
        }
    }

    [Serializable]
    public sealed class BossAnimationRuntimeUtilityDescriptor
    {
        public string actionId;
        public List<BossAnimationRuntimeUtilityStepDescriptor> steps =
            new List<BossAnimationRuntimeUtilityStepDescriptor>();
    }

    /// <summary>
    /// Defines a single action transaction's media recipe. The impact must precede (or
    /// share a frame with) its optional release; a continuation never settles rules again.
    /// Pure validation is shared by author profiles, lightweight manifests and tests.
    /// </summary>
    public static class BossUtilitySequenceContract
    {
        public static bool TryValidate(
            string actionId,
            IReadOnlyList<BossUtilityStepTiming> steps,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(actionId) || steps == null || steps.Count == 0)
            {
                error = "Utility action needs a stable actionId and at least one segment.";
                return false;
            }

            int impacts = 0;
            int releases = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                BossUtilityStepTiming step = steps[i];
                if (step == null)
                {
                    error = $"{actionId}: null Utility segment {i}.";
                    return false;
                }
                if (step.resolveImpact)
                {
                    if (!BossAnimationTiming.IsValidNormalizedTime(step.impactNormalized))
                    {
                        error = $"{actionId}: invalid impact time in segment {i}.";
                        return false;
                    }
                    impacts++;
                }
                if (!step.emitSpellRelease)
                    continue;

                releases++;
                if (!BossAnimationTiming.IsValidNormalizedTime(step.releaseNormalized) ||
                    !BossAnimationTiming.IsValidNormalizedTime(step.sourceAnchorNormalized.x) ||
                    !BossAnimationTiming.IsValidNormalizedTime(step.sourceAnchorNormalized.y))
                {
                    error = $"{actionId}: invalid release time or final-frame anchor in segment {i}.";
                    return false;
                }
                if (impacts != 1 || (step.resolveImpact &&
                    step.releaseNormalized < step.impactNormalized))
                {
                    error = $"{actionId}: release must follow the unique rule impact.";
                    return false;
                }
            }
            if (impacts != 1 || releases > 1)
            {
                error = $"{actionId}: expected exactly one rule impact and at most one release.";
                return false;
            }
            return true;
        }
    }
}
