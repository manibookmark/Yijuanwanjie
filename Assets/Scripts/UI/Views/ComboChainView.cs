using System;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 连击链可视化视图
    /// GDD 13.3: 链中卡牌排列显示当前乘区，墨迹扩散动效随连击数增大，连击数 > 10 时特效增强
    /// GDD 11.5: 墨迹扩散动效
    /// </summary>
    public class ComboChainView : UIView
    {
        [Header("链布局")]
        public RectTransform chainContainer;
        public GameObject chainEntryPrefab;

        [Header("效果")]
        public Image comboGlow;
        public Text comboSummaryText;
        public event Action<float, bool> InkSpreadRequested;

        [Header("高潮模式")]
        public Color normalGlowColor = new Color(0.05f, 0.05f, 0.1f, 0.4f);
        public Color climaxGlowColor = new Color(0.8f, 0.1f, 0.1f, 0.6f);

        private ComboViewModel _comboVM;
        private bool _wasActive = false;
        private bool _wasClimax = false;

        protected override void OnViewModelChanged(string propertyName)
        {
            if (propertyName == "*" || propertyName == ComboViewModel.PROP_CHAIN ||
                propertyName == ComboViewModel.PROP_CLIMAX)
            {
                RefreshChain();
            }
        }

        public override void Bind(ViewModelBase viewModel)
        {
            base.Bind(viewModel);
            _comboVM = viewModel as ComboViewModel;
        }

        /// <summary>
        /// 刷新连击链显示
        /// </summary>
        private void RefreshChain()
        {
            if (_comboVM == null) return;

            var entries = _comboVM.ChainEntries;
            bool isActive = _comboVM.IsActive;
            bool isClimax = _comboVM.IsClimax;

            // 摘要文本
            if (comboSummaryText != null)
            {
                comboSummaryText.text = _comboVM.GetSummary();
                comboSummaryText.color = isClimax ? climaxGlowColor : normalGlowColor;
            }

            // 链条目
            if (chainContainer != null)
            {
                // 清除旧的
                for (int i = chainContainer.childCount - 1; i >= 0; i--)
                {
                    Destroy(chainContainer.GetChild(i).gameObject);
                }

                // 创建新的
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var entryObj = CreateChainEntry(entry);
                    entryObj.transform.SetParent(chainContainer, false);
                    entryObj.transform.localPosition = new Vector3(i * 80, 0, 0);
                }
            }

            // 墨迹扩散效果
            if (isActive && !_wasActive)
            {
                InkSpreadRequested?.Invoke(_comboVM.InkSpreadIntensity, isClimax);
            }
            else if (isActive && isClimax && !_wasClimax)
            {
                InkSpreadRequested?.Invoke(1.0f, true);
            }

            // 光晕
            if (comboGlow != null)
            {
                comboGlow.color = isClimax ? climaxGlowColor : normalGlowColor;
                comboGlow.gameObject.SetActive(isActive);

                // 连击数越大，光晕越强
                float alpha = isActive ? Mathf.Lerp(0.2f, 0.8f, Mathf.Min(1f, _comboVM.CurrentCombo / 12f)) : 0f;
                var c = comboGlow.color;
                c.a = alpha;
                comboGlow.color = c;
            }

            _wasActive = isActive;
            _wasClimax = isClimax;
        }

        /// <summary>
        /// 创建链条目
        /// </summary>
        private GameObject CreateChainEntry(UI.ChainEntry entry)
        {
            var go = new GameObject($"Chain_{entry.position}", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(70, 70);

            // 乘区数字
            var textObj = new GameObject("Multiplier", typeof(Text)).GetComponent<RectTransform>();
            textObj.SetParent(go.transform, false);
            textObj.anchorMin = Vector2.zero;
            textObj.anchorMax = Vector2.one;
            textObj.offsetMin = Vector2.zero;
            textObj.offsetMax = Vector2.zero;
            var text = textObj.GetComponent<Text>();
            text.text = $"×{entry.multiplier}";
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 24;
            text.color = entry.multiplier >= 5 ? new Color(0.8f, 0.1f, 0.1f) : new Color(0.1f, 0.1f, 0.2f);

            // 费用
            var costObj = new GameObject("Cost", typeof(Text)).GetComponent<RectTransform>();
            costObj.SetParent(go.transform, false);
            costObj.anchorMin = new Vector2(0f, 0.7f);
            costObj.anchorMax = new Vector2(1f, 1f);
            costObj.offsetMin = Vector2.zero;
            costObj.offsetMax = Vector2.zero;
            var costText = costObj.GetComponent<Text>();
            costText.text = $"费{entry.cost}";
            costText.alignment = TextAnchor.MiddleCenter;
            costText.fontSize = 12;
            costText.color = Color.gray;

            return go;
        }
    }
}
