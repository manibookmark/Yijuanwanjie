using System;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Mutable JsonUtility transport only. BalanceSimulationSuite immediately
    /// validates and freezes it so runtime callers never share these arrays.
    /// </summary>
    [Serializable]
    internal sealed class BalanceSuiteDocument
    {
        public int schemaVersion;
        public string suiteId;
        public int revision;
        public string floorContentId;
        public string floorResourcePath;
        public string snapshotId;
        public string coverageMode;
        public string routeExecution;
        public BalanceEntryStateDocument entryState;
        public BalanceEntryRuleDocument entryRule;
        public BalanceVersionedInputDocument agent;
        public BalanceVersionedInputDocument rewardPolicy;
        public BalanceEncounterDocument[] isolatedEncounters;
        public BalanceRouteDocument[] routes;
        public BalanceCoverageExclusionDocument[] coverageExclusions;
        public BalanceCheckGroupDocument[] checks;
    }

    [Serializable]
    internal sealed class BalanceEntryStateDocument
    {
        public string kind;
        public int revision;
        public string checkpointFile;
    }

    [Serializable]
    internal sealed class BalanceEntryRuleDocument
    {
        public string id;
        public int revision;
        public string selectedHeroContentId;
        public BalanceRuleDocument rules;
    }

    [Serializable]
    internal sealed class BalanceRuleDocument
    {
        public float knockbackBaseChance;
        public float disarmBaseChance;
        public bool preservePlayerArmorBetweenTurns;
        public bool shieldBashEnabled;
        public bool comboEnabled;
        public bool wuxingEnabled;
        public bool bossOverkillEnabled;
        public bool normalOverkillEnabled;
        public bool ultraOverkillEnabled;

        public CombatRuleSet CreateRules()
        {
            return new CombatRuleSet
            {
                knockbackBaseChance = knockbackBaseChance,
                disarmBaseChance = disarmBaseChance,
                preservePlayerArmorBetweenTurns =
                    preservePlayerArmorBetweenTurns,
                shieldBashEnabled = shieldBashEnabled,
                comboEnabled = comboEnabled,
                wuxingEnabled = wuxingEnabled,
                bossOverkillEnabled = bossOverkillEnabled,
                normalOverkillEnabled = normalOverkillEnabled,
                ultraOverkillEnabled = ultraOverkillEnabled
            };
        }
    }

    [Serializable]
    internal sealed class BalanceVersionedInputDocument
    {
        public string id;
        public int revision;
    }

    [Serializable]
    internal sealed class BalanceEncounterDocument
    {
        public string id;
        public string nodeContentId;
        public bool quick;
    }

    [Serializable]
    internal sealed class BalanceRouteDocument
    {
        public string id;
        public int revision;
        public string purpose;
        public bool quick;
        public string[] nodeContentIds;
        public BalanceRouteStepDocument[] steps;
    }

    [Serializable]
    internal sealed class BalanceRouteStepDocument
    {
        public string kind;
        public string nodeContentId;
        public string targetCardContentId;
        public int targetCardOccurrence;
        public string victorySealDisposition;
        public string victorySealContentId;
        public string cardConversionDisposition;
    }

    [Serializable]
    internal sealed class BalanceCoverageExclusionDocument
    {
        public string nodeContentId;
        public string reason;
    }

    [Serializable]
    internal sealed class BalanceCheckGroupDocument
    {
        public string id;
        public string mode;
        public string severity;
        public string description;
        public BalanceCheckConditionDocument[] conditions;
    }

    [Serializable]
    internal sealed class BalanceCheckConditionDocument
    {
        public string kind;
        public string scope;
        public string metric;
        public string comparison;
        public float value;
        public float upperValue;
        public string[] targetIds;
        public string baselineId;
        public string candidateId;
    }
}
