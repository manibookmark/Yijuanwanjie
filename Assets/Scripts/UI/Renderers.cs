using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Meta;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 英灵渲染器
    /// 挂载到英灵卡片/立绘 Prefab 上
    /// </summary>
    public class CrawlerRenderer : MonoBehaviour
    {
        [Header("UI References")]
        public Image portrait;
        public TMP_Text nameText;
        public TMP_Text triggerText;
        public TMP_Text durationText;
        public TMP_Text manaCostText;
        public TMP_Text playstyleText;
        public Image colorIndicator;

        [Header("States")]
        public GameObject activeIndicator;
        public GameObject lockedIndicator;
        public Button recruitButton;

        private CrawlerConfig _config;

        public void Setup(CrawlerConfig crawler, bool recruited, bool canAfford, int currentPrice)
        {
            _config = crawler;

            if (nameText != null) nameText.text = crawler.crawlerName;
            if (triggerText != null) triggerText.text = crawler.triggerEffect;
            if (durationText != null) durationText.text = $"Duration: {crawler.duration}";
            if (manaCostText != null) manaCostText.text = crawler.cardMana == 0 ? "0" : crawler.cardMana.ToString();
            if (playstyleText != null) playstyleText.text = crawler.enterEffect;

            // 立绘：优先使用配置的，回退到孙悟空立绘
            if (portrait != null && portrait.sprite == null)
            {
                portrait.sprite = RuntimeArtLoader.GetWukongPortrait();
            }

            if (colorIndicator != null)
            {
                colorIndicator.color = crawler.triggerColor switch
                {
                    CardColor.Huo => new Color(1.0f,   0.271f, 0.0f),
                    CardColor.Tu  => new Color(0.545f, 0.353f, 0.169f),
                    CardColor.Mu  => new Color(0.180f, 0.545f, 0.337f),
                    CardColor.Shui => new Color(0.098f, 0.098f, 0.439f),
                    CardColor.Jin => new Color(1.0f,   0.843f, 0.0f),
                    CardColor.Yang => new Color(1.0f,   0.980f, 0.941f),
                    CardColor.Yin => new Color(0.102f, 0.0f,   0.102f),
                    _ => Color.white
                };
            }

            if (activeIndicator != null) activeIndicator.SetActive(recruited);
            if (lockedIndicator != null) lockedIndicator.SetActive(!recruited && !canAfford);

            if (recruitButton != null)
            {
                recruitButton.interactable = !recruited && canAfford;
                var label = recruitButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = recruited ? "Recruited" : $"{currentPrice} Jade";
                }
            }
        }
    }

    /// <summary>
    /// 宝石渲染器
    /// </summary>
    public class GemRenderer : MonoBehaviour
    {
        public Image gemIcon;
        public TMP_Text gemName;
        public TMP_Text gemDesc;
        public Image rarityFrame;

        public void Setup(GemConfig gem)
        {
            if (gemName != null) gemName.text = gem.gemName;
            if (gemDesc != null) gemDesc.text = gem.description;

            if (rarityFrame != null)
            {
                rarityFrame.color = gem.rarity switch
                {
                    GemRarity.Common => new Color(0.6f, 0.6f, 0.6f),
                    GemRarity.Uncommon => new Color(0.2f, 0.7f, 0.3f),
                    GemRarity.Rare => new Color(0.2f, 0.5f, 0.9f),
                    GemRarity.VeryRare => new Color(0.7f, 0.3f, 0.9f),
                    GemRarity.UltraRare => new Color(1f, 0.8f, 0.2f),
                    _ => Color.white
                };
            }
        }
    }

    /// <summary>
    /// 敌人渲染器
    /// </summary>
    public class EnemyRenderer : MonoBehaviour
    {
        public Image portrait;
        public TMP_Text nameText;
        public Slider healthBar;
        public TMP_Text hpText;
        public TMP_Text armorText;
        public Image bossIndicator;
        public Button selectButton;

        private int _index;

        public void Setup(EnemyStats enemy, int index, bool isSelected)
        {
            _index = index;

            if (nameText != null)
            {
                nameText.text = enemy.name;
                nameText.color = enemy.isBoss ? Color.red : Color.white;
            }

            if (healthBar != null)
            {
                healthBar.maxValue = enemy.baseHealth;
                healthBar.value = enemy.currentHealth;
            }

            if (hpText != null)
            {
                hpText.text = $"{enemy.currentHealth}/{enemy.baseHealth}";
                hpText.color = enemy.IsDead ? Color.gray : Color.white;
            }

            if (armorText != null)
            {
                armorText.text = enemy.armor > 0 ? $"Armor: {enemy.armor}" : "";
            }

            if (bossIndicator != null)
            {
                bossIndicator.gameObject.SetActive(enemy.isBoss);
            }

            if (selectButton != null)
            {
                selectButton.interactable = !enemy.IsDead;

                var outline = GetComponent<Outline>();
                if (outline != null)
                {
                    outline.effectColor = isSelected ? Color.yellow : Color.clear;
                    outline.enabled = isSelected;
                }
            }
        }

        public int Index => _index;
    }
}
