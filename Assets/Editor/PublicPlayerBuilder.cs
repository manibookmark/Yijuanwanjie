using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.EditorTools
{
    public static class PublicPlayerBuilder
    {
        public static void ValidateRelease()
        {
            ProjectBuildSettings.ValidateProject();
            var tutorial = LevelRunProfileCatalog.Get(DemoReleasePolicy.TutorialLevelContentId);
            var tutorialErrors = tutorial.Validate();
            if (tutorialErrors.Count > 0)
                throw new BuildFailedException("Tutorial configuration rejected: " + string.Join(", ", tutorialErrors));

            ValidateStageResources();
            string leader = GameSession.DefaultCrawlerContentId;
            var account = ProgressAccountBlueprints.Build(TestAccountPreset.PostTutorial, leader);
            if (!RunStartService.TryPrepare("run-entry.xiyou.shuangchaling.v1", account, leader,
                    DaozangProgressionRules.ApplyToPlayerStats, out var run, out string error))
                throw new BuildFailedException("Formal G1 entry rejected: " + error);
            if (run.RunCardCount == 0 || !run.TryProjectPlayerBaseline(new PlayerStats()))
                throw new BuildFailedException("Formal G1 entry has no playable baseline.");
            Debug.Log("[PublicPlayerBuilder] Normal G1 entry passed.");
        }

        private static void ValidateStageResources()
        {
            var manifests = Resources.LoadAll<StageResourceManifest>(StageResourceManifestCatalog.ResourceRoot);
            if (manifests.Length == 0)
                throw new BuildFailedException("No stage resource manifests were found.");
            foreach (StageResourceManifest manifest in manifests)
            {
                if (!manifest.TryCreatePlan(out var plan, out string error))
                    throw new BuildFailedException(manifest.dungeonContentId + ": " + error);
                foreach (StageResourceLoadItem item in plan.Resources)
                {
                    Object asset = item.Kind switch
                    {
                        StageResourceKind.Sprite => Resources.Load<Sprite>(item.ResourcePath),
                        StageResourceKind.Texture2D => Resources.Load<Texture2D>(item.ResourcePath),
                        StageResourceKind.AudioClip => Resources.Load<AudioClip>(item.ResourcePath),
                        _ => Resources.Load(item.ResourcePath)
                    };
                    if (asset == null)
                        throw new BuildFailedException(plan.DungeonContentId + ": missing " + item.CacheKey);
                }
                Debug.Log($"[PublicPlayerBuilder] {plan.DungeonContentId} resource plan passed ({plan.Resources.Count} items).");
            }
        }

        public static void BuildWindows()
        {
            ValidateRelease();
            const BuildTarget target = BuildTarget.StandaloneWindows64;
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new BuildFailedException("Install Windows build support for Tuanjie.");

            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Public"));
            if (Directory.Exists(output))
                throw new BuildFailedException("Builds/Public already exists. Move the previous build before rebuilding.");

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = ProjectBuildSettings.ProductionScenes,
                locationPathName = Path.Combine(output, "WanjieScroll.exe"),
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.CompressWithLz4HC
            });

            if (report == null || report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new BuildFailedException("Windows Player build failed. Review the Editor log.");
            Debug.Log("[PublicPlayerBuilder] Windows Player built successfully.");
        }
    }
}
