using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.Simulation.Balance;

namespace WanjieScroll.Simulation.Stage
{
    public enum RunAcceptanceObjective { Survival, Exploration }
    public enum RunAcceptanceOutcome { Victory, Defeat, SimulationLimit, Error }

    /// <summary>One declared starting slice and reproducible policy budget; no generated deck or route.</summary>
    [Serializable]
    public sealed class RunAcceptanceSettings
    {
        public string sliceId = "g1.post-tutorial.no-upgrades";
        public RunAcceptanceObjective objective = RunAcceptanceObjective.Survival;
        public int maxTurnsPerEncounter = 100;
        public int maxNodeDecisions = 256;
        public int searchDepth = 4;
        public int beamWidth = 6;
        public int futureSamples = 3;
        public int maxExpansions = 96;
        public bool validateReplay = true;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(sliceId) || sliceId.Any(c =>
                    !char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_'))
                throw new ArgumentException("Slice identity must contain only letters, digits, '.', '-' or '_'.");
            if (!Enum.IsDefined(typeof(RunAcceptanceObjective), objective) ||
                maxTurnsPerEncounter < 1 || maxNodeDecisions < 1 || searchDepth < 1 ||
                beamWidth < 1 || futureSamples < 1 || maxExpansions < 1)
                throw new ArgumentOutOfRangeException(nameof(RunAcceptanceSettings));
        }

        public string GameRunId(int seed)
        {
            Validate();
            if (seed <= 0) throw new ArgumentOutOfRangeException(nameof(seed));
            // Neither report identity nor strategy version changes map reward randomness.
            return "run.acceptance." + sliceId + ".s" + seed.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    [Serializable]
    public sealed class RunAcceptanceReport
    {
        public int schemaVersion = 1;
        public string runId;
        public string sourceSuiteId, sourceSuiteSha256, contentFingerprint;
        public string combatPolicyId = PlanningCombatAgent.Id, runPolicyId = RunAcceptancePolicy.PolicyId;
        public int combatPolicyRevision = PlanningCombatAgent.Revision, runPolicyRevision = RunAcceptancePolicy.CurrentRevision;
        public string entryScope = "formal-fresh-run;post-tutorial-all-grants;isolated-account;initial-currency=0;powerups=none;rules=RunCombatRules";
        public RunAcceptanceSettings settings;
        public int seedStart, seedEnd, requestedCount, exitCode, errorCount, simulationLimitCount;
        public BalanceOutcomeStatistics terminalHealth;
        public BalanceRateInterval completionRate95;
        public List<RunAcceptanceFloorPopulation> floors = new List<RunAcceptanceFloorPopulation>();
        public List<RunAcceptanceSample> samples = new List<RunAcceptanceSample>();

        public static RunAcceptanceReport Deserialize(string json)
        {
            RunAcceptanceReport report = JsonUtility.FromJson<RunAcceptanceReport>(json)
                ?? throw new InvalidOperationException("Acceptance report could not be decoded.");
            if (RunAcceptanceSample.FindEvidenceField(json, "samples", out string rawSamples))
            {
                if (!JsonObjectShape.TryGetArrayItems(rawSamples, out IReadOnlyList<string> samples) ||
                    samples.Count != report.samples.Count)
                    throw new InvalidOperationException("Acceptance report samples are invalid.");
                for (int i = 0; i < samples.Count; i++)
                    report.samples[i].ValidateSerializedAccountProgress(samples[i]);
            }
            return report;
        }

        internal void Summarize()
        {
            var outcomes = new BalanceStatistics();
            errorCount = simulationLimitCount = 0;
            foreach (RunAcceptanceSample sample in samples)
            {
                if (sample.outcome == RunAcceptanceOutcome.Error) { errorCount++; continue; }
                bool limited = sample.outcome == RunAcceptanceOutcome.SimulationLimit;
                if (limited) simulationLimitCount++;
                outcomes.Add(sample.outcome == RunAcceptanceOutcome.Victory, limited,
                    sample.terminal?.health ?? 0);
            }
            terminalHealth = outcomes.Summarize();
            completionRate95 = BalanceStatistics.Wilson95(
                samples.Count(s => s.outcome == RunAcceptanceOutcome.Victory), requestedCount);
            exitCode = errorCount > 0 ? 2 : simulationLimitCount > 0 ? 1 : 0;
            floors.Clear();
            foreach (string floorId in samples.SelectMany(s => s.entries)
                         .Select(e => e.floorContentId).Distinct())
            {
                var reached = samples.SelectMany(s => s.entries)
                    .Where(e => e.floorContentId == floorId).ToList();
                var health = new BalanceStatistics();
                foreach (RunAcceptanceEntry entry in reached) health.Add(true, false, entry.health);
                floors.Add(new RunAcceptanceFloorPopulation
                {
                    floorContentId = floorId,
                    reachRate95 = BalanceStatistics.Wilson95(reached.Count, requestedCount),
                    entryHealth = health.Summarize().all,
                    distinctDecks = reached.Select(e => e.DeckSignature()).Distinct().Count()
                });
            }
        }
    }

    [Serializable]
    public sealed class RunAcceptanceSample
    {
        public int seed;
        public string gameRunId;
        public RunAcceptanceOutcome outcome;
        public string failedFloorContentId, failedNodeContentId, failureReason;
        public long elapsedMilliseconds;
        public long managedBytesBefore, managedBytesAfter;
        public int accountTotalJadeEarned;
        // The adjacent account JSON preserves its legacy shape. These typed facts
        // complete its qualification evidence; neither is inferred from deck cards.
        public int initialHighestPlayerLevel, finalHighestPlayerLevel;
        public CardUnlockProgressSaveData initialUnlockProgress, finalUnlockProgress;
        public EventUnlockProgressSaveData initialEventUnlockProgress, finalEventUnlockProgress;
        // Independent typed XP evidence. Legacy zero means unrecorded history;
        // neither the final level nor another collection supplies missing XP.
        public int experienceCollectionProgressSchemaVersion;
        public ExperienceCollectionProgressSaveData initialExperienceCollectionProgress, finalExperienceCollectionProgress;
        // Independent event evidence envelope: old reports have no permanent-gate proof.
        public int eventUnlockProgressSchemaVersion;
        // Zero is the legacy envelope. New reports require explicit final progress.
        public int accountProgressSchemaVersion;
        public int totalTurns, totalCardsPlayed, collectedJade, dungeonBonusJade, accountJade;
        public bool finaleCompleted, conclusionFrozen, pendingTransaction;
        public string entryProjectionId;
        public int sourceAccountSeed;
        public bool forceMetaStatProgression;
        public int metaProjectedCombatCount, metaSuppressedCombatCount;
        public List<RunAcceptancePowerUpLevel> initialPowerUps = new List<RunAcceptancePowerUpLevel>();
        public List<RunAcceptancePowerUpLevel> finalPowerUps = new List<RunAcceptancePowerUpLevel>();
        public List<string> unlockedContentIds = new List<string>();
        public List<RunAcceptanceEntry> entries = new List<RunAcceptanceEntry>();
        public RunAcceptanceEntry terminal;
        public List<RunAcceptanceDecision> decisions = new List<RunAcceptanceDecision>();
        public List<RunAcceptanceCombat> combats = new List<RunAcceptanceCombat>();
        [NonSerialized] public string terminalPayload;
        public string terminalPath, terminalSha256;
        [NonSerialized] public string initialAccountPayload, finalAccountPayload;
        public string initialAccountPath, initialAccountSha256, finalAccountPath, finalAccountSha256;

        /// <summary>Restore the exported final account with its separately recorded
        /// permanent facts. Older reports omit the peak and therefore restore zero;
        /// content access and Run levels cannot manufacture missing history.</summary>
        public MetaProgression RestoreFinalAccount()
        {
            if (string.IsNullOrWhiteSpace(finalAccountPayload) || finalHighestPlayerLevel < 0)
                throw new InvalidOperationException("Final account evidence is missing or invalid.");
            if (accountProgressSchemaVersion != 0 &&
                accountProgressSchemaVersion != 2 &&
                accountProgressSchemaVersion != CardUnlockProgressSaveData.CurrentSchemaVersion)
                throw new InvalidOperationException("Unsupported final account progress schema.");
            MetaProgression restored = JsonUtility.FromJson<MetaProgression>(finalAccountPayload)
                ?? throw new InvalidOperationException("Final account evidence could not be decoded.");
            // JsonUtility creates a zero-valued nested class even when old JSON
            // omits this field. Only that completely empty surrogate is legacy;
            // any explicit version, peak or collection must pass strict Read.
            bool hasProgressEvidence = finalUnlockProgress != null &&
                (finalUnlockProgress.schemaVersion != 0 || finalUnlockProgress.highestPlayerLevel != 0 ||
                 finalUnlockProgress.lifetimeCounts != null || finalUnlockProgress.acceptedRuns != null ||
                 finalUnlockProgress.crawlerHighestLevels != null);
            if (!hasProgressEvidence && accountProgressSchemaVersion != 0)
                throw new InvalidOperationException("Final account complete progress is missing.");
            CardUnlockProgressSnapshot progress = hasProgressEvidence
                ? finalUnlockProgress.Read()
                : new CardUnlockProgressSnapshot(finalHighestPlayerLevel);
            if (progress.HighestPlayerLevel != finalHighestPlayerLevel)
                throw new InvalidOperationException("Final account progress disagrees with its recorded peak.");
            restored.CardUnlocks.Restore(progress);
            if (eventUnlockProgressSchemaVersion != 0 &&
                !EventUnlockProgressSaveData.SupportsSchemaVersion(eventUnlockProgressSchemaVersion))
                throw new InvalidOperationException("Unsupported final event-unlock envelope.");
            bool hasEventEvidence = HasEventEvidence(finalEventUnlockProgress);
            if (!hasEventEvidence && eventUnlockProgressSchemaVersion != 0)
                throw new InvalidOperationException("Final permanent event evidence is missing.");
            if (eventUnlockProgressSchemaVersion != 0 &&
                finalEventUnlockProgress.schemaVersion != eventUnlockProgressSchemaVersion)
                throw new InvalidOperationException("Event evidence disagrees with its report envelope version.");
            restored.EventUnlocks.Restore(hasEventEvidence
                ? finalEventUnlockProgress.Read(progress) : default);
            if (experienceCollectionProgressSchemaVersion == 0)
            {
                if (HasExperienceEvidence(initialExperienceCollectionProgress) || HasExperienceEvidence(finalExperienceCollectionProgress))
                    throw new InvalidOperationException("Legacy reports cannot contain future XP evidence.");
            }
            else
            {
                if (experienceCollectionProgressSchemaVersion != ExperienceCollectionProgress.CurrentVersion ||
                    initialExperienceCollectionProgress == null || finalExperienceCollectionProgress == null)
                    throw new InvalidOperationException("Unsupported or missing complete account XP evidence.");
                // Initial evidence is independently validated, though only final
                // watermarks are restored into this detached new account.
                var initialExperience = new ExperienceCollectionProgress();
                initialExperience.RestoreSaveData(initialExperienceCollectionProgress);
                restored.ExperienceCollections.RestoreSaveData(finalExperienceCollectionProgress);
                RequireForwardExperience(initialExperience, restored.ExperienceCollections);
            }
            return restored;
        }

        // Sample envelope knowledge belongs here. Account-ledger syntax, totals
        // and watermark validation remain owned by ExperienceCollectionProgress.
        private void ValidateSerializedExperience(string rawSample)
        {
            bool hasVersion = FindEvidenceField(rawSample, "experienceCollectionProgressSchemaVersion", out string version);
            int decodedVersion = 0;
            if (hasVersion && (version != "0" && version != "1"))
                throw new InvalidOperationException("Unsupported account XP report envelope.");
            if (hasVersion) decodedVersion = version == "1" ? 1 : 0;
            if (decodedVersion != experienceCollectionProgressSchemaVersion)
                throw new InvalidOperationException("XP evidence disagrees with its report envelope.");
            bool hasInitial = FindEvidenceField(rawSample, "initialExperienceCollectionProgress", out string initial);
            bool hasFinal = FindEvidenceField(rawSample, "finalExperienceCollectionProgress", out string final);
            if (decodedVersion == 0)
            {
                if ((hasInitial && initial != "null") || (hasFinal && final != "null"))
                    throw new InvalidOperationException("Legacy reports cannot contain future XP evidence.");
                initialExperienceCollectionProgress = finalExperienceCollectionProgress = null;
                return;
            }
            if (!hasInitial || !hasFinal || initial == "null" || final == "null")
                throw new InvalidOperationException("Complete initial and final account XP evidence is required.");
            initialExperienceCollectionProgress = ExperienceCollectionProgress.ReadSaveData(initial);
            finalExperienceCollectionProgress = ExperienceCollectionProgress.ReadSaveData(final);
            var first = new ExperienceCollectionProgress(); var last = new ExperienceCollectionProgress();
            first.RestoreSaveData(initialExperienceCollectionProgress);
            last.RestoreSaveData(finalExperienceCollectionProgress);
            RequireForwardExperience(first, last);
        }

        private static void RequireForwardExperience(ExperienceCollectionProgress first, ExperienceCollectionProgress last)
        {
            // A report must retain every starting watermark, even when its final
            // aggregate happens to be larger because another Run was credited.
            _ = last.LifetimeTotals.Subtract(first.LifetimeTotals);
            foreach (ExperienceCollectionRunCheckpoint run in first.Capture().AcceptedRuns)
                _ = last.GetAcceptedRunTotals(run.RunId).Subtract(run.Totals);
        }

        // Missing and ambiguous fields cannot share the same legacy fallback.
        // Escaped names are rejected only for these owned envelope fields; other
        // existing report values retain their normal JSON serialization semantics.
        internal static bool FindEvidenceField(string json, string field, out string raw)
        {
            raw = null;
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Missing report object.");
            string text = json.Trim();
            if (text.Length < 2 || text[0] != '{' || text[text.Length - 1] != '}')
                throw new InvalidOperationException("Report evidence must be an object.");
            int depth = 0, count = 0;
            for (int index = 0; index < text.Length; index++)
            {
                char current = text[index];
                if (current == '{' || current == '[') { depth++; continue; }
                if (current == '}' || current == ']') { depth--; continue; }
                if (current != '"') continue;
                var name = new System.Text.StringBuilder(); bool escaped = false, ended = false;
                while (++index < text.Length)
                {
                    current = text[index];
                    if (current == '"') { ended = true; break; }
                    if (current != '\\') { name.Append(current); continue; }
                    escaped = true;
                    if (++index >= text.Length) break;
                    current = text[index];
                    if (current == 'u')
                    {
                        if (index + 4 >= text.Length || !ushort.TryParse(text.Substring(index + 1, 4),
                            System.Globalization.NumberStyles.AllowHexSpecifier,
                            System.Globalization.CultureInfo.InvariantCulture, out ushort code))
                            throw new InvalidOperationException("Malformed report Unicode escape.");
                        name.Append((char)code); index += 4;
                    }
                    else
                    {
                        switch (current)
                        {
                            case '"': case '\\': case '/': name.Append(current); break;
                            case 'b': name.Append('\b'); break;
                            case 'f': name.Append('\f'); break;
                            case 'n': name.Append('\n'); break;
                            case 'r': name.Append('\r'); break;
                            case 't': name.Append('\t'); break;
                            default: throw new InvalidOperationException("Malformed report escape.");
                        }
                    }
                }
                if (!ended) throw new InvalidOperationException("Unterminated report string.");
                int next = index + 1; while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                if (depth != 1 || next >= text.Length || text[next] != ':' || name.ToString() != field) continue;
                if (escaped || ++count > 1) throw new InvalidOperationException("Report evidence cannot be escaped or duplicated.");
            }
            if (count == 0) return false;
            if (!JsonObjectShape.TryGetField(text, field, out raw)) throw new InvalidOperationException("Malformed report evidence field.");
            return true;
        }

        private static bool HasEventEvidence(EventUnlockProgressSaveData value)
            => value != null && (value.schemaVersion != 0 || value.chickenShopUnlocked ||
                value.manaOfferingUnlocked || !string.IsNullOrEmpty(value.manaOfferingQualifyingRunId) ||
                value.duplicateOfferingUnlocked || !string.IsNullOrEmpty(value.duplicateOfferingQualifyingRunId) ||
                value.evoOfferingUnlocked || !string.IsNullOrEmpty(value.evoOfferingQualifyingRunId) ||
                !string.IsNullOrEmpty(value.evoOfferingDungeonContentId) ||
                (value.evoOfferingCompletedFloorContentIds != null && value.evoOfferingCompletedFloorContentIds.Count != 0) ||
                value.gunForHireUnlocked || !string.IsNullOrEmpty(value.gunForHireQualifyingRunId));

        private static bool HasExperienceEvidence(ExperienceCollectionProgressSaveData value)
            => value != null && (value.version != 0 || value.playerXPGathered != 0 || value.pickupCrystal != 0 ||
                (value.acceptedRuns != null && value.acceptedRuns.Count != 0));

        internal void ValidateSerializedAccountProgress(string rawSample)
        {
            ValidateSerializedExperience(rawSample);
            if (eventUnlockProgressSchemaVersion != 0 &&
                !EventUnlockProgressSaveData.SupportsSchemaVersion(eventUnlockProgressSchemaVersion))
                throw new InvalidOperationException("Unsupported final event-unlock envelope.");
            bool hasFinalEvent = JsonObjectShape.TryGetField(rawSample, "finalEventUnlockProgress", out string finalEvent);
            bool hasInitialEvent = JsonObjectShape.TryGetField(rawSample, "initialEventUnlockProgress", out string initialEvent);
            bool requiresFinalEvent = eventUnlockProgressSchemaVersion != 0 || HasEventEvidence(finalEventUnlockProgress);
            bool requiresInitialEvent = eventUnlockProgressSchemaVersion != 0 || HasEventEvidence(initialEventUnlockProgress);
            if ((requiresFinalEvent && (!hasFinalEvent || finalEvent == "null")) ||
                (requiresInitialEvent && (!hasInitialEvent || initialEvent == "null")))
                throw new InvalidOperationException("Complete permanent event evidence is missing.");
            if (requiresFinalEvent) EventUnlockProgressSaveData.ValidateSerializedProgress(finalEvent);
            if (requiresInitialEvent) EventUnlockProgressSaveData.ValidateSerializedProgress(initialEvent);
            if (eventUnlockProgressSchemaVersion != 0 &&
                ((requiresFinalEvent && finalEventUnlockProgress.schemaVersion != eventUnlockProgressSchemaVersion) ||
                 (requiresInitialEvent && initialEventUnlockProgress.schemaVersion != eventUnlockProgressSchemaVersion)))
                throw new InvalidOperationException("Event evidence disagrees with its report envelope version.");
            if (accountProgressSchemaVersion != 0 &&
                accountProgressSchemaVersion != 2 &&
                accountProgressSchemaVersion != CardUnlockProgressSaveData.CurrentSchemaVersion)
                throw new InvalidOperationException("Unsupported final account progress schema.");
            bool hasFinal = JsonObjectShape.TryGetField(rawSample, "finalUnlockProgress", out string final);
            if (accountProgressSchemaVersion != 0 && (!hasFinal || final == "null"))
                throw new InvalidOperationException("Final account complete progress is missing.");
            if (hasFinal && final != "null") CardUnlockProgressSaveData.ValidateSerializedProgress(final);
            if (JsonObjectShape.TryGetField(rawSample, "initialUnlockProgress", out string initial) && initial != "null")
                CardUnlockProgressSaveData.ValidateSerializedProgress(initial);
        }
    }

    [Serializable]
    public sealed class RunAcceptancePowerUpLevel
    {
        public string contentId;
        public int level;
    }

    [Serializable]
    public sealed class RunAcceptanceEntry
    {
        public string floorContentId, snapshotId;
        public int health, maxHealth, level, experience, xpFractionCarryUnits;
        public int runJadeEarned, baseManaBonus, baseArmorPerTurnBonus, mightBonus, handLimitBonus, areaBonus;
        public int runLuckBonus, runGreedBonus, runGrowthBonus;
        public List<RunAcceptanceCard> cards = new List<RunAcceptanceCard>();
        public string checkpointPath, checkpointSha256;
        [NonSerialized] public string payload;

        internal string DeckSignature() => string.Join("|", cards
            .Select(c => c.contentId + ":" + c.limitBreak + ":" + string.Join(",", c.seals.OrderBy(s => s)))
            .OrderBy(s => s, StringComparer.Ordinal));
    }

    [Serializable]
    public sealed class RunAcceptanceCard
    {
        public string contentId;
        public int instanceId, limitBreak;
        public List<string> seals = new List<string>();
    }

    [Serializable]
    public sealed class RunAcceptanceFloorPopulation
    {
        public string floorContentId;
        public BalanceRateInterval reachRate95;
        public BalanceHealthDistribution entryHealth;
        public int distinctDecks;
    }

    [Serializable]
    public sealed class RunAcceptanceDecision
    {
        public int sequence;
        public string floorContentId, nodeContentId, kind, selectedId, reason;
        public int healthBefore, healthAfter;
        public List<RunAcceptanceCandidate> candidates = new List<RunAcceptanceCandidate>();
    }

    [Serializable]
    public sealed class RunAcceptanceCandidate
    {
        public string id, contentId, reason;
        public int instanceId;
        public float score;
    }

    [Serializable]
    public sealed class RunAcceptanceCombat
    {
        public string floorContentId, nodeContentId, scenarioId, termination;
        public int turns, cardsPlayed, startingHealth, endingHealth, maxCombo, armorGenerated, healing;
        public int maximumSearchDepth, crawlerTriggers, cardsDrawn, damage, comboBreaks;
        // 0/absent = not measured; a failed run must not masquerade as zero events.
        public int telemetryRevision;
        public int appliedDamage, criticalDamage, dotDamage, overkillDamage;
        public int killedEnemies, killsByDirect, killsBySplash, killsByDot, killsByOther;
        public long decisions, expandedNodes, replayedCommands, budgetExhaustions, hiddenBoundaryStops;
        public double searchMilliseconds;
        public bool replayValidated;
        public long replayValidationMilliseconds;
        public string tracePath, traceSha256;
        [NonSerialized] public string tracePayload;
    }
}
