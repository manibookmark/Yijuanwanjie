using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 连击预览结果（不实际执行出牌，只计算假设结果）
    /// </summary>
    public struct ComboPreview
    {
        /// <summary>打出这张牌后是否进入连击</summary>
        public bool WouldCombo;
        /// <summary>打出这张牌是否会断链（Combo 清零）</summary>
        public bool WouldBreak;
        /// <summary>打出后连击是否会清空（ComboBreaker/Mild）</summary>
        public bool WouldClearCombo;
        /// <summary>打出后的 Combo 值（0-based，= comboLength - 1）</summary>
        public int ResultingCombo;
        /// <summary>打出后的效果倍率 ×(1+Combo)</summary>
        public int ResultingMultiplier;
        /// <summary>描述文字（用于 UI 显示）</summary>
        public string Description;
    }

    /// <summary>
    /// VC 兼容连击系统。详见 docs/rules/连击系统规范.md。
    /// 规则：连击值恰好递增 1 → 连击继续；否则断链。
    /// Wild 无条件续链并重置数字要求。Mild/ComboBreaker 结算后清空连击。
    /// </summary>
    public class ComboSystem
    {
        public ComboSystem(bool enabled = true)
        {
            Enabled = enabled;
        }

        /// <summary>是否已经由九叠或其它规则来源启用。</summary>
        public bool Enabled { get; }

        /// <summary>
        /// 当前连击计数（0-based，= comboLength - 1）。
        /// 第1张连击牌 Combo=0（倍率×1），第2张 Combo=1（倍率×2），依此类推。
        /// </summary>
        public int Combo => Math.Max(0, ComboLength - 1);

        /// <summary>
        /// 当前连击长度（1-based）。已进入连击栈的卡牌数量。
        /// 倍率 = comboLength（×1 起）。
        /// </summary>
        public int ComboLength { get; private set; } = 0;

        /// <summary>
        /// 相生加成中尚未被下一张出牌吸收的层数。
        /// 下一次 TryPlayCard / ClearCombo 时归零（加成已并入连击长度）。
        /// HUD 连击数字显示 ComboLength（含本加成），相生后立刻 +1。
        /// </summary>
        public int OutstandingWuxingBonus { get; private set; } = 0;

        /// <summary>
        /// 刚结算那张牌实际使用的连击长度（即其倍率基数）。
        /// 相生 +1 不反向放大当前牌，只推高后续链；如需还原“当牌
        /// 实际倍率”时从这里读取。
        /// </summary>
        public int ResolvedCardComboLength =>
            Math.Max(0, ComboLength - OutstandingWuxingBonus);

        /// <summary>
        /// 上一张数字卡的连击值。null = 无牌或 Wild 后，下一张可从任意值开始。
        /// </summary>
        public int? LastComboValue { get; private set; } = null;

        /// <summary>
        /// 上一张是否允许下一张降 1（逆行印效果）。
        /// </summary>
        public bool AllowReverseNext { get; private set; } = false;

        /// <summary>
        /// 当前连击是否激活（comboLength > 0 或已起链）。
        /// </summary>
        public bool IsActive => Enabled &&
                                (ComboLength > 0 || _chainStarted);

        /// <summary>
        /// 本回合所有打出卡牌的连击值序列（用于 UI 显示）。
        /// </summary>
        public List<int> ChainSequence { get; private set; } = new List<int>();

        private bool _chainStarted = false;

        /// <summary>
        /// 上一次 TryPlayCard 是否触发了断链（供 UI 层检测并清空出牌堆）。
        /// </summary>
        public bool WasChainBroken { get; private set; }

        /// <summary>
        /// ChainLink 装备后，回合结束不清空连击。
        /// </summary>
        public bool ChainLinkActive { get; set; } = false;

        /// <summary>
        /// 尝试打出一张牌，返回是否成功连击（是否推进 comboLength）。
        /// </summary>
        /// <param name="cost">解析后的三值费用</param>
        /// <returns>true=连击推进（或 Wild/Breaker），false=断链</returns>
        public bool TryPlayCard(ResolvedCardCost cost)
        {
            WasChainBroken = false;
            OutstandingWuxingBonus = 0;
            if (!Enabled)
            {
                ClearCombo();
                return false;
            }

            // ── ComboBreaker：无条件获得下一档倍率，结算后清空 ──
            if (cost.IsComboBreaker)
            {
                if (!_chainStarted)
                {
                    // 空链打出 Breaker：×1 结算，仍为空链
                    ComboLength = 0;
                    _chainStarted = false;
                    LastComboValue = null;
                    AllowReverseNext = false;
                    ChainSequence.Clear();
                    return true;
                }
                // comboLength 保持当前值（不减），倍率 = comboLength + 1
                // 结算后由调用方调用 ClearCombo()
                ComboLength++; // Breaker 自身也 +1
                ChainSequence.Add(cost.ComboValue);
                // 标记需要清空（调用方检查 IsComboBreaker 后调 ClearCombo）
                return true;
            }

            // ── Wild：无条件续链 ──
            if (cost.IsWild)
            {
                ComboLength++;
                _chainStarted = true;
                LastComboValue = null;
                AllowReverseNext = false;
                ChainSequence.Add(cost.ComboValue);
                WasChainBroken = false;
                return true;
            }

            // ── 普通数字卡（含 Mild / FreeToPlay）──
            bool isLegal = CheckLegal(cost.ComboValue);

            if (isLegal)
            {
                ComboLength++;
                _chainStarted = true;
                LastComboValue = cost.ComboValue;
                AllowReverseNext = cost.HasReverse;
                ChainSequence.Add(cost.ComboValue);
                WasChainBroken = false;
                // Mild 在结算后由调用方调 ClearCombo()
                return true;
            }

            // 断链：当前牌成为新链起点
            ComboLength = 1;
            _chainStarted = true;
            LastComboValue = cost.ComboValue;
            AllowReverseNext = cost.HasReverse;
            ChainSequence.Clear();
            ChainSequence.Add(cost.ComboValue);
            WasChainBroken = true;
            // Mild 断链后也是 ×1 结算再 ClearCombo()
            return false;
        }

        /// <summary>
        /// 判断一张数字卡是否合法续链（不修改状态）。
        /// </summary>
        private bool CheckLegal(int comboValue)
        {
            // A. 空链
            if (!_chainStarted || ComboLength == 0)
                return true;

            // B. Wild 后（LastComboValue == null）
            if (LastComboValue == null)
                return true;

            // C. 恰好递增 1
            if ((long)comboValue == (long)LastComboValue.Value + 1)
                return true;

            // D. 逆行下降 1
            if (AllowReverseNext && (long)comboValue == (long)LastComboValue.Value - 1)
                return true;

            return false;
        }

        /// <summary>
        /// 五行相生额外加成（木→火效果）：增加连击长度，提升倍率。
        /// 不改变 LastComboValue（下一张牌仍按正常规则判定）。
        /// </summary>
        public void AddWuxingBonus(int extra = 1)
        {
            if (!Enabled) return;
            ComboLength += extra;
            OutstandingWuxingBonus += extra;
            _chainStarted = true;
        }

        /// <summary>
        /// 清空连击状态（ComboBreaker / Mild 结算后调用）。
        /// </summary>
        public void ClearCombo()
        {
            ComboLength = 0;
            LastComboValue = null;
            AllowReverseNext = false;
            _chainStarted = false;
            WasChainBroken = false;
            OutstandingWuxingBonus = 0;
            ChainSequence.Clear();
        }

        /// <summary>
        /// 预览：如果打出这张牌，连击结果是什么（不实际执行）。
        /// </summary>
        public ComboPreview PreviewPlayCard(ResolvedCardCost cost)
        {
            if (!Enabled)
            {
                return new ComboPreview
                {
                    WouldCombo = false,
                    WouldBreak = false,
                    WouldClearCombo = false,
                    ResultingCombo = 0,
                    ResultingMultiplier = 1,
                    Description = "叠势尚未启用"
                };
            }

            // ComboBreaker
            if (cost.IsComboBreaker)
            {
                int resultingCombo = ComboLength; // 当前 comboLength（0-based: ComboLength-1），Breaker +1 后 = ComboLength
                int multiplier = Math.Max(1, ComboLength + 1);
                return new ComboPreview
                {
                    WouldCombo = true,
                    WouldBreak = false,
                    WouldClearCombo = true,
                    ResultingCombo = resultingCombo,
                    ResultingMultiplier = multiplier,
                    Description = $"收势 · ×{multiplier}后连击清空"
                };
            }

            // Wild
            if (cost.IsWild)
            {
                int newCombo = ComboLength + 1;
                return new ComboPreview
                {
                    WouldCombo = true,
                    WouldBreak = false,
                    WouldClearCombo = false,
                    ResultingCombo = newCombo - 1, // 0-based
                    ResultingMultiplier = newCombo,
                    Description = $"太极牌续链 · ×{newCombo}"
                };
            }

            // Mild
            if (cost.IsMild)
            {
                bool legal = CheckLegal(cost.ComboValue);
                if (legal)
                {
                    int newCombo = ComboLength + 1;
                    return new ComboPreview
                    {
                        WouldCombo = true,
                        WouldBreak = false,
                        WouldClearCombo = true,
                        ResultingCombo = newCombo - 1,
                        ResultingMultiplier = newCombo,
                        Description = $"定印 · ×{newCombo}后连击清空"
                    };
                }
                return new ComboPreview
                {
                    WouldCombo = false,
                    WouldBreak = false,
                    WouldClearCombo = true,
                    ResultingCombo = 0,
                    ResultingMultiplier = 1,
                    Description = "定印断链 · ×1后连击清空"
                };
            }

            // 普通数字卡
            bool isLegal = CheckLegal(cost.ComboValue);

            if (isLegal)
            {
                int newCombo = ComboLength + 1;
                return new ComboPreview
                {
                    WouldCombo = true,
                    WouldBreak = false,
                    WouldClearCombo = false,
                    ResultingCombo = newCombo - 1,
                    ResultingMultiplier = newCombo,
                    Description = $"连击 · ×{newCombo}"
                };
            }

            // 断链
            string desc;
            if (LastComboValue != null)
                desc = $"断链 · 连击值{cost.ComboValue}≠{LastComboValue.Value}+1 · 连击清零";
            else
                desc = "断链 · 连击清零";

            return new ComboPreview
            {
                WouldCombo = false,
                WouldBreak = true,
                WouldClearCombo = false,
                ResultingCombo = 0,
                ResultingMultiplier = 1,
                Description = desc
            };
        }

        /// <summary>
        /// A returning play earns the ordinary hypothetical multiplier but
        /// freezes every part of the chain, including reverse and Wuxing debt.
        /// Preview and execution use this same receipt, never snapshot/undo.
        /// </summary>
        public ComboPreview PreviewPlayCard(ResolvedCardCost cost, bool preserveChain)
        {
            ComboPreview result = PreviewPlayCard(cost);
            if (!preserveChain) return result;
            result.WouldCombo = false;
            result.WouldBreak = false;
            result.WouldClearCombo = false;
            result.Description = $"返手 · ×{result.ResultingMultiplier}（叠势不变）";
            return result;
        }

        public ComboPreview ResolvePlayCard(ResolvedCardCost cost, bool preserveChain)
        {
            ComboPreview result = PreviewPlayCard(cost, preserveChain);
            if (preserveChain)
                WasChainBroken = false; // Last-resolution receipt, not chain state.
            else
            {
                result.WouldCombo = TryPlayCard(cost);
                result.WouldBreak = WasChainBroken;
            }
            return result;
        }

        /// <summary>
        /// 回合结束时清零。ChainLink 装备时跳过。
        /// </summary>
        public void Reset()
        {
            if (ChainLinkActive) return;
            ClearCombo();
        }

        /// <summary>
        /// A detached checkpoint of this one state machine, including reverse's
        /// consumed/unconsumed state. Capturing/restoring never plays a card or
        /// rearms a qualifier. Formal seals and legacy hasReverse share this state.
        /// </summary>
        public ComboMemento CaptureMemento()
            => new ComboMemento
            {
                enabled = Enabled,
                length = ComboLength,
                outstandingWuxingBonus = OutstandingWuxingBonus,
                hasLastValue = LastComboValue.HasValue,
                lastValue = LastComboValue.GetValueOrDefault(),
                allowReverse = AllowReverseNext,
                chainStarted = _chainStarted,
                chainBroken = WasChainBroken,
                chainLink = ChainLinkActive,
                sequence = new List<int>(ChainSequence)
            };

        /// <summary>Validates a complete checkpoint before replacing live state; no callbacks or payment.</summary>
        public void RestoreMemento(ComboMemento saved)
        {
            if (saved == null || saved.version != 1 || saved.enabled != Enabled || saved.length < 0 ||
                saved.outstandingWuxingBonus < 0 || saved.outstandingWuxingBonus > saved.length ||
                saved.sequence == null || saved.sequence.Count > saved.length ||
                (saved.allowReverse && !saved.hasLastValue) ||
                (!saved.chainStarted && (saved.length != 0 || saved.hasLastValue || saved.allowReverse)) ||
                (!Enabled && (saved.length != 0 || saved.chainStarted)))
                throw new ArgumentException("Invalid combo checkpoint.", nameof(saved));
            var sequence = new List<int>(saved.sequence);
            ComboLength = saved.length;
            OutstandingWuxingBonus = saved.outstandingWuxingBonus;
            LastComboValue = saved.hasLastValue ? saved.lastValue : (int?)null;
            AllowReverseNext = saved.allowReverse;
            _chainStarted = saved.chainStarted;
            WasChainBroken = saved.chainBroken;
            ChainLinkActive = saved.chainLink;
            ChainSequence = sequence;
        }

        /// <summary>
        /// 连击状态摘要（用于 UI 显示）。
        /// </summary>
        public string GetSummary()
        {
            if (!Enabled) return "叠势尚未启用";
            if (!IsActive) return "未连击";
            var seq = string.Join("→", ChainSequence);
            return $"连击 ×{Combo + 1} | 链: {seq}";
        }
    }

    /// <summary>Serialization DTO only; a restored owner never shares its mutable sequence.</summary>
    [Serializable]
    public sealed class ComboMemento
    {
        public int version = 1;
        public bool enabled;
        public int length;
        public int outstandingWuxingBonus;
        public bool hasLastValue;
        public int lastValue;
        public bool allowReverse;
        public bool chainStarted;
        public bool chainBroken;
        public bool chainLink;
        public List<int> sequence = new List<int>();
    }
}
