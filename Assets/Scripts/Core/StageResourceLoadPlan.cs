using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WanjieScroll.Core
{
    public enum StageResourceKind { Object = 0, Sprite = 1, Texture2D = 2, AudioClip = 3 }

    public readonly struct StageResourceLoadItem
    {
        public string ResourcePath { get; }
        public StageResourceKind Kind { get; }
        public string CacheKey => ((int)Kind).ToString() + ":" + ResourcePath;
        public StageResourceLoadItem(string resourcePath, StageResourceKind kind)
        { ResourcePath = resourcePath; Kind = kind; }
    }

    public readonly struct StageMapResourceLocation
    {
        public string ContentId { get; }
        public string ResourcePath { get; }
        public StageMapResourceLocation(string contentId, string resourcePath)
        { ContentId = contentId; ResourcePath = resourcePath; }
    }

    /// <summary>
    /// Immutable, validated Resources work. Contains identity and paths only;
    /// no mutable content, loaded Unity objects, UI, or actual loading state.
    /// </summary>
    public sealed class StageResourceLoadPlan
    {
        public string DungeonContentId { get; }
        public string DisplayName { get; }
        public string CoverResourcePath { get; }
        public IReadOnlyList<StageResourceLoadItem> Resources { get; }
        public IReadOnlyList<StageMapResourceLocation> MapProfiles { get; }

        public StageResourceLoadPlan(string dungeonContentId, string displayName,
            string coverResourcePath, IEnumerable<StageResourceLoadItem> resources,
            IEnumerable<StageMapResourceLocation> mapProfiles)
        {
            if (string.IsNullOrWhiteSpace(dungeonContentId) || dungeonContentId != dungeonContentId.Trim())
                throw new ArgumentException("关区预热清单缺少规范的稳定 dungeonContentId。");
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("关区预热清单缺少显示名称。");
            ValidatePath(coverResourcePath);
            var entries = new List<StageResourceLoadItem>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            bool hasCover = false;
            foreach (StageResourceLoadItem item in resources ?? throw new ArgumentNullException(nameof(resources)))
            {
                ValidatePath(item.ResourcePath);
                if (!Enum.IsDefined(typeof(StageResourceKind), item.Kind))
                    throw new ArgumentException("未知关区资源类型。");
                if (!keys.Add(item.CacheKey))
                    throw new ArgumentException("关区预热资源重复：" + item.CacheKey);
                hasCover |= item.Kind == StageResourceKind.Sprite &&
                    string.Equals(item.ResourcePath, coverResourcePath, StringComparison.Ordinal);
                entries.Add(item);
            }
            if (!hasCover) throw new ArgumentException("关区封面必须包含在 Sprite 预热资源中。");
            if (entries[0].Kind != StageResourceKind.Sprite || entries[0].ResourcePath != coverResourcePath)
                throw new ArgumentException("关区封面必须是第一项预热资源。");
            var maps = new List<StageMapResourceLocation>();
            var mapIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (StageMapResourceLocation map in mapProfiles ?? throw new ArgumentNullException(nameof(mapProfiles)))
            {
                if (string.IsNullOrWhiteSpace(map.ContentId) || map.ContentId != map.ContentId.Trim() ||
                    !mapIds.Add(map.ContentId))
                    throw new ArgumentException("关区地图稳定 contentId 为空或重复。");
                ValidatePath(map.ResourcePath);
                if (!keys.Contains(new StageResourceLoadItem(map.ResourcePath, StageResourceKind.Object).CacheKey))
                    throw new ArgumentException("地图配置未包含在 Object 预热资源中：" + map.ContentId);
                maps.Add(map);
            }
            if (maps.Count == 0) throw new ArgumentException("关区预热清单没有地图配置。");
            DungeonContentId = dungeonContentId;
            DisplayName = displayName.Trim();
            CoverResourcePath = coverResourcePath;
            Resources = new ReadOnlyCollection<StageResourceLoadItem>(entries);
            MapProfiles = new ReadOnlyCollection<StageMapResourceLocation>(maps);
        }

        public static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path != path.Trim() || path.StartsWith("/", StringComparison.Ordinal) ||
                path.Contains("\\") || path.Contains(":") || path.Contains("//") ||
                path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("资源必须使用无扩展名的 Resources 相对路径。");
            foreach (string segment in path.Split('/'))
                if (segment == "." || segment == ".." || segment.Length == 0)
                    throw new ArgumentException("资源路径含非法片段。");
            foreach (string extension in new[] { ".asset", ".prefab", ".png", ".jpg", ".jpeg", ".wav", ".ogg", ".mp3", ".mat", ".shader", ".ttf", ".otf", ".json" })
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Resources 路径不能携带文件扩展名：" + path);
        }
    }
}
