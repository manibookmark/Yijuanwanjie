using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Overlays
{
    /// <summary>
    /// 结算页“获得增益”单项 tile 的 Prefab 视图。视觉参数在 Prefab 上维护；
    /// 运行时只写文本。Hover tooltip 由面板在根物体上追加 Hover 表面，
    /// 本组件不持有 Hover 状态。
    /// </summary>
    public sealed class RunSummaryGainTileView : MonoBehaviour
    {
        [SerializeField] private Text labelText;

        public void SetData(string text)
        {
            if (labelText != null)
                labelText.text = text ?? string.Empty;
        }
    }
}
