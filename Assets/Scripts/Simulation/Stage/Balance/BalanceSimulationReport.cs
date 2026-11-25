using System;
using System.Collections.Generic;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Stable normalized report contract. It stores aggregates and replay seeds,
    /// not mutable combat objects or log text.
    /// </summary>
    [Serializable]
    public sealed class BalanceSimulationReport
    {
        public const int CurrentSchemaVersion = 3;
        public const string EntryBuildScopeId =
            "production-scenario+checkpoint+suite-rules;" +
            "meta-progression=none;daozang-projection=none";
        public const string RouteSequenceSemanticsId =
            "design-sequence;map-prerequisites=informational";
        public const string ProductionRouteSemanticsId =
            "production-route;map-prerequisites=enforced";

        public int schemaVersion = CurrentSchemaVersion;
        public string status;
        public int exitCode;
        public string suiteId;
        public int suiteRevision;
        public string floorContentId;
        public string floorResourcePath;
        public string snapshotId;
        public string coverageMode;
        public string routeSequenceSemantics;
        public string preset;
        public int seedStart;
        public int countPerTarget;
        public int maxTurnsPerEncounter;
        public bool strictBalanceChecks;
        public string suiteSourceSha256;
        public string gitCommit;
        public string gitDirtyFingerprint;
        public string entryStateKind;
        public int entryStateRevision;
        public string entryCheckpointFile;
        public string entryCheckpointSha256;
        public string agentId;
        public int agentRevision;
        public string rewardPolicyId;
        public int rewardPolicyRevision;
        public string entryRuleId;
        public int entryRuleRevision;
        public string selectedHeroContentId;
        public string entryBuildScope;
        public int targetsExecuted;
        public int combatRunsExecuted;
        public bool hasBalanceTargets;
        public bool calibrationBlockedByCoverage;
        public string contentFingerprint;
        public List<BalanceCoverageGap> coverageGaps =
            new List<BalanceCoverageGap>();
        public List<BalanceLedgerEntry> configLedger =
            new List<BalanceLedgerEntry>();
        public List<BalanceEncounterAggregate> isolatedEncounters =
            new List<BalanceEncounterAggregate>();
        public List<BalanceRouteAggregate> routes =
            new List<BalanceRouteAggregate>();
        public List<BalanceEncounterAggregate> routeEncounters =
            new List<BalanceEncounterAggregate>();
        public List<BalanceCardAggregate> cards =
            new List<BalanceCardAggregate>();
        public List<BalanceEnemyAggregate> enemies =
            new List<BalanceEnemyAggregate>();
        public List<BalanceRewardAggregate> rewards =
            new List<BalanceRewardAggregate>();
        public List<BalanceFailureSample> failures =
            new List<BalanceFailureSample>();
        public List<BalanceCheckResult> checks =
            new List<BalanceCheckResult>();
        public List<BalanceRouteSample> routeSamples = new List<BalanceRouteSample>();
        public List<BalanceEncounterSample> encounterSamples = new List<BalanceEncounterSample>();
    }

    [Serializable]
    public sealed class BalanceCoverageGap
    {
        public string nodeContentId;
        public string nodeType;
        public bool affectsCombat;
        public bool blocksCalibration;
        public string configuredEffects;
        public string declaredReason;
    }

    [Serializable]
    public sealed class BalanceLedgerEntry
    {
        public string nodeContentId;
        public string scenarioId;
        public string itemKind;
        public string contentId;
        public int ordinal;
        public int count;
        public int health;
        public int attack;
        public int experience;
        public int armor;
        public int manaCost;
        public int damage;
        public int healing;
        public int manaGain;
        public string behavior;
        public string notes;
    }

    [Serializable]
    public sealed class BalanceEncounterAggregate
    {
        public string scopeId;
        public string targetId;
        public string nodeContentId;
        public string scenarioId;
        public int runs;
        public int populationRuns;
        public int wins;
        public int simulationLimits;
        public float winRate;
        public float populationWinRate;
        public float reachRate;
        public float simulationLimitRate;
        public float medianTurns;
        public float medianHpLost;
        public float p90HpLost;
        public float medianDamage;
        public float medianCardsPlayed;
        public float medianManaSpent;
        public float medianEndingHealth;
        public BalanceOutcomeStatistics outcomes = new BalanceOutcomeStatistics();
        public BalanceRateInterval reachRate95 = new BalanceRateInterval();
        public BalanceRateInterval populationWinRate95 = new BalanceRateInterval();
    }

    [Serializable]
    public sealed class BalanceRouteAggregate
    {
        public string routeId;
        public int routeRevision;
        public int runs;
        public int completions;
        public int defeats;
        public int simulationLimits;
        public float completionRate;
        public float simulationLimitRate;
        public float medianEndingHealth;
        public float p10EndingHealth;
        public float medianEncounterCount;
        public float medianTurns;
        public float medianHpLost;
        public float medianCardsPlayed;
        public float medianDamage;
        public BalanceOutcomeStatistics outcomes = new BalanceOutcomeStatistics();
    }

    /// <summary>Every route seed, including failures, for paired comparisons.</summary>
    [Serializable]
    public sealed class BalanceEncounterSample
    {
        public string scopeId;
        public string targetId;
        public string nodeContentId;
        public int seed;
        public string termination;
        public bool win;
        public int startingHealth;
        public int endingHealth;
        public int hpLost;
        public int damageTaken;
        public int armorAbsorbed;
        public int healingDone;
        public int enemyActions;
        public int turns;
        public int cardsPlayed;
        public int startingLevel;
        public int endingLevel;
        public int startingExperience;
        public int endingExperience;
        public int killedEnemies;
        public int escapedEnemies;
        public int enemyExperienceEarned;
        // 0/absent = not measured under the corrected event-based contract.
        public int telemetryRevision;
        public int appliedDamage, criticalDamage, dotDamage, overkillDamage;
        public int comboBreaks, killsByDirect, killsBySplash, killsByDot, killsByOther;
    }

    [Serializable]
    public sealed class BalanceRouteSample
    {
        public string routeId;
        public int seed;
        public string termination;
        public string failedNodeContentId;
        public int encountersReached;
        public int startingHealth;
        public int endingHealth;
        public int endingLevel;
        public int endingExperience;
        public int deckSize;
        public int turns;
    }

    [Serializable]
    public sealed class BalanceCardAggregate
    {
        public string scopeId;
        public string targetId;
        public string cardContentId;
        public int plays;
        public int damage;
        public int armor;
        public int healing;
        public int manaSpent;
        public int manaGained;
        public int directKills;
        public float damagePerPlay;
    }

    [Serializable]
    public sealed class BalanceEnemyAggregate
    {
        public string scopeId;
        public string targetId;
        public string enemyContentId;
        public int actions;
        public int attacks;
        public int defends;
        public int playerHealthDamage;
        public int playerArmorDamage;
        public int armorGained;
    }

    [Serializable]
    public sealed class BalanceRewardAggregate
    {
        public string scopeId;
        public string targetId;
        public string rewardKey;
        public string cardContentId;
        public string rewardType;
        public int offered;
        public int selected;
        public float selectionRateWhenOffered;
    }

    [Serializable]
    public sealed class BalanceFailureSample
    {
        public string scopeId;
        public string targetId;
        public int seed;
        public string nodeContentId;
        public string termination;
        public int endingHealth;
        public int turns;
    }

    [Serializable]
    public sealed class BalanceCheckResult
    {
        public string checkId;
        public string status;
        public string severity;
        public string description;
        public string details;
    }
}
