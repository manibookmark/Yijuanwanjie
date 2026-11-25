using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    [Serializable]
    public sealed class StageResourceEntry
    {
        public string resourcePath;
        public StageResourceKind kind;
    }

    [Serializable]
    public sealed class StageMapResourceBinding
    {
        public string contentId;
        public string resourcePath;
    }

    /// <summary>
    /// Generated light index: paths only, never hard references to stage art.
    /// Runtime loading owns object residency; editor generation owns discovery.
    /// </summary>
    [CreateAssetMenu(fileName = "stage-resources", menuName = "万界长卷/内容/关区预热清单")]
    public sealed class StageResourceManifest : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public string dungeonContentId;
        public string displayName;
        public string coverResourcePath;
        public List<StageResourceEntry> resources = new List<StageResourceEntry>();
        public List<StageMapResourceBinding> mapProfiles = new List<StageMapResourceBinding>();
        public List<string> coverageNotes = new List<string>();

        public bool TryCreatePlan(out StageResourceLoadPlan plan, out string error)
        {
            plan = null; error = null;
            try
            {
                if (schemaVersion != CurrentSchemaVersion) throw new ArgumentException("关区清单版本不支持。");
                var entries = new List<StageResourceLoadItem>();
                foreach (StageResourceEntry item in resources ?? throw new ArgumentException("关区资源列表为空。"))
                {
                    if (item == null) throw new ArgumentException("关区资源列表含空项。");
                    entries.Add(new StageResourceLoadItem(item.resourcePath, item.kind));
                }
                var maps = new List<StageMapResourceLocation>();
                foreach (StageMapResourceBinding map in mapProfiles ?? throw new ArgumentException("关区地图列表为空。"))
                {
                    if (map == null) throw new ArgumentException("关区地图列表含空项。");
                    maps.Add(new StageMapResourceLocation(map.contentId, map.resourcePath));
                }
                plan = new StageResourceLoadPlan(dungeonContentId, displayName, coverResourcePath, entries, maps);
                return true;
            }
            catch (ArgumentException exception) { error = exception.Message; return false; }
        }
    }

    public static class StageResourceManifestCatalog
    {
        public const string ResourceRoot = "StagePreload";
        private static Dictionary<string, StageResourceManifest> _manifests;
        public static StageResourceManifest Get(string dungeonContentId)
        {
            if (TryGet(dungeonContentId, out StageResourceManifest manifest)) return manifest;
            throw new InvalidOperationException("关区缺少预热清单：" + dungeonContentId);
        }
        public static bool TryGet(string dungeonContentId, out StageResourceManifest manifest)
        {
            manifest = null;
            if (string.IsNullOrWhiteSpace(dungeonContentId)) return false;
            if (_manifests == null)
            {
                var built = new Dictionary<string, StageResourceManifest>(StringComparer.Ordinal);
                foreach (StageResourceManifest candidate in Resources.LoadAll<StageResourceManifest>(ResourceRoot))
                {
                    if (candidate == null) throw new InvalidOperationException("关区预热清单含空资产。");
                    if (!candidate.TryCreatePlan(out _, out string error))
                        throw new InvalidOperationException("关区预热清单无效：" + error);
                    if (!built.TryAdd(candidate.dungeonContentId, candidate))
                        throw new InvalidOperationException("关区预热清单 dungeonContentId 重复：" + candidate.dungeonContentId);
                }
                _manifests = built;
            }
            return _manifests.TryGetValue(dungeonContentId, out manifest);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearCache() => _manifests = null;
    }
}
