using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 一名驻场英灵的透明序列、专属反馈与局部定位配置。配置只
    /// 描述角色表现，不拥有凝神、触发次数或退场规则。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewCrawlerResidentPresentation",
        menuName = "万界长卷/战斗/驻场英灵表现")]
    public sealed class CrawlerResidentPresentationProfile : ScriptableObject
    {
        public const float MinHoverAuraOffset = -160f;
        public const float MaxHoverAuraOffset = 160f;

        [Tooltip("权威英灵 contentId；不得使用显示名称关联")]
        public string crawlerContentId;

        [Header("透明序列图集")]
        public Texture2D frameAtlas;
        [Min(1)] public int frameCount = 48;
        [Min(1)] public int atlasColumns = 8;
        [Min(1)] public int atlasRows = 6;
        [Min(1f)] public float framesPerSecond = 12f;
        [Tooltip("图集中驻场循环的首帧；可复用显化视频末帧保持人物稳定。")]
        [Min(0)] public int atlasFirstFrame;
        [Tooltip("轻微淡金沙粒驻场层；不改变角色姿态或凝神规则。")]
        public bool sandPresence;
        [Tooltip("往返播放驻场帧，避免源片首尾姿态不同造成跳变；其他英灵默认普通循环。")]
        public bool pingPongLoop;

        [Header("召唤反馈")]
        [Tooltip("新权威驻场实例首次显化时播放的语音 cue；初始装配与调参重播不触发。")]
        public string summonVoiceCue;

        [Header("触发反馈")]
        [Tooltip("驻场增益触发时播放的语音 cue；为空时保持安静。")]
        public string triggerVoiceCue;

        [Header("身体范围与 Hover 灵气定位")]
        [Tooltip("单帧内的身体包络，左下为(0,0)，右上为(1,1)。包含头脚与躯干，不以武器或透明留白决定中心；所有循环帧共用。也用作鼠标命中范围。")]
        public Rect residentBodyBounds = new Rect(0.2f, 0.06f, 0.6f, 0.88f);

        [Tooltip("1920×1080 参考分辨率下，灵气相对已标定身体中心的微调。基础定位由身体范围决定。")]
        public Vector2 hoverAuraOffset = Vector2.zero;

        public float FrameAspect
        {
            get
            {
                if (frameAtlas == null || atlasColumns <= 0 || atlasRows <= 0)
                    return 1f;
                float width = frameAtlas.width / (float)atlasColumns;
                float height = frameAtlas.height / (float)atlasRows;
                return height > 0f ? width / height : 1f;
            }
        }

        public int GetLoopFrame(float elapsed)
        {
            int count = Mathf.Max(1, frameCount);
            int period = pingPongLoop && count > 1 ? (count - 1) * 2 : count;
            int frame = Mathf.FloorToInt(Mathf.Max(0f, elapsed) * framesPerSecond) % period;
            return frame < count ? frame : period - frame;
        }

        /// <summary>把左上起始的帧序号换算为 Unity 左下原点 UV。</summary>
        public Rect GetUvRect(int frameIndex)
        {
            int safeColumns = Mathf.Max(1, atlasColumns);
            int safeRows = Mathf.Max(1, atlasRows);
            int safeCount = Mathf.Max(1, frameCount);
            int index = Mathf.Max(0, atlasFirstFrame) + Mathf.Clamp(frameIndex, 0, safeCount - 1);
            int column = index % safeColumns;
            int rowFromTop = index / safeColumns;
            float width = 1f / safeColumns;
            float height = 1f / safeRows;
            return new Rect(
                column * width,
                1f - (rowFromTop + 1) * height,
                width,
                height);
        }

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(crawlerContentId))
                return Fail("驻场英灵 crawlerContentId 不能为空。", out error);
            if (frameAtlas == null)
                return Fail($"{crawlerContentId} 缺少透明序列图集。", out error);
            if (frameCount <= 0 || atlasColumns <= 0 || atlasRows <= 0)
                return Fail($"{crawlerContentId} 的帧数与图集网格必须大于 0。", out error);
            if (atlasFirstFrame < 0 || atlasFirstFrame + frameCount > atlasColumns * atlasRows)
                return Fail($"{crawlerContentId} 的帧数超出图集容量。", out error);
            if (frameAtlas.width % atlasColumns != 0 ||
                frameAtlas.height % atlasRows != 0)
            {
                return Fail($"{crawlerContentId} 的图集尺寸不能被网格整除。", out error);
            }
            if (framesPerSecond <= 0f)
                return Fail($"{crawlerContentId} 的帧率必须大于 0。", out error);
            if (!IsFinite(residentBodyBounds.x) || !IsFinite(residentBodyBounds.y) ||
                !IsFinite(residentBodyBounds.width) || !IsFinite(residentBodyBounds.height) ||
                residentBodyBounds.width <= 0f || residentBodyBounds.height <= 0f ||
                residentBodyBounds.xMin < 0f || residentBodyBounds.yMin < 0f ||
                residentBodyBounds.xMax > 1f || residentBodyBounds.yMax > 1f)
                return Fail($"{crawlerContentId} 的身体范围必须是单帧内的有效归一化矩形。", out error);
            if (hoverAuraOffset.x < MinHoverAuraOffset ||
                hoverAuraOffset.x > MaxHoverAuraOffset ||
                hoverAuraOffset.y < MinHoverAuraOffset ||
                hoverAuraOffset.y > MaxHoverAuraOffset)
            {
                return Fail(
                    $"{crawlerContentId} 的 Hover 灵气 X/Y 偏移必须位于 " +
                    $"{MinHoverAuraOffset}–{MaxHoverAuraOffset} 像素。",
                    out error);
            }

            error = null;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>按稳定英灵 ID 解析驻场表现，并拒绝重复或损坏的配置。</summary>
    public static class CrawlerResidentPresentationCatalog
    {
        private const string ResourceRoot = "Content/CrawlerResidents";
        private static Dictionary<string, CrawlerResidentPresentationProfile>
            _profiles;

        public static bool TryGet(
            string crawlerContentId,
            out CrawlerResidentPresentationProfile profile)
        {
            if (string.IsNullOrWhiteSpace(crawlerContentId))
            {
                profile = null;
                return false;
            }

            EnsureLoaded();
            return _profiles.TryGetValue(crawlerContentId, out profile);
        }

        public static void ClearCache()
        {
            _profiles = null;
        }

        private static void EnsureLoaded()
        {
            if (_profiles != null)
                return;

            _profiles = new Dictionary<string, CrawlerResidentPresentationProfile>(
                StringComparer.OrdinalIgnoreCase);
            CrawlerResidentPresentationProfile[] profiles =
                Resources.LoadAll<CrawlerResidentPresentationProfile>(ResourceRoot);
            for (int i = 0; i < profiles.Length; i++)
            {
                CrawlerResidentPresentationProfile profile = profiles[i];
                if (profile == null)
                    continue;
                if (!profile.TryValidate(out string error))
                    throw new InvalidOperationException(error);
                if (!_profiles.TryAdd(profile.crawlerContentId, profile))
                {
                    throw new InvalidOperationException(
                        $"驻场英灵表现 contentId 重复：{profile.crawlerContentId}。");
                }
            }
        }
    }
}
