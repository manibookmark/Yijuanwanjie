using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// BOSS 动作时间轴的唯一换算入口。源帧率仍描述素材本身；统一动作倍率只
    /// 作用于 Attack/Defend，并同时驱动帧推进、结算时点与真实战斗音频音调。
    /// </summary>
    public static class BossAnimationTiming
    {
        public const float DefaultActionPlaybackSpeed = 1f;
        public const float MinimumActionPlaybackSpeed = 0.5f;
        public const float MaximumActionPlaybackSpeed = 2f;
        public const float DefaultDefendImpactNormalized = 0.46f;

        public static float ResolveActionPlaybackSpeed(
            BossAnimationProfile profile)
        {
            float configured = profile != null
                ? profile.actionPlaybackSpeed
                : DefaultActionPlaybackSpeed;
            if (configured <= 0f || float.IsNaN(configured) ||
                float.IsInfinity(configured))
            {
                return DefaultActionPlaybackSpeed;
            }

            return Mathf.Clamp(
                configured,
                MinimumActionPlaybackSpeed,
                MaximumActionPlaybackSpeed);
        }

        public static float GetFramesPerSecond(
            BossAnimationProfile profile,
            BossAnimationClip clip,
            bool isAction)
        {
            float sourceFramesPerSecond = Mathf.Max(
                0.1f,
                clip?.framesPerSecond ?? 0.1f);
            return isAction
                ? sourceFramesPerSecond *
                  ResolveActionPlaybackSpeed(profile)
                : sourceFramesPerSecond;
        }

        public static float GetDuration(
            BossAnimationProfile profile,
            BossAnimationClip clip,
            bool isAction)
        {
            if (clip == null)
                return 0f;
            return Mathf.Max(1, clip.frameCount) /
                   GetFramesPerSecond(profile, clip, isAction);
        }

        public static int GetFrameIndex(
            BossAnimationProfile profile,
            BossAnimationClip clip,
            bool isAction,
            float elapsedSeconds,
            bool loop)
        {
            if (clip == null)
                return 0;

            int frameCount = Mathf.Max(1, clip.frameCount);
            int index = Mathf.Max(
                0,
                Mathf.FloorToInt(
                    Mathf.Max(0f, elapsedSeconds) *
                    GetFramesPerSecond(profile, clip, isAction)));
            return loop
                ? index % frameCount
                : Mathf.Min(index, frameCount - 1);
        }

        public static float GetSettlementTime(
            BossAnimationProfile profile,
            BossAnimationClip clip,
            float settlementNormalized)
        {
            return GetDuration(profile, clip, isAction: true) *
                   Mathf.Clamp01(settlementNormalized);
        }

        public static bool IsValidNormalizedTime(float value)
        {
            return value >= 0f && value <= 1f &&
                   !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static float ResolveDefendImpactNormalized(
            BossAnimationProfile profile)
        {
            float configured = profile != null
                ? profile.defendImpactNormalized
                : DefaultDefendImpactNormalized;
            return IsValidNormalizedTime(configured)
                ? configured
                : DefaultDefendImpactNormalized;
        }

        public static float GetActionAudioPitch(
            BossAnimationProfile profile)
        {
            return ResolveActionPlaybackSpeed(profile);
        }
    }
}
