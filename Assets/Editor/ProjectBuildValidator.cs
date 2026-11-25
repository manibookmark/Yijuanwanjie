using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using WanjieScroll.Bootstrap;
using WanjieScroll.Content;

namespace WanjieScroll.EditorTools
{
    public static class ProjectBuildSettings
    {
        public static readonly string[] ProductionScenes =
        {
            "Assets/Scenes/Loading.unity",
            "Assets/Scenes/MainMenu.unity",
            "Assets/Scenes/Hub.unity",
            "Assets/Scenes/LevelSelect.unity",
            "Assets/Scenes/RouteSelect.unity",
            "Assets/Scenes/Dungeon.unity",
            "Assets/Scenes/OpeningCG.unity",
            "Assets/Scenes/LevelMap.unity",
            "Assets/Scenes/Combat.unity",
            "Assets/Scenes/Daozang.unity"
        };

        public static void ApplyProductionBuildSettings()
        {
            EnsureSceneFilesExist();
            EditorBuildSettings.scenes = ProductionScenes
                .Select(path => new EditorBuildSettingsScene(path, true))
                .ToArray();
            Debug.Log($"[ProjectBuildSettings] 已写入 {ProductionScenes.Length} 个正式场景。");
        }

        public static void ValidateProject()
        {
            var errors = CollectErrors();
            if (errors.Count > 0)
                throw new BuildFailedException(string.Join(Environment.NewLine, errors));
            Debug.Log("[ProjectBuildSettings] 项目校验通过。");
        }

        public static List<string> CollectErrors()
        {
            var errors = new List<string>();
            errors.AddRange(MetaGuidValidator.CollectErrors());
            foreach (string scenePath in ProductionScenes)
            {
                if (!File.Exists(scenePath))
                    errors.Add($"正式场景不存在：{scenePath}");
            }

            string[] actualScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (!actualScenes.SequenceEqual(ProductionScenes))
            {
                errors.Add(
                    $"Build Settings 不一致。期望：{string.Join(", ", ProductionScenes)}；" +
                    $"实际：{string.Join(", ", actualScenes)}");
            }

            List<string> contentPackageErrors =
                ContentPackageGenerator.CollectErrors();
            errors.AddRange(contentPackageErrors);
            if (contentPackageErrors.Count == 0)
            {
                try
                {
                    ContentValidationReport report =
                        ContentCatalog.Current.ValidationReport;
                    foreach (ContentValidationIssue issue in report.Issues)
                    {
                        if (issue.Severity == ContentValidationSeverity.Error)
                            errors.Add(issue.ToString());
                    }
                }
                catch (Exception exception)
                {
                    errors.Add($"运行时 ContentCatalog 加载失败：{exception.Message}");
                }
            }

            errors.AddRange(ProductionConfigurationValidator.CollectErrors());
            errors.AddRange(RuntimeUiPrefabBuildValidator.CollectErrors());
            errors.AddRange(PapercraftSpriteImportPolicy.CollectErrors());
            errors.AddRange(MapInteractionSpriteImportPolicy.CollectErrors());
            errors.AddRange(MapBackgroundImportPolicy.CollectErrors());
            errors.AddRange(PlayerBrandingSettings.CollectErrors());

            return errors;
        }

        // The dedicated profile has no scene navigation or presentation. It
        // still validates GUIDs and every production rule/content contract.
        public static List<string> CollectSimulationErrors()
        {
            var errors = new List<string>(MetaGuidValidator.CollectErrors());
            errors.AddRange(ContentPackageGenerator.CollectErrors());
            errors.AddRange(ProductionConfigurationValidator.CollectErrors());
            try
            {
                foreach (ContentValidationIssue issue in ContentCatalog.Current.ValidationReport.Issues)
                    if (issue.Severity == ContentValidationSeverity.Error) errors.Add(issue.ToString());
            }
            catch (Exception exception) { errors.Add("Runtime content validation failed: " + exception.Message); }
            return errors;
        }

        private static void EnsureSceneFilesExist()
        {
            string[] missing = ProductionScenes.Where(path => !File.Exists(path)).ToArray();
            if (missing.Length > 0)
                throw new FileNotFoundException($"缺少正式场景：{string.Join(", ", missing)}");
        }
    }

    public sealed class ProjectBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            List<string> errors = AgentSimulationPlayerBuilder.IsBuilding
                ? ProjectBuildSettings.CollectSimulationErrors() : ProjectBuildSettings.CollectErrors();
            if (errors.Count > 0)
                throw new BuildFailedException(string.Join(Environment.NewLine, errors));
        }
    }
}
