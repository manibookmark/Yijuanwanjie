using System;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Bootstrap
{
    /// <summary>把当前 Run 的只读修正叠加到本场新建玩家，不回写场景配置。</summary>
    public static class LevelRunCombatModifier
    {
        public static void ApplyToPlayer(
            PlayerStats player,
            LevelRunModifierSnapshot modifiers)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));
            // Run bonuses can already be int.MaxValue; clamp after widening the sum.
            player.faLi = (int)Math.Max(1L,
                Math.Min(int.MaxValue, (long)player.faLi + modifiers.BaseManaBonus));
            player.baseArmorPerTurn = (int)Math.Max(0L,
                Math.Min(int.MaxValue, (long)player.baseArmorPerTurn + modifiers.BaseArmorPerTurnBonus));
            player.fengRui = (int)Math.Max(0L,
                Math.Min(int.MaxValue, (long)player.fengRui + modifiers.MightBonus));
            player.bengLie = (int)Math.Max(0L,
                Math.Min(int.MaxValue, (long)player.bengLie + modifiers.AreaBonus));
            player.shouPai = (int)Math.Max(1L,
                Math.Min(int.MaxValue, (long)player.shouPai + modifiers.HandLimitBonus));
        }
    }
}
