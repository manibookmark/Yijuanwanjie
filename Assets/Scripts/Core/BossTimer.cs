using System;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 天劫倒计时（Boss 计时器）
    /// Boss 头顶一排"劫眼"，每打出一张牌/结束回合睁开一只
    /// 全部睁开 → Boss 释放天劫攻击
    /// </summary>
    public class BossTimer
    {
        /// <summary>
        /// 劫眼总数
        /// </summary>
        public int TotalEyes { get; private set; }

        /// <summary>
        /// 已睁开的劫眼数
        /// </summary>
        public int OpenEyes { get; private set; }

        /// <summary>
        /// 天劫是否即将释放（全部睁开）
        /// </summary>
        public bool IsTriggered => OpenEyes >= TotalEyes;

        /// <summary>
        /// 天劫攻击力
        /// </summary>
        public int Damage { get; private set; }

        public event Action OnEyesChanged;
        public event Action OnTianjieTriggered;

        public BossTimer(int totalEyes, int damage)
        {
            TotalEyes = totalEyes;
            OpenEyes = 0;
            Damage = damage;
        }

        /// <summary>
        /// 出一张牌 → 睁开一只劫眼
        /// </summary>
        public void OnCardPlayed()
        {
            if (IsTriggered) return;

            OpenEyes++;
            OnEyesChanged?.Invoke();

            if (IsTriggered)
            {
                OnTianjieTriggered?.Invoke();
            }
        }

        /// <summary>
        /// 回合结束 → 睁开一只劫眼
        /// </summary>
        public void OnTurnEnd()
        {
            if (IsTriggered) return;

            OpenEyes++;
            OnEyesChanged?.Invoke();

            if (IsTriggered)
            {
                OnTianjieTriggered?.Invoke();
            }
        }

        /// <summary>
        /// 冰封效果：重置劫眼
        /// </summary>
        public void Freeze()
        {
            OpenEyes = 0;
            OnEyesChanged?.Invoke();
        }

        /// <summary>
        /// 重置（新 Boss 或新回合）
        /// </summary>
        public void Reset()
        {
            OpenEyes = 0;
        }

        public string GetSummary()
        {
            string eyes = "";
            for (int i = 0; i < TotalEyes; i++)
            {
                eyes += i < OpenEyes ? "●" : "○";
            }
            return $"天劫: [{eyes}] {OpenEyes}/{TotalEyes}" + (IsTriggered ? " ⚠ 即将释放！" : "");
        }
    }
}
