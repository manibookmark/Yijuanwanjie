#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using WanjieScroll.EditorTools;

class SceneBuildSettingsHandler : AssetPostprocessor
{
    static void OnPostprocessAllAssets(
        string[] importedAssets, string[] deletedAssets,
        string[] movedAssets, string[] movedFromAssetPaths)
    {
        // 检查是否有任何 GUID 为零的场景
        foreach (var s in EditorBuildSettings.scenes)
        {
            string guid = AssetDatabase.AssetPathToGUID(s.path);
            if (string.IsNullOrEmpty(guid) || guid == "00000000000000000000000000000000" || s.guid.ToString() != guid)
            {
                ForceUpdate();
                return;
            }
        }
    }

    static void ForceUpdate()
    {
        var scenes = ProjectBuildSettings.ProductionScenes
            .Select(path => new EditorBuildSettingsScene(path, true))
            .ToArray();

        EditorBuildSettings.scenes = scenes;

        foreach (var s in scenes)
        {
            string guid = AssetDatabase.AssetPathToGUID(s.path);
            Debug.Log($"[SceneBuildSettingsHandler] {s.path} → GUID={guid}");
        }
    }
}
#endif
