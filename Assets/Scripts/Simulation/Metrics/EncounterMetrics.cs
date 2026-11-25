using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation
{
    /// <summary>
    /// Immutable per-card contribution captured from authoritative combat events.
    /// Draw counts are intentionally excluded: the draw event currently exposes
    /// runtime instance IDs only, so attributing them without a controller-owned
    /// identity map would be guesswork.
    /// </summary>
    public sealed class CardUsageMetric
    {
        internal CardUsageMetric(
            string contentId,
            int plays,
            int damage,
            int armor,
            int healing,
            int manaSpent,
            int manaGained,
            int directKills)
        {
            ContentId = contentId;
            Plays = plays;
            Damage = damage;
            Armor = armor;
            Healing = healing;
            ManaSpent = manaSpent;
            ManaGained = manaGained;
            DirectKills = directKills;
        }

        public string ContentId { get; }
        public int Plays { get; }
        public int Damage { get; }
        public int Armor { get; }
        public int Healing { get; }
        public int ManaSpent { get; }
        public int ManaGained { get; }
        /// <summary>Legacy name: plays with any immediate kill, not Direct-kind victims.</summary>
        public int DirectKills { get; }
    }

    /// <summary>Immutable per-enemy action contribution keyed by stable contentId.</summary>
    public sealed class EnemyActionUsageMetric
    {
        internal EnemyActionUsageMetric(
            string contentId,
            int actions,
            int attacks,
            int defends,
            int playerHealthDamage,
            int playerArmorDamage,
            int armorGained)
        {
            ContentId = contentId;
            Actions = actions;
            Attacks = attacks;
            Defends = defends;
            PlayerHealthDamage = playerHealthDamage;
            PlayerArmorDamage = playerArmorDamage;
            ArmorGained = armorGained;
        }

        public string ContentId { get; }
        public int Actions { get; }
        public int Attacks { get; }
        public int Defends { get; }
        public int PlayerHealthDamage { get; }
        public int PlayerArmorDamage { get; }
        public int ArmorGained { get; }
    }

    public sealed class RewardCandidateMetric
    {
        internal RewardCandidateMetric(
            int index,
            LevelUpSystem.Reward reward)
        {
            Index = index;
            Type = reward.type;
            Name = reward.name ?? string.Empty;
            CardContentId = reward.card?.contentId ?? string.Empty;
            StatType = reward.statType;
            StatAmount = reward.statAmount;
        }

        public int Index { get; }
        public LevelUpSystem.RewardType Type { get; }
        public string Name { get; }
        public string CardContentId { get; }
        public StatType StatType { get; }
        public int StatAmount { get; }
    }

    public sealed class RewardSelectionMetric
    {
        internal RewardSelectionMetric(
            IReadOnlyList<LevelUpSystem.Reward> rewards,
            int selectedIndex)
        {
            var candidates = new List<RewardCandidateMetric>(rewards.Count);
            for (int i = 0; i < rewards.Count; i++)
            {
                if (rewards[i] != null)
                    candidates.Add(new RewardCandidateMetric(i, rewards[i]));
            }
            Candidates = candidates.AsReadOnly();
            SelectedIndex = selectedIndex;
        }

        public IReadOnlyList<RewardCandidateMetric> Candidates { get; }
        public int SelectedIndex { get; }
        public RewardCandidateMetric Selected
        {
            get
            {
                for (int i = 0; i < Candidates.Count; i++)
                {
                    if (Candidates[i].Index == SelectedIndex)
                        return Candidates[i];
                }
                return null;
            }
        }
    }

    /// <summary>
    /// 单场 Encounter 模拟的结构化指标。
    /// 通过订阅 CombatController 事件收集，不解析 OnLog。
    /// Owns observed counters and their scoped subscriptions only; no combat
    /// rules or mutable Core state are duplicated here. Outcome telemetry stays
    /// in this existing observation boundary despite the number of counters.
    /// </summary>
    public sealed class EncounterMetrics
    {
        // Revision 0 (including absent fields in old JSON) means these counters
        // were not captured under the event-based contract. Do not interpret as zero.
        public const int CurrentTelemetryRevision = 1;
        public int TelemetryRevision { get; internal set; }

        private sealed class CardUsageAccumulator
        {
            public int Plays;
            public int Damage;
            public int Armor;
            public int Healing;
            public int ManaSpent;
            public int ManaGained;
            public int DirectKills;
        }

        private sealed class EnemyUsageAccumulator
        {
            public int Actions;
            public int Attacks;
            public int Defends;
            public int PlayerHealthDamage;
            public int PlayerArmorDamage;
            public int ArmorGained;
        }

        // ── 生存 ──
        public bool Win;
        public int StartingHP;
        public int EndingHP;
        public int HpLost;
        public int ArmorGenerated;
        public int ArmorAbsorbed;
        public int HealingDone;
        public int DamageTaken;

        // ── 节奏 ──
        public int Turns;
        public int CardsPlayed;
        public int EnemyActions;

        // ── 输出 ──
        public int TotalDamage;
        // All authoritative damage events, including delayed effects; excludes overkill.
        // TotalDamage and per-card Damage retain their legacy immediate-play semantics.
        public int AppliedDamage;
        public int CritDamage;
        public int DoTDamage;
        public int OverkillDamage;
        public int KillsByDirect;
        // Area means DamageKind.Splash, not any multi-target direct attack.
        public int KillsByArea;
        public int KillsByDoT;
        // Count actual enemy outcomes, not cards that happened to score a kill.
        public int KilledEnemies;
        // Bounce/Burn/etc. and non-damage defeats (e.g. Erase) remain observable
        // without claiming a more precise attribution than the Core receipt provides.
        public int KillsByOther => Math.Max(0,
            KilledEnemies - KillsByDirect - KillsByArea - KillsByDoT);
        public int EscapedEnemies;
        // Awarded enemy-kill XP after modifiers; excludes card/entry/event XP.
        public int EnemyExperienceEarned;

        // ── 资源 ──
        public int ManaSpent;
        public int CardsDrawn;

        // ── 连击 ──
        public int MaxCombo;
        // Cost-sequence mismatches only; excludes seal clearing and turn resets.
        public int ComboBreakCount;

        // ── 异常 ──
        public SimulationTerminationReason Termination;
        private readonly List<RewardSelectionMetric> _rewardSelections =
            new List<RewardSelectionMetric>();
        private readonly Dictionary<string, CardUsageAccumulator> _cardUsage =
            new Dictionary<string, CardUsageAccumulator>(StringComparer.Ordinal);
        private readonly Dictionary<int, string> _enemyContentIdsByRuntimeId =
            new Dictionary<int, string>();
        private readonly Dictionary<string, EnemyUsageAccumulator> _enemyUsage =
            new Dictionary<string, EnemyUsageAccumulator>(StringComparer.Ordinal);
        public IReadOnlyList<RewardSelectionMetric> RewardSelections =>
            _rewardSelections;
        public IReadOnlyList<CardUsageMetric> CardUsage => SnapshotCardUsage();
        public IReadOnlyList<EnemyActionUsageMetric> EnemyUsage =>
            SnapshotEnemyUsage();

        internal IDisposable Capture(CombatController combat)
        {
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            TelemetryRevision = CurrentTelemetryRevision;
            RegisterEnemies(combat.Enemies);
            return new MetricSubscription(this, combat);
        }

        // ── 收集器 ──
        public void OnCardResolved(CombatCardResolvedEvent e)
        {
            CardsPlayed++;
            TotalDamage += e.DamageDealt;
            ArmorGenerated += e.ArmorGained;
            HealingDone += e.HealAmount;
            ManaSpent += Math.Max(0, e.ManaCost);
            MaxCombo = Math.Max(MaxCombo, e.ComboCount);
            if (e.ChainWasBroken) ComboBreakCount++;

            string contentId = e.Card?.contentId;
            if (!string.IsNullOrWhiteSpace(contentId))
            {
                if (!_cardUsage.TryGetValue(
                        contentId,
                        out CardUsageAccumulator usage))
                {
                    usage = new CardUsageAccumulator();
                    _cardUsage.Add(contentId, usage);
                }
                usage.Plays++;
                usage.Damage += e.DamageDealt;
                usage.Armor += e.ArmorGained;
                usage.Healing += e.HealAmount;
                usage.ManaSpent += Math.Max(0, e.ManaCost);
                usage.ManaGained += e.ManaGained;
                if (e.EnemyKilled) usage.DirectKills++;
            }
        }

        public void OnCardsDrawn(CombatCardsDrawnEvent e)
        {
            CardsDrawn += e.Count;
        }

        public void OnEnemyAction(CombatEnemyActionResult e)
        {
            EnemyActions++;
            ArmorAbsorbed += e.PlayerArmorDamage;
            DamageTaken += e.PlayerHealthDamage;

            if (!_enemyContentIdsByRuntimeId.TryGetValue(
                    e.EnemyRuntimeId,
                    out string contentId) ||
                string.IsNullOrWhiteSpace(contentId))
            {
                return;
            }
            if (!_enemyUsage.TryGetValue(
                    contentId,
                    out EnemyUsageAccumulator usage))
            {
                usage = new EnemyUsageAccumulator();
                _enemyUsage.Add(contentId, usage);
            }
            usage.Actions++;
            if (e.Intent.Type == EnemyIntentType.Attack) usage.Attacks++;
            if (e.Intent.Type == EnemyIntentType.Defend) usage.Defends++;
            usage.PlayerHealthDamage += e.PlayerHealthDamage;
            usage.PlayerArmorDamage += e.PlayerArmorDamage;
            usage.ArmorGained += Math.Max(
                0,
                e.EnemyArmorAfter - e.EnemyArmorBefore);
        }

        public void OnDamageEvent(DamageEvent e)
        {
            AppliedDamage += e.AppliedDamage;
            OverkillDamage += e.OverkillDamage;
            if (e.IsCritical) CritDamage += e.AppliedDamage;
            if (e.Kind == DamageKind.DoT) DoTDamage += e.AppliedDamage;
            if (!e.IsFatal) return;
            switch (e.Kind)
            {
                case DamageKind.Direct: KillsByDirect++; break;
                case DamageKind.Splash: KillsByArea++; break;
                case DamageKind.DoT: KillsByDoT++; break;
            }
        }

        public void OnEnemyKilled(EnemyStats enemy)
        {
            KilledEnemies++;
        }

        public void OnEnemyWithdrawn(EnemyWithdrawnEvent e)
        {
            // Core publishes this only after a successful Escape command;
            // waiting, displacement and death do not publish withdrawals.
            EscapedEnemies++;
        }

        public void OnExperienceAwarded(CombatExperienceAward e)
        {
            EnemyExperienceEarned += e.ActualExperience;
        }

        public void OnCombatEnded(CombatEndedEvent e)
        {
            Win = e.Outcome == CombatOutcome.Victory;
            Termination = Win
                ? SimulationTerminationReason.Victory
                : SimulationTerminationReason.Defeat;
        }

        internal void RecordRewardSelection(
            IReadOnlyList<LevelUpSystem.Reward> rewards,
            int selectedIndex)
        {
            _rewardSelections.Add(new RewardSelectionMetric(
                rewards,
                selectedIndex));
        }

        internal void RegisterEnemies(IReadOnlyList<EnemyStats> enemies)
        {
            _enemyContentIdsByRuntimeId.Clear();
            if (enemies == null) return;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy == null || enemy.runtimeId <= 0 ||
                    string.IsNullOrWhiteSpace(enemy.contentId))
                {
                    continue;
                }
                _enemyContentIdsByRuntimeId[enemy.runtimeId] = enemy.contentId;
            }
        }

        private IReadOnlyList<CardUsageMetric> SnapshotCardUsage()
        {
            var ids = new List<string>(_cardUsage.Keys);
            ids.Sort(StringComparer.Ordinal);
            var result = new List<CardUsageMetric>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                CardUsageAccumulator usage = _cardUsage[id];
                result.Add(new CardUsageMetric(
                    id,
                    usage.Plays,
                    usage.Damage,
                    usage.Armor,
                    usage.Healing,
                    usage.ManaSpent,
                    usage.ManaGained,
                    usage.DirectKills));
            }
            return result.AsReadOnly();
        }

        private IReadOnlyList<EnemyActionUsageMetric> SnapshotEnemyUsage()
        {
            var ids = new List<string>(_enemyUsage.Keys);
            ids.Sort(StringComparer.Ordinal);
            var result = new List<EnemyActionUsageMetric>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                EnemyUsageAccumulator usage = _enemyUsage[id];
                result.Add(new EnemyActionUsageMetric(
                    id,
                    usage.Actions,
                    usage.Attacks,
                    usage.Defends,
                    usage.PlayerHealthDamage,
                    usage.PlayerArmorDamage,
                    usage.ArmorGained));
            }
            return result.AsReadOnly();
        }

        public override string ToString()
        {
            return $"[{(Win ? "WIN" : "LOSE")}] Turns={Turns} HP={EndingHP}/{StartingHP} " +
                   $"Dmg={TotalDamage} Kills={KilledEnemies} Combo={MaxCombo} " +
                   $"Mana={ManaSpent} Cards={CardsPlayed}";
        }

        private sealed class MetricSubscription : IDisposable
        {
            private readonly EncounterMetrics _metrics;
            private CombatController _combat;

            internal MetricSubscription(EncounterMetrics metrics, CombatController combat)
            {
                _metrics = metrics;
                _combat = combat;
                combat.OnCardPlayResolved += metrics.OnCardResolved;
                combat.OnCardsDrawn += metrics.OnCardsDrawn;
                combat.OnEnemyActionResolved += metrics.OnEnemyAction;
                combat.OnDamageEvent += metrics.OnDamageEvent;
                combat.OnCombatEnded += metrics.OnCombatEnded;
                combat.OnEnemyKilled += metrics.OnEnemyKilled;
                combat.OnEnemyWithdrawn += metrics.OnEnemyWithdrawn;
                combat.OnExperienceAwarded += metrics.OnExperienceAwarded;
            }

            public void Dispose()
            {
                if (_combat == null) return;
                _combat.OnCardPlayResolved -= _metrics.OnCardResolved;
                _combat.OnCardsDrawn -= _metrics.OnCardsDrawn;
                _combat.OnEnemyActionResolved -= _metrics.OnEnemyAction;
                _combat.OnDamageEvent -= _metrics.OnDamageEvent;
                _combat.OnCombatEnded -= _metrics.OnCombatEnded;
                _combat.OnEnemyKilled -= _metrics.OnEnemyKilled;
                _combat.OnEnemyWithdrawn -= _metrics.OnEnemyWithdrawn;
                _combat.OnExperienceAwarded -= _metrics.OnExperienceAwarded;
                _combat = null;
            }
        }
    }
}
