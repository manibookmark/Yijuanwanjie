using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Overlays
{
    /// <summary>
    /// 结算页单项数据 tile 的 Prefab 视图。字号、颜色、对齐等视觉参数全部在
    /// Prefab 上维护；运行时只写入文本与胜败强调色，不重建层级。
    /// </summary>
    public sealed class RunSummaryMetricTileView : MonoBehaviour
    {
        [SerializeField] private Text labelText;
        [SerializeField] private Text valueText;

        public void SetData(string label, string value, Color valueColor)
        {
            if (labelText != null)
                labelText.text = label ?? string.Empty;
            if (valueText != null)
            {
                valueText.text = value ?? string.Empty;
                valueText.color = valueColor;
            }
        }
    }
}
