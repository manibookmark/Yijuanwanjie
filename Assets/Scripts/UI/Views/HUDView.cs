using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// HUD 视图（玩家状态/敌人列表/法力/Boss计时器/英灵）
    /// GDD 11.5: 印章风格按钮，卷轴/竹简背景
    /// </summary>
    public class HUDView : UIView
    {
        [Header("玩家状态")]
        public Image healthBar;
        public Text healthText;
        public Text armorText;
        public Image healthBarBackground;

        [Header("法力")]
        public RectTransform manaOrbContainer;
        public GameObject manaOrbPrefab;
        public Text manaText;

        [Header("敌人")]
        public RectTransform enemyContainer;
        public GameObject enemyBarPrefab;

        [Header("Boss计时器")]
        public RectTransform bossEyeContainer;
        public GameObject eyeOpenPrefab;
        public GameObject eyeClosedPrefab;
        public GameObject bossTimerPanel;

        [Header("英灵")]
        public RectTransform crawlerContainer;
        public GameObject crawlerTagPrefab;

        [Header("等级经验")]
        public Text levelText;
        public Image xpBar;

        [Header("灵玉")]
        public Text jadeText;
        public RectTransform jadeDisplay;

        private HUDViewModel _hudVM;
        private Image[] _manaOrbs;
        private GameObject[] _bossEyes;

        private void Awake()
        {
            // 灵玉只属于地图经济层，战斗 HUD 不显示钱包，避免与战斗资源混淆。
            // 地图上的 ExplorationHUD 负责显示并播放收集动画。
            if (jadeDisplay != null)
                jadeDisplay.gameObject.SetActive(false);
            jadeDisplay = null;
            jadeText = null;
        }

        protected override void OnViewModelChanged(string propertyName)
        {
            if (propertyName == "*" || propertyName == HUDViewModel.PROP_PLAYER)
                RefreshPlayer();
            if (propertyName == "*")
                RefreshJade();
            if (propertyName == "*" || propertyName == HUDViewModel.PROP_ENEMIES)
                RefreshEnemies();
            if (propertyName == "*" || propertyName == HUDViewModel.PROP_MANA)
                RefreshMana();
            if (propertyName == "*" || propertyName == HUDViewModel.PROP_BOSS_TIMER)
                RefreshBossTimer();
            if (propertyName == "*" || propertyName == HUDViewModel.PROP_CRAWLERS)
                RefreshCrawlers();
        }

        public override void Bind(ViewModelBase viewModel)
        {
            base.Bind(viewModel);
            _hudVM = viewModel as HUDViewModel;
            RefreshJade();
        }

        public void RefreshJade()
        {
            // Intentionally empty in combat; jade is a map-only presentation.
        }

        public void PlayJadeGain(int amount)
        {
            // No wallet animation in battle.
        }

        /// <summary>
        /// 刷新玩家状态
        /// </summary>
        private void RefreshPlayer()
        {
            if (_hudVM == null) return;

            // 血条
            if (healthBar != null)
            {
                healthBar.fillAmount = _hudVM.HPRatio;
                healthBar.color = _hudVM.HPRatio < 0.3f ? Color.red :
                                  _hudVM.HPRatio < 0.6f ? new Color(1f, 0.6f, 0f) : new Color(0.8f, 0.2f, 0.2f);
            }

            if (healthText != null)
            {
                healthText.text = $"{_hudVM.PlayerHP} / {_hudVM.PlayerMaxHP}";
            }

            if (armorText != null)
            {
                armorText.text = _hudVM.PlayerArmor > 0 ? $"护甲 {_hudVM.PlayerArmor}" : "";
            }

            // 等级经验
            if (levelText != null)
            {
                levelText.text = $"Lv.{_hudVM.Level}";
            }

            if (xpBar != null)
            {
                xpBar.fillAmount = _hudVM.XPRatio;
            }
        }

        /// <summary>
        /// 刷新敌人列表
        /// </summary>
        private void RefreshEnemies()
        {
            if (_hudVM == null || enemyContainer == null) return;

            // 清除旧的
            for (int i = enemyContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(enemyContainer.GetChild(i).gameObject);
            }

            // 创建新的
            var enemies = _hudVM.Enemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (enemy.isDead) continue;

                var go = CreateEnemyBar(enemy);
                go.transform.SetParent(enemyContainer, false);
                go.transform.localPosition = new Vector3(0, -i * 40, 0);
            }
        }

        private GameObject CreateEnemyBar(EnemyDisplayData enemy)
        {
            var go = new GameObject($"Enemy_{enemy.name}", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200, 30);

            // 名称
            var nameObj = new GameObject("Name", typeof(Text)).GetComponent<RectTransform>();
            nameObj.SetParent(go.transform, false);
            nameObj.anchorMin = new Vector2(0, 0);
            nameObj.anchorMax = new Vector2(0.5f, 1);
            nameObj.offsetMin = Vector2.zero;
            nameObj.offsetMax = Vector2.zero;
            var nameText = nameObj.GetComponent<Text>();
            nameText.text = enemy.name + (enemy.isBoss ? " [Boss]" : "");
            nameText.fontSize = 14;
            nameText.color = enemy.isBoss ? new Color(0.8f, 0.2f, 0.2f) : Color.black;
            nameText.alignment = TextAnchor.MiddleLeft;

            // HP 文字
            var hpObj = new GameObject("HP", typeof(Text)).GetComponent<RectTransform>();
            hpObj.SetParent(go.transform, false);
            hpObj.anchorMin = new Vector2(0.5f, 0);
            hpObj.anchorMax = new Vector2(1f, 1);
            hpObj.offsetMin = Vector2.zero;
            hpObj.offsetMax = Vector2.zero;
            var hpText = hpObj.GetComponent<Text>();
            hpText.text = $"{enemy.currentHP}/{enemy.baseHP}" + (enemy.armor > 0 ? $" 盾{enemy.armor}" : "");
            hpText.fontSize = 12;
            hpText.color = Color.red;
            hpText.alignment = TextAnchor.MiddleRight;

            return go;
        }

        /// <summary>
        /// 刷新法力
        /// </summary>
        private void RefreshMana()
        {
            if (_hudVM == null) return;

            if (manaText != null)
            {
                manaText.text = _hudVM.ManaTemp > 0
                    ? $"{_hudVM.ManaCurrent}(+{_hudVM.ManaTemp}) / {_hudVM.ManaMax}"
                    : $"{_hudVM.ManaCurrent} / {_hudVM.ManaMax}";
            }

            // 法力球
            if (manaOrbContainer != null)
            {
                int max = _hudVM.ManaMax + _hudVM.ManaTemp;
                if (_manaOrbs == null || _manaOrbs.Length != max)
                {
                    // 重建
                    for (int i = manaOrbContainer.childCount - 1; i >= 0; i--)
                        Destroy(manaOrbContainer.GetChild(i).gameObject);

                    _manaOrbs = new Image[max];
                    for (int i = 0; i < max; i++)
                    {
                        var orb = new GameObject($"ManaOrb_{i}", typeof(Image)).GetComponent<RectTransform>();
                        orb.SetParent(manaOrbContainer, false);
                        orb.sizeDelta = new Vector2(30, 30);
                        orb.localPosition = new Vector3(i * 35, 0, 0);
                        _manaOrbs[i] = orb.GetComponent<Image>();
                        _manaOrbs[i].color =
                            SpiritEnergyColorPalette.ManaFull;
                    }
                }

                int current = _hudVM.ManaCurrent + _hudVM.ManaTemp;
                int baseMax = _hudVM.ManaMax;
                for (int i = 0; i < _manaOrbs.Length; i++)
                {
                    if (_manaOrbs[i] == null) continue;
                    bool isFilled = i < current;
                    bool isTemp = i >= baseMax;

                    if (isFilled)
                    {
                        _manaOrbs[i].color = isTemp
                            ? SpiritEnergyColorPalette.ManaHighlight
                            : SpiritEnergyColorPalette.ManaFull;
                    }
                    else
                    {
                        _manaOrbs[i].color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                    }
                }
            }
        }

        /// <summary>
        /// 刷新 Boss 计时器
        /// </summary>
        private void RefreshBossTimer()
        {
            if (_hudVM == null) return;

            bool hasTimer = _hudVM.HasBossTimer;
            if (bossTimerPanel != null)
                bossTimerPanel.SetActive(hasTimer);

            if (!hasTimer || bossEyeContainer == null) return;

            int total = _hudVM.BossEyesTotal;

            // 创建/更新劫眼
            if (_bossEyes == null || _bossEyes.Length != total)
            {
                for (int i = bossEyeContainer.childCount - 1; i >= 0; i--)
                    Destroy(bossEyeContainer.GetChild(i).gameObject);

                _bossEyes = new GameObject[total];
                for (int i = 0; i < total; i++)
                {
                    var eye = new GameObject($"Eye_{i}", typeof(Image)).GetComponent<RectTransform>();
                    eye.SetParent(bossEyeContainer, false);
                    eye.sizeDelta = new Vector2(25, 25);
                    eye.localPosition = new Vector3(i * 30, 0, 0);
                    _bossEyes[i] = eye.gameObject;
                }
            }

            bool[] eyes = _hudVM.BossEyes;
            for (int i = 0; i < _bossEyes?.Length; i++)
            {
                if (_bossEyes[i] == null) continue;
                var img = _bossEyes[i].GetComponent<Image>();
                if (img == null) continue;

                bool isOpen = i < eyes.Length && eyes[i];
                img.color = isOpen ? new Color(0.9f, 0.2f, 0.1f) : new Color(0.1f, 0.1f, 0.1f, 0.5f);
            }
        }

        /// <summary>
        /// 刷新英灵列表
        /// </summary>
        private void RefreshCrawlers()
        {
            if (_hudVM == null || crawlerContainer == null) return;

            for (int i = crawlerContainer.childCount - 1; i >= 0; i--)
                Destroy(crawlerContainer.GetChild(i).gameObject);

            var crawlers = _hudVM.Crawlers;
            for (int i = 0; i < crawlers.Count; i++)
            {
                var crawler = crawlers[i];
                var go = new GameObject($"Crawler_{crawler.name}", typeof(Text)).GetComponent<RectTransform>();
                go.SetParent(crawlerContainer, false);
                go.localPosition = new Vector3(0, -i * 25, 0);
                go.sizeDelta = new Vector2(150, 20);

                var text = go.GetComponent<Text>();
                text.text = $"◆ {crawler.name}({crawler.remainingTriggers})";
                text.fontSize = 12;
                text.color = CardColorPalette.GetMainColor(crawler.triggerColor);
                text.alignment = TextAnchor.MiddleLeft;
            }
        }
    }
}
