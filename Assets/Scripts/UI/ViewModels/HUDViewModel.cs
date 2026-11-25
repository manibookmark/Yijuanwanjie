using System;
using System.Collections.Generic;
using WanjieScroll.Core;


namespace WanjieScroll.UI
{
    /// <summary>
    /// HUD ViewModel
    /// 管理玩家状态、敌人列表、法力显示、Boss计时器、英灵列表
    /// </summary>
    public class HUDViewModel : ViewModelBase
    {
        private CombatController _combat;
        private List<EnemyDisplayData> _enemyList = new List<EnemyDisplayData>();
        private List<CrawlerDisplayData> _crawlerList = new List<CrawlerDisplayData>();
        private bool[] _bossEyes;

        public const string PROP_PLAYER = "Player";
        public const string PROP_ENEMIES = "Enemies";
        public const string PROP_MANA = "Mana";
        public const string PROP_BOSS_TIMER = "BossTimer";
        public const string PROP_CRAWLERS = "Crawlers";

        // ====== 玩家状态 ======
        public int PlayerHP => _combat?.Player.currentHealth ?? 0;
        public int PlayerMaxHP => _combat?.Player.maxHealth ?? 100;
        public int PlayerArmor => _combat?.Player.armor ?? 0;
        public float HPRatio => PlayerMaxHP > 0 ? (float)PlayerHP / PlayerMaxHP : 0f;

        // ====== 法力 ======
        public int ManaCurrent => _combat?.Mana.CurrentMana ?? 0;
        public int ManaTemp => _combat?.Mana.TemporaryMana ?? 0;
        public int ManaMax => _combat?.Mana.BaseMana ?? 0;

        // ====== 敌人 ======
        public IReadOnlyList<EnemyDisplayData> Enemies => _enemyList;
        public int EnemyCount => _enemyList.Count;
        public bool HasBoss
        {
            get
            {
                for (int i = 0; i < _enemyList.Count; i++)
                    if (_enemyList[i].isBoss) return true;
                return false;
            }
        }

        // ====== Boss 计时器 ======
        public bool HasBossTimer => _combat?.IsBossTimerActive == true;
        public bool[] BossEyes => _bossEyes;
        public int BossEyesOpen => _combat?.BossTimer.OpenEyes ?? 0;
        public int BossEyesTotal => _combat?.BossTimer.TotalEyes ?? 0;
        public bool BossTimerTriggered => _combat?.BossTimer.IsTriggered ?? false;

        // ====== 英灵 ======
        public IReadOnlyList<CrawlerDisplayData> Crawlers => _crawlerList;
        public int CrawlerCount => _crawlerList.Count;

        // ====== 等级/经验 ======
        public int Level => _combat?.CurrentLevel ?? 1;
        public int XP => _combat?.CurrentXP ?? 0;
        public int XPRequired => _combat?.RequiredXP ??
                                 LevelSystem.GetRequiredXP(Level);
        public float XPRatio => XPRequired > 0 ? (float)XP / XPRequired : 0f;

        /// <summary>
        /// 绑定战斗控制器
        /// </summary>
        public void Bind(CombatController combat)
        {
            _combat = combat;
            _combat.OnStateChanged += OnCombatStateChanged;
            _combat.OnEnemyKilled += OnEnemyKilled;

            if (_combat.BossTimer != null)
            {
                _combat.BossTimer.OnEyesChanged += OnBossEyesChanged;
                _bossEyes = new bool[_combat.BossTimer.TotalEyes];
            }

            Refresh();
            Log("✓ HUDViewModel 已绑定");
        }

        public override void Dispose()
        {
            if (_combat != null)
            {
                _combat.OnStateChanged -= OnCombatStateChanged;
                _combat.OnEnemyKilled -= OnEnemyKilled;

                if (_combat.BossTimer != null)
                {
                    _combat.BossTimer.OnEyesChanged -= OnBossEyesChanged;
                }
                _combat = null;
            }
            base.Dispose();
        }

        private void OnCombatStateChanged(CombatStateChangeReason reason)
        {
            Refresh();
        }

        private void OnEnemyKilled(EnemyStats enemy)
        {
            Refresh();
        }

        private void OnBossEyesChanged()
        {
            RefreshBossEyes();
            RaisePropertyChanged(PROP_BOSS_TIMER);
        }

        /// <summary>
        /// 刷新所有数据
        /// </summary>
        public void Refresh()
        {
            if (_combat == null) return;

            // 敌人列表
            _enemyList.Clear();
            foreach (var enemy in _combat.Enemies)
            {
                if (!enemy.IsDeployed) continue;
                _enemyList.Add(EnemyDisplayData.FromEnemy(enemy));
            }

            // 英灵列表
            _crawlerList.Clear();
            if (_combat.ColorTrigger != null)
            {
                foreach (var ac in _combat.ColorTrigger.ActiveCrawlers)
                {
                    _crawlerList.Add(CrawlerDisplayData.FromCrawler(ac));
                }
            }

            // Boss 劫眼
            RefreshBossEyes();

            RaisePropertyChanged(PROP_PLAYER);
            RaisePropertyChanged(PROP_ENEMIES);
            RaisePropertyChanged(PROP_MANA);
            RaisePropertyChanged(PROP_BOSS_TIMER);
            RaisePropertyChanged(PROP_CRAWLERS);
        }

        private void RefreshBossEyes()
        {
            if (_combat?.BossTimer == null) return;

            if (_bossEyes == null || _bossEyes.Length != _combat.BossTimer.TotalEyes)
            {
                _bossEyes = new bool[_combat.BossTimer.TotalEyes];
            }

            for (int i = 0; i < _bossEyes.Length; i++)
            {
                _bossEyes[i] = i < _combat.BossTimer.OpenEyes;
            }
        }

        public override void RefreshAll()
        {
            Refresh();
            base.RefreshAll();
        }
    }
}
