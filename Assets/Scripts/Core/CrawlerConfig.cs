using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Optional follow-up contract for a color-triggered crawler. The contract
    /// is resolved after that resident spends its charge. Duration gains target
    /// all residents still present, or the shared one-use reserve if none remain.
    /// </summary>
    public enum CrawlerTriggerFollowUp
    {
        None = 0,
        IncreaseOtherCrawlersDuration = 1,
        GainCrawlerDuration = 2
    }

    /// <summary>
    /// 英灵配置（ScriptableObject）
    /// 以"魂卡"（绿卡）形式入卡组，打出后在场触发被动
    /// </summary>
    [CreateAssetMenu(fileName = "NewCrawler", menuName = "万界长卷/英灵")]
    public class CrawlerConfig : ScriptableObject
    {
        [Header("基本信息")]
            public string contentId;
            public string crawlerName;
            [TextArea(1, 3)]
            public string description;
            public string volumeId = "common";

        [Header("美术资源")]
        [Tooltip("英灵立绘（1024×1536）")]
        public Sprite portraitArt;

        [Tooltip("英灵缩略图（128×128）")]
        public Sprite iconArt;

        [Header("魂卡属性")]
        [Tooltip("打出魂卡的法力费用")]
        public int cardMana = 0;
        [Tooltip("入场效果描述")]
        public string enterEffect;

        [Header("颜色触发")]
        public CardColor triggerColor = CardColor.Huo;
        [Tooltip("触发被动效果描述")]
        public string triggerEffect;
        [Tooltip("结构化触发效果；为空时 triggerEffect 仅作展示文案")]
        public List<CardEffectSpec> triggerEffects;

        [Header("触发后续")]
        [Tooltip("自身先消耗一次并立即离场（若归零），随后给当前全部驻场续时；无人时进入一次性储备。")]
        public CrawlerTriggerFollowUp triggerFollowUp = CrawlerTriggerFollowUp.None;
        [Tooltip("触发后续每次给符合条件的英灵增加的凝神，通常为 1。")]
        public int triggerFollowUpAmount = 1;

        [Header("驻留")]
        [Tooltip("在场触发次数")]
        public int duration = 5;
        public CrawlerLeaderStats leaderStats;

        [Header("经济")]
        [Tooltip("招募价（灵玉）")]
        public int recruitPrice = 0;

        [Header("初始卡组")]
        [Tooltip("该英灵金卡的稳定 contentId；金卡本体不计入下方四张支援牌。")]
        public string cardContentId;
        [Tooltip("该英灵固定携带的四张支援牌稳定 contentId；重复 ID 表示重复卡牌实例。")]
        public string[] startingCardContentIds = System.Array.Empty<string>();

        /// <summary>
        /// 触发被动（返回效果描述供日志显示）
        /// </summary>
        public virtual string TriggerPassive(PlayerStats player)
        {
            // 基类只返回描述，具体效果由子类或系统解析
            return triggerEffect;
        }
    }
}
