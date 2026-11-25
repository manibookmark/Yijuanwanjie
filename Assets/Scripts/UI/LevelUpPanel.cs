using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 升级奖励面板
    /// 三选一：新卡 / 宝石 / 属性提升
    /// </summary>
    public class LevelUpPanel : MonoBehaviour
    {
        [Header("References")]
        public GameObject panel;
        public Transform rewardContainer;
        public GameObject rewardCardPrefab;
        public TMP_Text levelText;
        public Button skipButton;

        private LevelUpSystem _levelUp;
        private List<LevelUpSystem.Reward> _currentRewards;
        private System.Action _onClosed;

        public void Setup(LevelUpSystem levelUp)
        {
            _levelUp = levelUp;
            if (skipButton != null)
                skipButton.onClick.AddListener(() =>
                {
                    // 跳过换 XP
                    if (panel != null) panel.SetActive(false);
                    _onClosed?.Invoke();
                });
        }

        /// <summary>
        /// 显示升级面板
        /// </summary>
        public void Show(int newLevel, List<CardConfig> deck, System.Action onClosed = null)
        {
            _onClosed = onClosed;
            if (panel != null) panel.SetActive(true);
            if (levelText != null) levelText.text = $"Level Up! Lv.{newLevel}";

            _currentRewards = _levelUp.GenerateRewards(3);
            RenderRewards(deck);
        }

        void RenderRewards(List<CardConfig> deck)
        {
            if (rewardContainer == null || rewardCardPrefab == null) return;

            for (int i = rewardContainer.childCount - 1; i >= 0; i--)
                Destroy(rewardContainer.GetChild(i).gameObject);

            for (int i = 0; i < _currentRewards.Count; i++)
            {
                var reward = _currentRewards[i];
                var obj = Instantiate(rewardCardPrefab, rewardContainer);

                var nameText = obj.transform.Find("Name")?.GetComponent<TMP_Text>();
                var descText = obj.transform.Find("Description")?.GetComponent<TMP_Text>();
                var iconImg = obj.transform.Find("Icon")?.GetComponent<Image>();
                var btn = obj.GetComponent<Button>();

                if (nameText != null) nameText.text = reward.name;
                if (descText != null)
                    descText.text = reward.card != null
                        ? Cards.CardTextRenderer.GetDisplayDescription(CardDisplayData.FromCard(reward.card))
                        : reward.description;

                // 图标颜色按类型
                if (iconImg != null)
                {
                    iconImg.color = reward.type switch
                    {
                        LevelUpSystem.RewardType.NewCard => new Color(0.75f, 0.22f, 0.17f), // 红
                        LevelUpSystem.RewardType.Gem => new Color(0.56f, 0.27f, 0.68f),      // 紫
                        LevelUpSystem.RewardType.StatBoost => new Color(0.96f, 0.82f, 0.25f), // 黄
                        _ => Color.white
                    };
                }

                if (btn != null)
                {
                    var capturedReward = reward;
                    btn.onClick.AddListener(() =>
                    {
                        _levelUp.SelectReward(capturedReward, deck);
                        if (panel != null) panel.SetActive(false);
                        _onClosed?.Invoke();
                    });
                }
            }
        }
    }
}
