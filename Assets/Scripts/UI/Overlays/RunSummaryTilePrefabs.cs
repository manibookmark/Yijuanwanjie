using UnityEngine;

namespace WanjieScroll.UI.Overlays
{
    /// <summary>
    /// 结算页 tile Prefab 的唯一路径来源。Prefab 缺失是资产事故（需运行
    /// 菜单 万界长卷/结算/生成结算页 Tile Prefab），不做运行时回退重建，
    /// 避免代码布局与 Prefab 布局两套平行真相。
    /// </summary>
    internal static class RunSummaryTilePrefabs
    {
        internal const string MetricTilePath =
            "UI/RunSummary/run-summary-metric-tile";
        internal const string GainTilePath =
            "UI/RunSummary/run-summary-gain-tile";
        internal const string RankRowPath =
            "UI/RunSummary/run-summary-rank-row";

        internal static GameObject Load(string path, string owner)
        {
            GameObject prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError(
                    $"[RunSummary] 缺少结算 tile Prefab：" +
                    $"Resources/{path}.prefab（{owner}）。请运行菜单 " +
                    "万界长卷/结算/生成结算页 Tile Prefab。");
            }
            return prefab;
        }
    }
}
