using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 连击链 ViewModel
    /// GDD 13.3: 连击链可视化（链中卡牌排列显示当前Combo，墨迹扩散动效随连击数增大）
    /// </summary>
    public class ComboViewModel : ViewModelBase
    {
        private ComboSystem _combo;

        private List<ChainEntry> _chainEntries = new List<ChainEntry>();

        public const string PROP_CHAIN = "Chain";
        public const string PROP_COMBO = "Combo";
        public const string PROP_CLIMAX = "Climax";

        /// <summary>连击链条目</summary>
        public IReadOnlyList<ChainEntry> ChainEntries => _chainEntries;

        /// <summary>当前连击计数（0=无连击）</summary>
        public int CurrentCombo => _combo?.Combo ?? 0;

        /// <summary>连击是否激活</summary>
        public bool IsActive => _combo?.IsActive ?? false;

        /// <summary>连击链长度</summary>
        public int ChainLength => _combo?.ChainSequence.Count ?? 0;

        /// <summary>是否进入连击高潮（连击 ≥ 10）</summary>
        public bool IsClimax => CurrentCombo >= 10;

        /// <summary>墨迹扩散强度（0-1，随连击数递增）</summary>
        public float InkSpreadIntensity
        {
            get
            {
                if (!IsActive) return 0f;
                return Math.Min(1.0f, CurrentCombo / 12f);
            }
        }

        /// <summary>
        /// 绑定连击系统
        /// </summary>
        public void Bind(ComboSystem combo)
        {
            _combo = combo;
            Refresh();
            Log("✓ ComboViewModel 已绑定");
        }

        public override void Dispose()
        {
            _combo = null;
            base.Dispose();
        }

        /// <summary>
        /// 刷新连击链数据
        /// </summary>
        public void Refresh()
        {
            if (_combo == null) return;

            _chainEntries.Clear();

            if (_combo.IsActive)
            {
                for (int i = 0; i < _combo.ChainSequence.Count; i++)
                {
                    _chainEntries.Add(new ChainEntry
                    {
                        position = i,
                        cost = _combo.ChainSequence[i],
                        multiplier = i + 1,
                        isCombo = true
                    });
                }
            }

            NotifyCollectionChanged(PROP_CHAIN);
            RaisePropertyChanged(PROP_COMBO);

            bool wasClimax = false;
            if (IsClimax != wasClimax)
            {
                RaisePropertyChanged(PROP_CLIMAX);
            }
        }

        /// <summary>
        /// 获取连击摘要文本（用于 UI 显示）
        /// </summary>
        public string GetSummary()
        {
            if (_combo == null || !_combo.IsActive)
                return "未连击";

            var parts = new List<string>();
            for (int i = 0; i < _chainEntries.Count; i++)
            {
                parts.Add($"×{_chainEntries[i].multiplier}");
            }

            string chainStr = string.Join("→", _combo.ChainSequence);
            string multStr = string.Join(" ", parts);
            return $"连击 ×{CurrentCombo} | 链: {chainStr} | {multStr}";
        }

        /// <summary>
        /// 当出牌后刷新（由 CombatViewModel 调用）
        /// </summary>
        public void OnCardPlayed()
        {
            Refresh();
        }
    }
}
