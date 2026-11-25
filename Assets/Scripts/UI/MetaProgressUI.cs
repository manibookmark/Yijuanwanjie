using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using WanjieScroll.Meta;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 长卷殿界面（局外成长）
    /// 管理：Power-Up 阁、英灵殿、鉴宝斋、天机阁、宝物馆、总录台
    /// </summary>
    public class MetaProgressUI : MonoBehaviour
    {
        [Header("Top Bar")]
        public TMP_Text jadeText;
        public TMP_Text totalEarnedText;

        [Header("Tabs")]
        public Button powerUpTab;
        public Button crawlerTab;
        public Button relicTab;
        public Button arcanaTab;
        public Button dungeonTab;

        [Header("Panels")]
        public GameObject powerUpPanel;
        public GameObject crawlerPanel;
        public GameObject relicPanel;
        public GameObject arcanaPanel;
        public GameObject dungeonPanel;

        [Header("Power-Up Panel")]
        public Transform powerUpList;
        public GameObject powerUpItemPrefab;

        [Header("Crawler Panel")]
        public Transform crawlerList;
        public GameObject crawlerItemPrefab;

        [Header("Relic Panel")]
        public Transform relicGrid;
        public GameObject relicItemPrefab;

        [Header("Arcana Panel")]
        public Transform arcanaList;
        public GameObject arcanaItemPrefab;
        public Button equipArcanaButton;

        [Header("Refund")]
        public Button refundButton;
        public GameObject refundConfirmPanel;

        private MetaProgression _meta;
        private List<PowerUpConfig> _powerUpConfigs;
        private string _selectedArcana;

        public void Setup(MetaProgression meta, List<PowerUpConfig> powerUpConfigs)
        {
            _meta = meta;
            _powerUpConfigs = powerUpConfigs;

            // Tab 切换
            if (powerUpTab != null) powerUpTab.onClick.AddListener(() => SwitchTab("powerup"));
            if (crawlerTab != null) crawlerTab.onClick.AddListener(() => SwitchTab("crawler"));
            if (relicTab != null) relicTab.onClick.AddListener(() => SwitchTab("relic"));
            if (arcanaTab != null) arcanaTab.onClick.AddListener(() => SwitchTab("arcana"));
            if (dungeonTab != null) dungeonTab.onClick.AddListener(() => SwitchTab("dungeon"));

            // 退款
            if (refundButton != null)
                refundButton.onClick.AddListener(() =>
                {
                    if (refundConfirmPanel != null) refundConfirmPanel.SetActive(true);
                });

            UpdateTopBar();
            RenderPowerUps();
        }

        void SwitchTab(string tab)
        {
            if (powerUpPanel != null) powerUpPanel.SetActive(tab == "powerup");
            if (crawlerPanel != null) crawlerPanel.SetActive(tab == "crawler");
            if (relicPanel != null) relicPanel.SetActive(tab == "relic");
            if (arcanaPanel != null) arcanaPanel.SetActive(tab == "arcana");
            if (dungeonPanel != null) dungeonPanel.SetActive(tab == "dungeon");

            switch (tab)
            {
                case "powerup": RenderPowerUps(); break;
                case "crawler": RenderCrawlers(); break;
                case "relic": RenderRelics(); break;
                case "arcana": RenderArcanas(); break;
                case "dungeon": RenderDungeonList(); break;
            }
        }

        void UpdateTopBar()
        {
            if (jadeText != null) jadeText.text = $"Jade: {_meta.lingyu}";
            if (totalEarnedText != null) totalEarnedText.text = $"Earned: {_meta.totalLingyuEarned}";
        }

        /// <summary>
        /// 渲染 Power-Up 列表
        /// </summary>
        void RenderPowerUps()
        {
            if (powerUpList == null) return;

            for (int i = powerUpList.childCount - 1; i >= 0; i--)
                Destroy(powerUpList.GetChild(i).gameObject);

            if (powerUpItemPrefab == null) return;

            foreach (var cfg in _powerUpConfigs)
            {
                var obj = Instantiate(powerUpItemPrefab, powerUpList);

                var nameText = obj.transform.Find("Name")?.GetComponent<TMP_Text>();
                var levelText = obj.transform.Find("Level")?.GetComponent<TMP_Text>();
                var priceText = obj.transform.Find("Price")?.GetComponent<TMP_Text>();
                var buyBtn = obj.transform.Find("BuyButton")?.GetComponent<Button>();
                var lockIcon = obj.transform.Find("Lock")?.gameObject;

                int level = _meta.GetPowerUpLevel(cfg);
                bool locked = DaozangProgressionRules.IsUpgradeLocked(_meta, cfg.StableId);

                if (nameText != null) nameText.text = cfg.powerUpName;
                if (levelText != null) levelText.text = $"Lv.{level}/{cfg.maxLevel}";
                if (priceText != null) priceText.text = locked ? "Locked" : $"{cfg.GetLevelPrice(level + 1)} Jade";
                if (lockIcon != null) lockIcon.SetActive(locked);

                if (buyBtn != null)
                {
                    buyBtn.interactable = !locked && level < cfg.maxLevel;
                    var config = cfg;
                    buyBtn.onClick.AddListener(() =>
                    {
                        _meta.BuyPowerUp(config);
                        UpdateTopBar();
                        RenderPowerUps();
                    });
                }
            }
        }

        void RenderCrawlers()
        {
            // 渲染英灵列表
        }

        void RenderRelics()
        {
            // 渲染遗物列表
        }

        void RenderArcanas()
        {
            // 渲染秘法列表
        }

        void RenderDungeonList()
        {
            // 渲染关卡列表
        }
    }
}
