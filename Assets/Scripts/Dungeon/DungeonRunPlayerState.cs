using System;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    public enum DungeonRunManaStateOrigin
    { NotEstablished = 0, RunEntry = 1, LegacyUnknown = 2, LegacyReconstructed = 3, CombatCaptured = 4 }
    /// <summary>旧档没有入口属性，不能声称已经恢复了原始构筑。</summary>
    public enum DungeonRunPlayerBaselineOrigin
    {
        NotEstablished = 0,
        RunEntry = 1,
        LegacyMissing = 2,
        LegacyReconstructed = 3
    }

    /// <summary>
    /// Run 玩家状态的唯一 owner：首次入口基线、可持久化生命、旧档缺失语义和
    /// 战斗/地图投影都在此收敛。入口必须尚未叠加 Run modifier；之后只回写
    /// 战斗生命和独立 CurrentMana 地图余额，绝不从战斗 PlayerStats 反推基线，
    /// 不保存 TemporaryMana、临时护甲或属性增益。地图余额不覆盖 Core 回合恢复规则。
    /// 首次建立入口基线时把凝神交给 Run 储备 owner 一次；保留原始快照
    /// 供追溯，后续投影不把这个一次性加值重新发给战斗或地图副本。
    /// RunProgression 与楼层 modifier 仍由原 owner 持有，不复制进这里。
    /// AdditionalStats与RevivalReserve仅参与只读投影；临时战斗属性不能回写
    /// 其账本，重入场也不能由历史入口还魂再次补充已消耗的次数。
    /// </summary>
    internal sealed class DungeonRunPlayerState
    {
        private DungeonRunPlayerBaselineSaveData _baseline;

        public bool HasPlayerState { get; private set; }
        public bool HasBaseline => _baseline != null;
        public int MaxHealth { get; private set; } = 1;
        public int CurrentHealth { get; private set; } = 1;
        public DungeonRunPlayerBaselineOrigin Origin { get; private set; }
        public bool HasManaState { get; private set; }
        public int MapCurrentMana { get; private set; }
        public DungeonRunManaStateOrigin ManaOrigin { get; private set; }

        public static DungeonRunPlayerState Restore(DungeonRunSaveData data, string payload = null)
        {
            if (data.schemaVersion >= 26)
            {
                if (payload == null || !JsonObjectShape.TryGetField(payload,"hasManaState",out var presence) || presence != (data.hasManaState ? "true" : "false") ||
                    !JsonObjectShape.TryGetField(payload,"mapCurrentMana",out var current) ||
                    !int.TryParse(current,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out int wireCurrent) || wireCurrent != data.mapCurrentMana ||
                    !JsonObjectShape.TryGetField(payload,"manaStateOrigin",out var origin) ||
                    !int.TryParse(origin,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out int wireOrigin) || wireOrigin != (int)data.manaStateOrigin)
                    throw new InvalidOperationException("Schema 26 requires explicit map Mana wire fields.");
            }
            var state = new DungeonRunPlayerState
            {
                HasPlayerState = data.hasPlayerState,
                MaxHealth = Math.Max(1, data.maxHealth)
            };
            state.CurrentHealth = Math.Max(0, Math.Min(state.MaxHealth, data.currentHealth));
            if (data.schemaVersion < 26)
            {
                if (data.hasManaState || data.mapCurrentMana != 0 || data.manaStateOrigin != 0)
                    throw new InvalidOperationException("Legacy Run cannot contain a fabricated map Mana balance.");
                state.ManaOrigin = DungeonRunManaStateOrigin.LegacyUnknown;
            }
            else
            {
                bool known = data.manaStateOrigin == DungeonRunManaStateOrigin.RunEntry ||
                    data.manaStateOrigin == DungeonRunManaStateOrigin.LegacyReconstructed || data.manaStateOrigin == DungeonRunManaStateOrigin.CombatCaptured;
                if (!Enum.IsDefined(typeof(DungeonRunManaStateOrigin), data.manaStateOrigin) || known != data.hasManaState ||
                    data.mapCurrentMana < 0 || (!known && data.mapCurrentMana != 0) || (known && !data.hasPlayerState))
                    throw new InvalidOperationException("Run map Mana balance/origin is invalid.");
                state.HasManaState = known; state.MapCurrentMana = data.mapCurrentMana; state.ManaOrigin = data.manaStateOrigin;
            }
            if (data.schemaVersion < 14)
            {
                // v1-v13 只保存生命。无法从当前楼层、旧牌组或现有账号反推出
                // 当时的初始属性；兼容加载后显式保留缺失状态，等待一次入口重建。
                state.Origin = data.hasPlayerState
                    ? DungeonRunPlayerBaselineOrigin.LegacyMissing
                    : DungeonRunPlayerBaselineOrigin.NotEstablished;
                return state;
            }

            if (!Enum.IsDefined(typeof(DungeonRunPlayerBaselineOrigin), data.playerBaselineOrigin))
                throw new InvalidOperationException("Run 玩家基线来源无效。");
            bool recorded = data.playerBaselineOrigin == DungeonRunPlayerBaselineOrigin.RunEntry ||
                data.playerBaselineOrigin == DungeonRunPlayerBaselineOrigin.LegacyReconstructed;
            if (recorded != data.hasPlayerBaseline || (recorded && data.playerBaseline == null) ||
                (recorded && !data.hasPlayerState) ||
                (data.playerBaselineOrigin == DungeonRunPlayerBaselineOrigin.LegacyMissing && !data.hasPlayerState))
                throw new InvalidOperationException("Run 玩家基线与来源/生命状态不一致。");
            state.Origin = data.playerBaselineOrigin;
            // Unity 的内联类序列化不保证保留 null，必须以显式存在位为准。
            state._baseline = recorded ? data.playerBaseline.Clone() : null;
            return state;
        }

        public void Prepare(PlayerStats player, RunProgressionState progression)
        {
            if (progression == null) throw new ArgumentNullException(nameof(progression));
            if (_baseline == null)
            {
                _baseline = DungeonRunPlayerBaselineSaveData.Capture(player);
                // The immutable entry snapshot still records the account value,
                // but Duration is admitted only here, once for the entire Run.
                // Reprojection and save restore must never regrant it.
                progression.CrawlerDurationReserve.Bank(Math.Max(0, _baseline.ningShen));
                Origin = Origin == DungeonRunPlayerBaselineOrigin.LegacyMissing
                    ? DungeonRunPlayerBaselineOrigin.LegacyReconstructed
                    : DungeonRunPlayerBaselineOrigin.RunEntry;
            }
            // Revival is admitted once, independently of later combat copies.
            // Re-entry cannot replenish spent lives from the immutable baseline.
            progression.RevivalReserve.InitializeOnce(Math.Max(0, _baseline.hunHun));
            if (!HasPlayerState) CaptureCombatHealth(player);
            TryProject(player, progression);
        }

        /// <summary>只读投影；未建立/旧档缺失时不采纳调用方默认值，也不改变它。</summary>
        public bool TryProject(PlayerStats player, RunProgressionState progression = null)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (_baseline == null) return false;
            _baseline.ApplyTo(player);
            // Duration has a one-use Run owner rather than a combat baseline.
            player.ningShen = 0;
            player.maxHealth = MaxHealth;
            player.currentHealth = CurrentHealth;
            if (progression != null)
            {
                player.amount = progression.AdditionalStats.ProjectAmount(_baseline.amount);
                player.yinJuan = progression.AdditionalStats.ProjectMagnet(_baseline.yinJuan);
                player.zhaoSha = progression.AdditionalStats.ProjectCurse(_baseline.zhaoSha);
                player.hunHun = progression.RevivalReserve.RemainingPercent;
            }
            // armor 是本战状态：不保存、也不覆盖当前入口 freshly 应用的
            // scenario/meta 初始护甲；调用方为每战创建玩家，不回用上一战副本。
            return true;
        }

        public void CaptureCombatHealth(PlayerStats player)
        {
            MaxHealth = Math.Max(1, player.maxHealth);
            CurrentHealth = Math.Max(0, Math.Min(MaxHealth, player.currentHealth));
            HasPlayerState = true;
        }

        // Only the map balance is persisted. Core remains the unique live combat
        // Mana owner; neither TemporaryMana nor combat BaseMana is a Run baseline.
        internal void PrepareMapMana(int authoritativeStartingMana)
        {
            if (authoritativeStartingMana < 0) throw new ArgumentOutOfRangeException(nameof(authoritativeStartingMana));
            if (HasManaState) return;
            MapCurrentMana = authoritativeStartingMana; HasManaState = true;
            ManaOrigin = ManaOrigin == DungeonRunManaStateOrigin.LegacyUnknown
                ? DungeonRunManaStateOrigin.LegacyReconstructed : DungeonRunManaStateOrigin.RunEntry;
        }
        internal void CaptureCombatMana(ManaSystem mana)
        {
            if (mana == null || mana.CurrentMana < 0) throw new InvalidOperationException("Combat Mana balance is invalid.");
            MapCurrentMana = mana.CurrentMana;
            if (!HasManaState) ManaOrigin = DungeonRunManaStateOrigin.CombatCaptured;
            HasManaState = true;
        }
        internal void SetMapManaAfterPickup(int value)
        {
            if (!HasManaState || value < 0) throw new InvalidOperationException("Known map Mana is required for pickup.");
            MapCurrentMana = value;
        }

        public bool TryHeal(int amount, out int before, out int after, out string error)
        {
            before = after = CurrentHealth;
            error = null;
            if (amount <= 0) { error = "恢复生命的 amount 必须大于 0。"; return false; }
            if (!HasPlayerState) { error = "Run 尚未建立玩家生命状态。"; return false; }
            after = CurrentHealth = (int)Math.Min(MaxHealth, (long)CurrentHealth + amount);
            return true;
        }

#if UNITY_EDITOR
        public bool TryEnsureTransientMinimumHealth(int minimumHealth, out string error)
        {
            error = null;
            if (minimumHealth <= 0) { error = "瞬态入口最低生命必须大于 0。"; return false; }
            if (!HasPlayerState) { error = "Run 尚未建立玩家生命状态。"; return false; }
            CurrentHealth = Math.Max(CurrentHealth, Math.Min(MaxHealth, minimumHealth));
            return true;
        }
#endif

        // Detached rollback state; adoption preserves this Run's player-state owner.
        internal DungeonRunPlayerState CaptureTransactionState() => new DungeonRunPlayerState
        {
            _baseline = _baseline?.Clone(), HasPlayerState = HasPlayerState,
            MaxHealth = MaxHealth, CurrentHealth = CurrentHealth, Origin = Origin,
            HasManaState = HasManaState, MapCurrentMana = MapCurrentMana, ManaOrigin = ManaOrigin
        };

        internal void AdoptPreparedState(DungeonRunPlayerState previous)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            _baseline = previous._baseline?.Clone();
            HasPlayerState = previous.HasPlayerState;
            MaxHealth = previous.MaxHealth;
            CurrentHealth = previous.CurrentHealth;
            Origin = previous.Origin;
            HasManaState = previous.HasManaState;
            MapCurrentMana = previous.MapCurrentMana;
            ManaOrigin = previous.ManaOrigin;
        }

        public DungeonRunPlayerBaselineSaveData CaptureBaseline() => _baseline?.Clone();
    }
}
