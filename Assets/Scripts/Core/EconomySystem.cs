using System;
using UnityEngine;

namespace WanjieScroll.Core
{
    public enum JadeSource
    {
        StageClear,
        CashOut,
        Overkill,
        Card,
        Seal,
        Special
    }

    public enum EconomyPriceTier
    {
        Tier1,
        Tier2,
        Tier3,
        Tier4,
        Tier5
    }

    [Serializable]
    public sealed class EconomyConfig
    {
        public int normalEncounterJade = 0;
        public int stageClearJade = 0;
        public int cashOutJade = 200;

        public int metaGreedPerRank = 25;
        public int metaGreedMaxRank = 4;
        public int greedSealGain = 20;
        public int runGreedMax = 1000;

        public int smallJadeBag = 10;
        public int bigJadeBag = 25;
        public int temporaryGreedGain = 10;

        public float overkillConversion = 1f;
        public int overkillCap = 500;
        public int ultraOverkillCap = 5000;

        public int priceTier1 = 250;
        public int priceTier2 = 500;
        public int priceTier3 = 1000;
        public int priceTier4 = 2000;
        public int priceTier5 = 4000;

        public int GetPrice(EconomyPriceTier tier)
        {
            return tier switch
            {
                EconomyPriceTier.Tier1 => priceTier1,
                EconomyPriceTier.Tier2 => priceTier2,
                EconomyPriceTier.Tier3 => priceTier3,
                EconomyPriceTier.Tier4 => priceTier4,
                _ => priceTier5
            };
        }
    }

    public readonly struct JadeGainResult
    {
        public readonly JadeSource Source;
        public readonly int BaseJade;
        public readonly int FinalJade;
        public readonly bool AffectedByGreed;

        public JadeGainResult(JadeSource source, int baseJade, int finalJade, bool affectedByGreed)
        {
            Source = source;
            BaseJade = baseJade;
            FinalJade = finalJade;
            AffectedByGreed = affectedByGreed;
        }
    }

    /// <summary>所有灵玉入口的唯一倍率与记账实现。</summary>
    public sealed class EconomySystem
    {
        private readonly Action<int> _jadeSink;
        private int _overkillBaseGranted;

        public EconomyConfig Config { get; }
        public RunProgressionState RunState { get; }
        public event Action<JadeGainResult> JadeGranted;

        public EconomySystem(EconomyConfig config = null, RunProgressionState runState = null,
            Action<int> jadeSink = null)
        {
            Config = config ?? new EconomyConfig();
            RunState = runState ?? new RunProgressionState();
            _jadeSink = jadeSink;
        }

        public static int CalculateFinalJade(int baseJade, int greed, bool affectedByGreed)
        {
            int safeBase = Math.Max(0, baseJade);
            if (!affectedByGreed) return safeBase;
            return Mathf.FloorToInt(safeBase * (1f + Math.Max(0, greed) / 100f));
        }

        public JadeGainResult Grant(
            int baseJade,
            int currentGreed,
            JadeSource source,
            bool affectedByGreed = true,
            bool recordInRun = true)
        {
            int safeBase = Math.Max(0, baseJade);
            int final = CalculateFinalJade(safeBase, currentGreed, affectedByGreed);
            if (final > 0)
            {
                if (recordInRun)
                    RunState.RecordJade(final);
                _jadeSink?.Invoke(final);
            }
            var result = new JadeGainResult(source, safeBase, final, affectedByGreed);
            JadeGranted?.Invoke(result);
            return result;
        }

        /// <summary>先限制余威基础值，再应用敛玉倍率。</summary>
        public JadeGainResult GrantOverkillDamage(int overflowDamage, int currentGreed, bool ultra)
        {
            int cap = Math.Max(0, ultra ? Config.ultraOverkillCap : Config.overkillCap);
            int remaining = Math.Max(0, cap - _overkillBaseGranted);
            int converted = Mathf.FloorToInt(Math.Max(0, overflowDamage) * Math.Max(0f, Config.overkillConversion));
            int accepted = Math.Min(remaining, converted);
            _overkillBaseGranted += accepted;
            return Grant(accepted, currentGreed, JadeSource.Overkill, true);
        }

        public JadeGainResult CashOut(int currentGreed)
            => Grant(Config.cashOutJade, currentGreed, JadeSource.CashOut, true);

        public void ResetEncounter()
        {
            _overkillBaseGranted = 0;
        }
    }
}
