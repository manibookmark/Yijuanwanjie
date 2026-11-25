using System;
using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Meta;

namespace WanjieScroll.UI.Rewards
{
    public readonly struct SealAssignmentCandidate
    {
        public CardConfig Card { get; }
        public int BeforeDamage { get; }
        public int AfterDamage { get; }
        public int TargetSlotIndex { get; }

        public SealAssignmentCandidate(
            CardConfig card,
            int beforeDamage,
            int afterDamage,
            int targetSlotIndex)
        {
            Card = card;
            BeforeDamage = beforeDamage;
            AfterDamage = afterDamage;
            TargetSlotIndex = targetSlotIndex;
        }
    }

    /// <summary>
    /// 钤印选择页的纯状态所有者。它只保存候选快照和当前选择，伤害预览不会
    /// 修改 Run；最终提交仍使用精确 runtimeInstanceId。
    /// </summary>
    public sealed class SealAssignmentViewModel
    {
        private readonly List<SealAssignmentCandidate> _candidates =
            new List<SealAssignmentCandidate>();
        private readonly List<CardDisplayData> _committedDisplays =
            new List<CardDisplayData>();
        private readonly List<CardDisplayData> _candidateDisplays =
            new List<CardDisplayData>();
        private readonly List<string> _committedTooltipTexts = new List<string>();

        public LingyinConfig Seal { get; }
        public IReadOnlyList<SealAssignmentCandidate> Candidates =>
            _candidates;
        public int SelectedIndex { get; private set; } = -1;
        public string GradeDisplayName => Seal.grade switch
        {
            LingyinGrade.Uncommon => "良品",
            LingyinGrade.Rare => "上品",
            LingyinGrade.VeryRare => "珍品",
            LingyinGrade.UltraRare => "仙品",
            _ => "凡品"
        };
        public bool HasSelection => SelectedIndex >= 0 &&
                                    SelectedIndex < _candidates.Count;
        public SealAssignmentCandidate Selected => HasSelection
            ? _candidates[SelectedIndex]
            : default;

        public SealAssignmentViewModel(
            LingyinConfig seal,
            IReadOnlyList<CardConfig> candidates)
        {
            Seal = seal ?? throw new ArgumentNullException(nameof(seal));
            if (candidates == null || candidates.Count == 0)
                throw new ArgumentException("钤印候选不能为空。", nameof(candidates));

            CardSealSpec pending = seal.ToCardSealSpec();
            for (int i = 0; i < candidates.Count; i++)
            {
                CardConfig card = candidates[i];
                _candidateDisplays.Add(CardDisplayData.FromCard(card));
                // 调用方（SealInteractionEffectHandler.TryGetCandidates）已用
                // CardSealRules.CanAssign 过滤合法宿主，此处信任输入不重复过滤。
                int beforeDamage = PreviewDamage(card);
                CardConfig projected = CardSealRules.BuildAssignedCard(card, pending);
                if (projected == null)
                    throw new ArgumentException("钤印候选不再满足宿主条件。", nameof(candidates));
                int afterDamage;
                try
                {
                    afterDamage = PreviewDamage(projected);
                    _committedDisplays.Add(CardDisplayData.FromCard(projected));
                    _committedTooltipTexts.Add(ResultCardTooltipPresenter.BuildText(projected));
                }
                finally { ReleaseProjection(projected); }
                _candidates.Add(new SealAssignmentCandidate(
                    card,
                    beforeDamage,
                    afterDamage,
                    CardSealRules.GetOccupiedSlotCount(card)));
            }
        }

        public bool Select(int index)
        {
            if (index < 0 || index >= _candidates.Count)
                return false;
            SelectedIndex = index;
            return true;
        }

        public CardDisplayData BuildCandidateDisplayData(int index)
            => CopyDisplay(_candidateDisplays[index]);

        private static int PreviewDamage(CardConfig card)
        {
            // 比较整张牌的基础主伤；不只取第一段，也不修改宿主。
            return CardAttackDisplayProjection.Project(
                card, new CombatStatSnapshot(null, 0)).MainDamage;
        }

        /// <summary>
        /// 返回选择页建立时投影的结果。地图候选是副本，战斗候选可能在提交时原地
        /// 改变；不能提交后重新校验或追加印，否则会满槽失败／重复落印。
        /// </summary>
        public CardDisplayData BuildCommittedDisplayData()
        {
            if (!HasSelection)
                throw new InvalidOperationException("尚未选择钤印宿主。");
            return CopyDisplay(_committedDisplays[SelectedIndex]);
        }

        /// <summary>与成果卡面同一次投影的说明；不在提交后重复应用灵印。</summary>
        public string BuildCommittedTooltipText()
        {
            if (!HasSelection)
                throw new InvalidOperationException("尚未选择钤印宿主。");
            return _committedTooltipTexts[SelectedIndex];
        }

        private static CardDisplayData CopyDisplay(CardDisplayData result)
        {
            result.seals = result.seals.ConvertAll(seal => seal.Clone());
            return result;
        }

        private static void ReleaseProjection(CardConfig card)
        {
            if (UnityEngine.Application.isPlaying)
                UnityEngine.Object.Destroy(card);
            else
                UnityEngine.Object.DestroyImmediate(card);
        }
    }
}
