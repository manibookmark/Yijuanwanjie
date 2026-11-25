using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 升级选择面板
    /// GDD 13.3: 三选一卡牌/宝石，曾进化过的组合会提示，符录可查询进化图鉴
    /// GDD 11.5: 印章风格按钮
    /// </summary>
    public class LevelUpPanel : UIView
    {
        [Header("面板")]
        public GameObject panelRoot;
        public Text titleText;

        [Header("奖励选项")]
        public RectTransform rewardContainer;
        public Button[] rewardButtons = new Button[3];
        public Text[] rewardNameTexts = new Text[3];
        public Text[] rewardDescTexts = new Text[3];
        public GameObject[] evolveHints = new GameObject[3];

        [Header("样式")]
        public Color newCardColor = new Color(0.8f, 0.6f, 0.2f);
        public Color gemColor = new Color(0.2f, 0.8f, 0.6f);
        public Color statColor = new Color(0.4f, 0.5f, 0.8f);

        private LevelUpViewModel _levelUpVM;

        protected override void OnViewModelChanged(string propertyName)
        {
            if (propertyName == "*" || propertyName == LevelUpViewModel.PROP_REWARDS ||
                propertyName == LevelUpViewModel.PROP_PENDING)
            {
                RefreshPanel();
            }
        }

        public override void Bind(ViewModelBase viewModel)
        {
            base.Bind(viewModel);
            _levelUpVM = viewModel as LevelUpViewModel;

            // 绑定按钮
            for (int i = 0; i < rewardButtons.Length; i++)
            {
                int capturedIndex = i;
                if (rewardButtons[i] != null)
                    rewardButtons[i].onClick.AddListener(() => OnSelectReward(capturedIndex));
            }
        }

        public override void Unbind()
        {
            foreach (var btn in rewardButtons)
            {
                if (btn != null) btn.onClick.RemoveAllListeners();
            }
            _levelUpVM = null;
            base.Unbind();
        }

        /// <summary>
        /// 刷新面板
        /// </summary>
        private void RefreshPanel()
        {
            if (_levelUpVM == null) return;

            bool isPending = _levelUpVM.IsPending;

            // 显示/隐藏面板
            if (panelRoot != null)
                panelRoot.SetActive(isPending);

            if (!isPending) return;

            // 标题
            if (titleText != null)
                titleText.text = "升级 — 三选一";

            // 奖励选项
            var rewards = _levelUpVM.Rewards;
            for (int i = 0; i < 3; i++)
            {
                bool hasReward = i < rewards.Count;

                if (rewardButtons[i] != null)
                    rewardButtons[i].gameObject.SetActive(hasReward);
                if (rewardNameTexts[i] != null && hasReward)
                    rewardNameTexts[i].gameObject.SetActive(true);
                if (rewardDescTexts[i] != null && hasReward)
                    rewardDescTexts[i].gameObject.SetActive(true);

                if (!hasReward) continue;

                var reward = rewards[i];

                // 名称
                if (rewardNameTexts[i] != null)
                {
                    rewardNameTexts[i].text = reward.name;
                    rewardNameTexts[i].color = reward.type switch
                    {
                        LevelUpSystem.RewardType.NewCard => newCardColor,
                        LevelUpSystem.RewardType.Gem => gemColor,
                        LevelUpSystem.RewardType.StatBoost => statColor,
                        _ => Color.white
                    };
                }

                // 描述
                if (rewardDescTexts[i] != null)
                {
                    rewardDescTexts[i].text = reward.description;
                }

                // 进化提示
                if (evolveHints[i] != null)
                {
                    evolveHints[i].SetActive(reward.canEvolve);
                    if (reward.canEvolve)
                    {
                        var hint = evolveHints[i].GetComponent<Text>();
                        if (hint != null)
                            hint.text = reward.evolutionHint;
                    }
                }
            }
        }

        /// <summary>
        /// 选择奖励
        /// </summary>
        private void OnSelectReward(int index)
        {
            _levelUpVM?.SelectReward(index);
        }
    }
}
