using System;
using System.Text.RegularExpressions;

namespace WanjieScroll.Audio
{
    /// <summary>
    /// 语音 cue 到 Resources 资源路径的唯一解析器（命名约定推导，零配置）。
    ///
    /// 约定（与《配音总关联文档》的目标文件列一致）：
    /// cue `voice.<角色>.<名称...>.v<版本>` 对应
    /// `Assets/Resources/Audio/Voice/<角色Pascal>/&lt;角色&gt;-&lt;名称...&gt;-v&lt;版本&gt;`。
    /// 示例：
    /// voice.wukong.arrival.v1 -> Audio/Voice/Wukong/wukong-arrival-v1
    /// voice.scroll-spirit.opening-cg.end.v1 -> Audio/Voice/ScrollSpirit/scroll-spirit-opening-cg-end-v1
    ///
    /// 保证：
    /// - 角色与名称段只允许小写字母、数字与连字符；版本段必须形如 v1、v2。
    /// - cue 大小写不敏感（统一按小写推导），但路径只由约定生成，不做任何查表。
    /// - 新增配音只需按约定命名文件放入 Voice 目录，无需登记表或生成步骤；
    ///   解析失败在运行时仅表现为该条语音静默（见 AudioManager.PlayVoiceCue）。
    /// </summary>
    public static class VoiceCueResolver
    {
        public const string CuePrefix = "voice.";
        public const string ResourceRoot = "Audio/Voice";

        private static readonly Regex SegmentPattern =
            new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

        private static readonly Regex VersionPattern =
            new Regex("^v[0-9]+$", RegexOptions.Compiled);

        /// <summary>
        /// 按命名约定推导 cue 对应的 Resources 路径（不含扩展名）。
        /// 返回 false 表示 cue 不符合语音约定（例如 sfx.presentation.* 前缀）。
        /// </summary>
        public static bool TryGetResourcePath(string cueId, out string resourcePath)
        {
            resourcePath = null;
            if (string.IsNullOrWhiteSpace(cueId))
                return false;

            string normalized = cueId.Trim().ToLowerInvariant();
            if (!normalized.StartsWith(CuePrefix, StringComparison.Ordinal))
                return false;

            // 至少三段：角色 + 名称 + 版本
            string[] segments = normalized
                .Substring(CuePrefix.Length)
                .Split('.');
            if (segments.Length < 3)
                return false;

            if (!VersionPattern.IsMatch(segments[segments.Length - 1]))
                return false;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (!SegmentPattern.IsMatch(segments[i]))
                    return false;
            }

            // 文件名 = 全部段落以连字符连接（角色-名称…-版本）
            string fileName = string.Join("-", segments);
            resourcePath = $"{ResourceRoot}/{ToPascalCase(segments[0])}/{fileName}";
            return true;
        }

        private static string ToPascalCase(string kebabCase)
        {
            string[] parts = kebabCase.Split('-');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    parts[i] = char.ToUpperInvariant(parts[i][0]) +
                               parts[i].Substring(1);
                }
            }
            return string.Concat(parts);
        }
    }
}
