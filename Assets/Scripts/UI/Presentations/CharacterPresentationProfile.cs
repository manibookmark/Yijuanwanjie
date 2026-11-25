using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI.Presentations
{
    public static class CharacterPresentationIds
    {
        public const string ScrollSpiritLevelMilestone =
            "presentation.character.scroll-spirit.level-milestone.v1";
        public const string ScrollSpiritKeyRelicReveal =
            "presentation.character.scroll-spirit.key-relic-reveal.v1";
        public const string WukongTutorialUnlock =
            "presentation.character.wukong.tutorial-unlock.v1";
        public const string TangSanzangG1F4Unlock =
            "presentation.character.tangsanzang.g1f4-unlock.v1";
    }

    /// <summary>
    /// 共用完整演出的内容变体 ID。变体主要选择台词、语音和独立重复记录；
    /// 场景语义确有差异时可显式覆盖对话框尺寸与退场粒子。
    /// </summary>
    public static class CharacterPresentationVariantIds
    {
        public const string TutorialHuaguoshanEntry =
            "presentation.variant.scroll-spirit.tutorial-huaguoshan.entry.v1";
        public const string G1L1Entry =
            "presentation.variant.scroll-spirit.g1l1.entry.v1";
        public const string G2F1Entry =
            "presentation.variant.scroll-spirit.g2f1.entry.v1";
        public const string G3F1Entry =
            "presentation.variant.scroll-spirit.g3f1.entry.v1";
        public const string G4F1Entry =
            "presentation.variant.scroll-spirit.g4f1.entry.v1";
        public const string G1F4Entry =
            "presentation.variant.scroll-spirit.g1f4.entry.v1";
        public const string TutorialHuaguoshanHubAwakening =
            "presentation.variant.scroll-spirit.tutorial-huaguoshan.hub-awakening.v1";
    }

    public enum CharacterPresentationPlacement
    {
        Left,
        Center,
        Right
    }

    public enum CharacterPresentationPlaybackMode
    {
        OnceHoldLast,
        Loop
    }

    public enum CharacterPresentationCompletionMode
    {
        AutoDismiss,
        RequireAcknowledgement,
        AcknowledgeAndPersist
    }

    public enum CharacterPresentationPanelStyle
    {
        CompactCompanion,
        HeroArrival
    }

    public enum CharacterPresentationEffectPreset
    {
        None,
        ScrollSpiritMaterialize
    }

    public enum CharacterPresentationRepeatMode
    {
        Always,
        OncePerRun,
        /// <summary>存档级一次：完成后写入存档，删存档后才会重新播放。</summary>
        OncePerSave
    }

    public enum CharacterPresentationInteractionMode
    {
        /// <summary>阻断下层输入，并由角色演出协调地图交互锁。</summary>
        Modal,
        /// <summary>与奖励窗等上层反馈并行展示，不接管其点击或地图交互锁。</summary>
        PassiveOverlay
    }

    public enum CharacterPresentationHintTiming
    {
        /// <summary>台词与语音全部结束后才显示确认提示（默认）。</summary>
        AfterDialogueAndVoice,
        /// <summary>演出动画开始即显示确认提示；点击生效仍等待语音结束。</summary>
        WithAnimation
    }

    public enum CharacterPresentationExitEffectOverride
    {
        Inherit,
        FadeOnly
    }

    /// <summary>
    /// 一句角色演出台词。正文、停留时间和音频都由内容配置表达，UI 不解析或播放音频。
    /// </summary>
    [Serializable]
    public sealed class CharacterPresentationLine
    {
        public string speakerName;
        [TextArea(1, 4)] public string text;
        [Min(0.2f)] public float visibleSeconds = 3.2f;
        [Tooltip("交给外部音频桥接器的稳定 cue ID；音频尚未交付时可留空")]
        public string audioCueId;
    }

    /// <summary>
    /// 同一完整演出中的内容变体。默认只拥有台词、配音和独立重复记录键；
    /// 只有场景语义确实不同的退场粒子与框体尺寸允许显式覆盖。
    /// </summary>
    [Serializable]
    public sealed class CharacterPresentationVariant
    {
        [Tooltip("调用方传入的稳定内容 ID，例如遗物 contentId")]
        public string variantId;
        [Tooltip("该内容独立的一次性记录键；可沿用旧演出 ID 兼容已有存档")]
        public string repeatKey;
        [Tooltip("可选对话框宽高覆盖；两个分量均为 0 时沿用完整演出的统一尺寸")]
        public Vector2 dialoguePanelSizeOverride;
        [Tooltip("Inherit 沿用统一退场粒子；FadeOnly 仅原地淡出，由场景专用特效接管")]
        public CharacterPresentationExitEffectOverride exitEffectOverride;
        public List<CharacterPresentationLine> lines =
            new List<CharacterPresentationLine>();

        public Vector2 ResolveDialoguePanelSize(Vector2 sharedSize)
        {
            return dialoguePanelSizeOverride.x > 0f &&
                   dialoguePanelSizeOverride.y > 0f
                ? dialoguePanelSizeOverride
                : sharedSize;
        }
    }

    /// <summary>
    /// 透明角色序列演出的静态配置。播放器统一持有帧、台词、关闭策略和局部特效生命周期；
    /// 调用方通过稳定 contentId 选择演出；复用同一完整演出时再传入稳定内容变体 ID。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewCharacterPresentation",
        menuName = "万界长卷/角色演出配置")]
    public sealed class CharacterPresentationProfile : ScriptableObject
    {
        [Tooltip("稳定配置 ID；调用方不得使用人物显示名建立关联")]
        public string contentId;
        [Tooltip("HeroArrival 对应的稳定英灵 contentId；缩略演出等非剧情入口按此解析")]
        public string subjectContentId;

        [Header("透明序列")]
        public Texture2D frameAtlas;
        [Min(1)] public int frameCount = 60;
        [Min(1)] public int atlasColumns = 12;
        [Min(1)] public int atlasRows = 5;
        [Min(1f)] public float framesPerSecond = 12f;
        public CharacterPresentationPlaybackMode playbackMode =
            CharacterPresentationPlaybackMode.OnceHoldLast;

        [Header("演出策略")]
        public CharacterPresentationCompletionMode completionMode =
            CharacterPresentationCompletionMode.RequireAcknowledgement;
        [Tooltip("确认提示显示时机：默认台词与语音结束后；WithAnimation 为动画开始即显示，点击仍需等语音结束")]
        public CharacterPresentationHintTiming hintTiming =
            CharacterPresentationHintTiming.AfterDialogueAndVoice;
        public CharacterPresentationRepeatMode repeatMode =
            CharacterPresentationRepeatMode.Always;
        public CharacterPresentationInteractionMode interactionMode =
            CharacterPresentationInteractionMode.Modal;
        public CharacterPresentationPanelStyle panelStyle =
            CharacterPresentationPanelStyle.HeroArrival;
        public CharacterPresentationEffectPreset effectPreset =
            CharacterPresentationEffectPreset.None;
        [Min(0f)] public float enterDuration = 0.55f;
        [Min(0f)] public float panelFadeDuration = 0.18f;
        [Min(0f)] public float exitDuration = 0.38f;
        [Tooltip("字幕计时结束后允许继续等待语音的最长秒数；防止异常音频状态永久阻断交互")]
        [Min(0f)] public float maxVoiceWaitAfterDialogue = 8f;

        [Header("首帧碎片聚散（effectPreset=ScrollSpiritMaterialize 时生效）")]
        [Tooltip("用首帧切分的碎片数量；0 = 使用默认 96")]
        [Min(0)] public int materializationParticleCount = 96;
        [Tooltip("碎片在拼合状态的尺寸缩放；1 = 无缝拼回原图")]
        [Min(0.1f)] public float materializationSizeScale = 1f;
        [Tooltip("碎片聚散速度缩放；1 = 与入场/退场时长一致，2 = 快一倍")]
        [Min(0.1f)] public float materializationSpeedScale = 1f;

        [Header("布局")]
        public CharacterPresentationPlacement placement =
            CharacterPresentationPlacement.Center;
        [Min(64f)] public float displayHeight = 512f;
        public Vector2 anchoredOffset;
        [Tooltip("相对面板样式默认锚点的局部偏移；用于避让奖励主体等并行 UI")]
        public Vector2 dialoguePanelOffset;
        [Tooltip("对话框宽高；两个分量都为 0 时使用面板样式默认值")]
        public Vector2 dialoguePanelSize;
        [Min(12)] public int dialogueFontSize = 27;
        [Min(12)] public int speakerFontSize = 24;
        [Min(12)] public int dismissHintFontSize = 17;
        [Tooltip("仅控制视觉底色；即使完全透明，演出根节点仍会阻断地图点击")]
        public Color backdropColor = Color.clear;

        [Header("面板文字与音频挂点")]
        [Tooltip("例如“齐天大圣 · 孙悟空”；不再单独显示屏幕顶部标题")]
        public string displayTitle;
        public string dismissHint = "点击继续";
        [Tooltip("演出开始时交给外部音频桥接器的稳定 cue ID")]
        public string openingAudioCueId;
        public List<CharacterPresentationLine> lines =
            new List<CharacterPresentationLine>();
        [Tooltip("可选内容变体；只保存台词、配音与独立重复记录，不复制演出参数")]
        public List<CharacterPresentationVariant> variants =
            new List<CharacterPresentationVariant>();

        public bool RequiresAcknowledgement =>
            completionMode != CharacterPresentationCompletionMode.AutoDismiss;

        public bool PersistsAfterAcknowledgement =>
            completionMode ==
            CharacterPresentationCompletionMode.AcknowledgeAndPersist;

        public bool BlocksUnderlyingInteraction =>
            interactionMode == CharacterPresentationInteractionMode.Modal;

        public CharacterPresentationPanelStyle ResolvedPanelStyle =>
            panelStyle;
        public Texture2D ResolvedFrameAtlas =>
            frameAtlas;
        public int ResolvedFrameCount =>
            frameCount;
        public int ResolvedAtlasColumns =>
            atlasColumns;
        public int ResolvedAtlasRows =>
            atlasRows;
        public float ResolvedFramesPerSecond =>
            framesPerSecond;
        public CharacterPresentationPlaybackMode ResolvedPlaybackMode =>
            playbackMode;
        public CharacterPresentationEffectPreset ResolvedEffectPreset =>
            effectPreset;
        public float ResolvedEnterDuration =>
            enterDuration;
        public float ResolvedPanelFadeDuration =>
            panelFadeDuration;
        public float ResolvedExitDuration =>
            exitDuration;
        public int ResolvedMaterializationParticleCount =>
            materializationParticleCount;
        public float ResolvedMaterializationSizeScale =>
            materializationSizeScale;
        public float ResolvedMaterializationSpeedScale =>
            materializationSpeedScale;
        public CharacterPresentationPlacement ResolvedPlacement =>
            placement;
        public float ResolvedDisplayHeight =>
            displayHeight;
        public Vector2 ResolvedAnchoredOffset =>
            anchoredOffset;
        public Vector2 ResolvedDialoguePanelOffset =>
            dialoguePanelOffset;
        public Color ResolvedBackdropColor =>
            backdropColor;
        public int ResolvedDialogueFontSize =>
            Mathf.Max(12, dialogueFontSize);
        public int ResolvedSpeakerFontSize =>
            Mathf.Max(12, speakerFontSize);
        public int ResolvedDismissHintFontSize =>
            Mathf.Max(12, dismissHintFontSize);

        public float SequenceDuration =>
            ResolvedFrameCount / Mathf.Max(1f, ResolvedFramesPerSecond);

        public Vector2 ResolvedDialoguePanelSize
        {
            get
            {
                if (dialoguePanelSize.x > 0f && dialoguePanelSize.y > 0f)
                    return dialoguePanelSize;

                return ResolvedPanelStyle ==
                       CharacterPresentationPanelStyle.CompactCompanion
                    ? new Vector2(620f, 150f)
                    : new Vector2(800f, 184f);
            }
        }

        public float DialogueDuration
        {
            get
            {
                if (lines == null || lines.Count == 0)
                    return 0f;

                float duration = 0f;
                for (int i = 0; i < lines.Count; i++)
                    duration += Mathf.Max(0.2f, lines[i]?.visibleSeconds ?? 0f);
                return duration;
            }
        }

        public bool HasVariants => variants != null && variants.Count > 0;

        public bool TryGetVariant(
            string variantId,
            out CharacterPresentationVariant variant)
        {
            variant = null;
            if (string.IsNullOrWhiteSpace(variantId) || variants == null)
                return false;

            for (int i = 0; i < variants.Count; i++)
            {
                CharacterPresentationVariant candidate = variants[i];
                if (candidate != null && string.Equals(
                        candidate.variantId,
                        variantId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    variant = candidate;
                    return true;
                }
            }
            return false;
        }

        public string ResolveRepeatKey(
            CharacterPresentationVariant variant)
        {
            return variant != null &&
                   !string.IsNullOrWhiteSpace(variant.repeatKey)
                ? variant.repeatKey
                : contentId;
        }

        public bool ShouldPlayExitMaterialization(
            CharacterPresentationVariant variant)
        {
            return variant == null ||
                   variant.exitEffectOverride !=
                   CharacterPresentationExitEffectOverride.FadeOnly;
        }

        public float FrameAspect
        {
            get
            {
                if (ResolvedFrameAtlas == null ||
                    ResolvedAtlasColumns <= 0 || ResolvedAtlasRows <= 0)
                    return 1f;
                float width = ResolvedFrameAtlas.width /
                              (float)ResolvedAtlasColumns;
                float height = ResolvedFrameAtlas.height /
                               (float)ResolvedAtlasRows;
                return height > 0f ? width / height : 1f;
            }
        }

        /// <summary>把从左上角开始的帧序号转换为 Unity 左下原点 UV。</summary>
        public Rect GetUvRect(int frameIndex)
        {
            int safeColumns = Mathf.Max(1, ResolvedAtlasColumns);
            int safeRows = Mathf.Max(1, ResolvedAtlasRows);
            int safeCount = Mathf.Max(1, ResolvedFrameCount);
            int index = Mathf.Clamp(frameIndex, 0, safeCount - 1);
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
            if (string.IsNullOrWhiteSpace(contentId))
                return Fail("contentId 不能为空。", out error);
            if (ResolvedFrameAtlas == null)
                return Fail($"{contentId} 缺少 frameAtlas。", out error);
            if (ResolvedFrameCount <= 0 ||
                ResolvedAtlasColumns <= 0 || ResolvedAtlasRows <= 0)
                return Fail($"{contentId} 的帧数与图集网格必须大于 0。", out error);
            if (ResolvedFrameCount >
                ResolvedAtlasColumns * ResolvedAtlasRows)
                return Fail($"{contentId} 的 frameCount 超出图集网格容量。", out error);
            if (ResolvedFrameAtlas.width % ResolvedAtlasColumns != 0 ||
                ResolvedFrameAtlas.height % ResolvedAtlasRows != 0)
            {
                return Fail($"{contentId} 的图集尺寸不能被网格整除。", out error);
            }
            if (ResolvedFramesPerSecond <= 0f)
                return Fail($"{contentId} 的 framesPerSecond 必须大于 0。", out error);
            if (ResolvedDisplayHeight <= 0f)
                return Fail($"{contentId} 的 displayHeight 必须大于 0。", out error);

            bool hasPartialDialoguePanelSize =
                (dialoguePanelSize.x > 0f) != (dialoguePanelSize.y > 0f);
            if (hasPartialDialoguePanelSize ||
                dialoguePanelSize.x < 0f || dialoguePanelSize.y < 0f)
            {
                return Fail(
                    $"{contentId} 的 dialoguePanelSize 必须同时为正数，" +
                    "或同时为 0 使用默认值。",
                    out error);
            }
            if (ResolvedEnterDuration < 0f ||
                ResolvedPanelFadeDuration < 0f ||
                ResolvedExitDuration < 0f)
                return Fail($"{contentId} 的演出时长不能为负数。", out error);

            bool hasDefaultLines = lines != null && lines.Count > 0;
            bool hasVariants = variants != null && variants.Count > 0;
            if (!hasDefaultLines && !hasVariants)
                return Fail($"{contentId} 至少需要默认台词或一个内容变体。", out error);
            if (hasDefaultLines && hasVariants)
            {
                return Fail(
                    $"{contentId} 不能同时配置默认台词和内容变体。",
                    out error);
            }
            if (hasDefaultLines &&
                !TryValidateLines(contentId, lines, out error))
                return false;

            if (hasVariants)
            {
                var variantIds = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                var repeatKeys = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < variants.Count; i++)
                {
                    CharacterPresentationVariant variant = variants[i];
                    if (variant == null ||
                        string.IsNullOrWhiteSpace(variant.variantId) ||
                        !variantIds.Add(variant.variantId))
                    {
                        return Fail(
                            $"{contentId} 的第 {i + 1} 个内容变体 ID 无效或重复。",
                            out error);
                    }
                    if (string.IsNullOrWhiteSpace(variant.repeatKey) ||
                        !repeatKeys.Add(variant.repeatKey))
                    {
                        return Fail(
                            $"{contentId} 的变体 {variant.variantId} " +
                            "缺少独立 repeatKey，或 repeatKey 重复。",
                            out error);
                    }
                    bool hasPartialPanelOverride =
                        (variant.dialoguePanelSizeOverride.x > 0f) !=
                        (variant.dialoguePanelSizeOverride.y > 0f);
                    if (hasPartialPanelOverride ||
                        variant.dialoguePanelSizeOverride.x < 0f ||
                        variant.dialoguePanelSizeOverride.y < 0f)
                    {
                        return Fail(
                            $"{contentId} 的变体 {variant.variantId} " +
                            "对话框覆盖尺寸必须同时为正数，或同时为 0。",
                            out error);
                    }
                    if (!TryValidateLines(
                            $"{contentId}/{variant.variantId}",
                            variant.lines,
                            out error))
                        return false;
                }
            }

            if (ResolvedPanelStyle ==
                    CharacterPresentationPanelStyle.HeroArrival &&
                string.IsNullOrWhiteSpace(displayTitle))
            {
                return Fail($"{contentId} 的英灵登场面板缺少 displayTitle。", out error);
            }
            if (ResolvedPanelStyle ==
                    CharacterPresentationPanelStyle.HeroArrival &&
                string.IsNullOrWhiteSpace(subjectContentId))
            {
                return Fail(
                    $"{contentId} 的英灵登场面板缺少 subjectContentId。",
                    out error);
            }
            if (RequiresAcknowledgement &&
                string.IsNullOrWhiteSpace(dismissHint))
            {
                return Fail($"{contentId} 的确认演出缺少 dismissHint。", out error);
            }

            error = null;
            return true;
        }

        private static bool TryValidateLines(
            string owner,
            IReadOnlyList<CharacterPresentationLine> configuredLines,
            out string error)
        {
            if (configuredLines == null || configuredLines.Count == 0)
                return Fail($"{owner} 至少需要一句台词。", out error);

            for (int i = 0; i < configuredLines.Count; i++)
            {
                CharacterPresentationLine line = configuredLines[i];
                if (line == null || string.IsNullOrWhiteSpace(line.text))
                    return Fail($"{owner} 的第 {i + 1} 句台词为空。", out error);
                if (line.visibleSeconds < 0.2f)
                    return Fail(
                        $"{owner} 的第 {i + 1} 句台词停留时间过短。",
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
    }

    /// <summary>按稳定 ID 解析角色演出配置，并拒绝重复或无效配置。</summary>
    public static class CharacterPresentationCatalog
    {
        private const string ResourceRoot = "Content/CharacterPresentations";
        private static Dictionary<string, CharacterPresentationProfile> _profiles;
        private static Dictionary<string, CharacterPresentationProfile>
            _heroArrivalsBySubject;

        public static CharacterPresentationProfile Get(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                return null;

            EnsureLoaded();
            if (_profiles.TryGetValue(contentId, out var profile))
                return profile;

            throw new KeyNotFoundException($"找不到角色演出配置：{contentId}。");
        }

        public static bool TryGet(
            string contentId,
            out CharacterPresentationProfile profile)
        {
            if (string.IsNullOrWhiteSpace(contentId))
            {
                profile = null;
                return false;
            }

            EnsureLoaded();
            return _profiles.TryGetValue(contentId, out profile);
        }

        /// <summary>按稳定英灵 ID 解析其正式 HeroArrival 演出。</summary>
        public static bool TryGetHeroArrivalForSubject(
            string subjectContentId,
            out CharacterPresentationProfile profile)
        {
            if (string.IsNullOrWhiteSpace(subjectContentId))
            {
                profile = null;
                return false;
            }

            EnsureLoaded();
            return _heroArrivalsBySubject.TryGetValue(
                subjectContentId,
                out profile);
        }

        public static void ClearCache()
        {
            _profiles = null;
            _heroArrivalsBySubject = null;
        }

        private static void EnsureLoaded()
        {
            if (_profiles != null)
                return;

            _profiles = new Dictionary<string, CharacterPresentationProfile>(
                StringComparer.OrdinalIgnoreCase);
            _heroArrivalsBySubject =
                new Dictionary<string, CharacterPresentationProfile>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (CharacterPresentationProfile profile in
                     Resources.LoadAll<CharacterPresentationProfile>(ResourceRoot))
            {
                if (profile == null)
                    continue;
                if (!profile.TryValidate(out string error))
                    throw new InvalidOperationException(error);
                if (!_profiles.TryAdd(profile.contentId, profile))
                {
                    throw new InvalidOperationException(
                        $"角色演出 contentId 重复：{profile.contentId}。");
                }
                if (profile.ResolvedPanelStyle ==
                        CharacterPresentationPanelStyle.HeroArrival &&
                    !_heroArrivalsBySubject.TryAdd(
                        profile.subjectContentId,
                        profile))
                {
                    throw new InvalidOperationException(
                        $"英灵 {profile.subjectContentId} 配置了重复的 " +
                        "HeroArrival 演出。");
                }
            }
        }
    }
}
