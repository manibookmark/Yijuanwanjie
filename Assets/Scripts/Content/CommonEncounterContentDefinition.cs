using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    public readonly struct CommonEncounterCurveKeyDefinition
    {
        public float Time { get; }
        public float Value { get; }
        public float InSlope { get; }
        public float OutSlope { get; }
        public float InWeight { get; }
        public float OutWeight { get; }
        public int WeightedMode { get; }

        internal CommonEncounterCurveKeyDefinition(CommonEncounterCurveKeyData data)
        {
            Time = data.time; Value = data.value;
            InSlope = data.inSlope; OutSlope = data.outSlope;
            InWeight = data.inWeight; OutWeight = data.outWeight;
            WeightedMode = data.weightedMode;
        }
    }

    public sealed class CommonEncounterCurveDefinition
    {
        private readonly CommonEncounterNativeCurve _native;
        public IReadOnlyList<CommonEncounterCurveKeyDefinition> Keys { get; }
        public int SerializedPreInfinity { get; }
        public int SerializedPostInfinity { get; }
        public int RotationOrder { get; }

        internal CommonEncounterCurveDefinition(CommonEncounterCurveData data)
        {
            var keys = new CommonEncounterCurveKeyDefinition[data.m_Curve.Count];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = new CommonEncounterCurveKeyDefinition(data.m_Curve[i]);
            Keys = Array.AsReadOnly(keys);
            SerializedPreInfinity = data.m_PreInfinity;
            SerializedPostInfinity = data.m_PostInfinity;
            RotationOrder = data.m_RotationOrder;
            _native = new CommonEncounterNativeCurve(this);
        }

        internal float Evaluate(float time) => _native.Evaluate(time);
    }

    public sealed class CommonEncounterDifficultyDefinition
    {
        public string ContentId { get; }
        /// <summary>Null inherits the dataset filter; empty explicitly disables it.</summary>
        public IReadOnlyList<string> EnemyRequestedLeafTags { get; }
        public float MinDifficulty { get; }
        public float MaxDifficulty { get; }
        public CommonEncounterCurveDefinition MinEnemyVariance { get; }
        public CommonEncounterCurveDefinition MaxEnemyVariance { get; }
        public CommonEncounterCurveDefinition MinEncounterVariance { get; }
        public CommonEncounterCurveDefinition MaxEncounterVariance { get; }

        internal CommonEncounterDifficultyDefinition(CommonEncounterDifficultyData data)
        {
            ContentId = data.contentId;
            EnemyRequestedLeafTags = data.enemyRequestedLeafTagContentIds == null
                ? null : Array.AsReadOnly(data.enemyRequestedLeafTagContentIds.ToArray());
            MinDifficulty = data.minDifficulty; MaxDifficulty = data.maxDifficulty;
            MinEnemyVariance = new CommonEncounterCurveDefinition(data.minEnemyVariance);
            MaxEnemyVariance = new CommonEncounterCurveDefinition(data.maxEnemyVariance);
            MinEncounterVariance = new CommonEncounterCurveDefinition(data.minEncounterVariance);
            MaxEncounterVariance = new CommonEncounterCurveDefinition(data.maxEncounterVariance);
        }

        internal float Normalize(float difficulty)
        {
            if (MinDifficulty == MaxDifficulty) return 0f;
            // Native subss/divss round at every operation. Expanded Mono
            // intermediates must not change the curve input at a boundary.
            float range = (float)((double)MaxDifficulty - MinDifficulty);
            float offset = (float)((double)difficulty - MinDifficulty);
            float value = (float)((double)offset / range);
            value = value < 0f ? 0f : value > 1f ? 1f : value;
            if (!CommonEncounterContentCompiler.IsFinite(value))
                throw new InvalidOperationException("Normalized tile difficulty is not finite.");
            return value;
        }
    }

    /// <summary>Immutable compiled dataset. Content owns curve interpretation;
    /// Core owns selection. No Run state, random stream or physical placement is
    /// retained. Native curves are privately created only for an actual request.</summary>
    public sealed class CommonEncounterContentDefinition : ICommonEncounterRequestSource,
        ICommonEncounterDifficultyEnemyTagSource
    {
        private readonly Dictionary<string, CommonEncounterDifficultyDefinition> _difficultyById;
        public string ContentId { get; }
        public string Fingerprint { get; }
        public IReadOnlyList<CommonEncounterTemplate> Templates { get; }
        public IReadOnlyList<CommonEncounterEnemy> Enemies { get; }
        public IReadOnlyList<CommonEncounterDifficultyDefinition> DifficultyProfiles { get; }
        public IReadOnlyList<CommonEncounterTagRequest> TemplateTagRequests { get; }
        public IReadOnlyList<string> EnemyRequestedLeafTags { get; }
        public IReadOnlyList<string> DefaultTemplateLeafTags { get; }

        internal CommonEncounterContentDefinition(string contentId, string fingerprint,
            List<CommonEncounterTemplate> templates, List<CommonEncounterEnemy> enemies,
            List<CommonEncounterDifficultyDefinition> difficulties,
            List<CommonEncounterTagRequest> requests, List<string> enemyTags, List<string> defaultTags)
        {
            ContentId = contentId; Fingerprint = fingerprint;
            Templates = Array.AsReadOnly(templates.ToArray());
            Enemies = Array.AsReadOnly(enemies.ToArray());
            DifficultyProfiles = Array.AsReadOnly(difficulties.ToArray());
            TemplateTagRequests = Array.AsReadOnly(requests.ToArray());
            EnemyRequestedLeafTags = Array.AsReadOnly(enemyTags.ToArray());
            DefaultTemplateLeafTags = Array.AsReadOnly(defaultTags.ToArray());
            _difficultyById = new Dictionary<string, CommonEncounterDifficultyDefinition>(StringComparer.Ordinal);
            foreach (CommonEncounterDifficultyDefinition difficulty in difficulties)
                _difficultyById.Add(difficulty.ContentId, difficulty);
        }

        /// <summary>Metadata-only query; it never initializes a native curve.</summary>
        public bool HasDifficulty(string difficultyContentId) =>
            difficultyContentId != null && _difficultyById.ContainsKey(difficultyContentId);

        /// <summary>Effective immutable metadata only; no native curve is created.</summary>
        public bool TryGetEnemyRequestedLeafTags(string difficultyContentId,
            out IReadOnlyList<string> tags)
        {
            tags = null;
            if (difficultyContentId == null || !_difficultyById.TryGetValue(
                difficultyContentId, out CommonEncounterDifficultyDefinition difficulty)) return false;
            tags = difficulty.EnemyRequestedLeafTags ?? EnemyRequestedLeafTags;
            return true;
        }

        public bool TryCreateRequest(string difficultyContentId, float tileDifficulty,
            out CommonEncounterRequest request, out string error)
        {
            request = null;
            if (difficultyContentId == null || !_difficultyById.TryGetValue(
                    difficultyContentId, out CommonEncounterDifficultyDefinition difficulty))
            {
                error = "Unknown encounter difficulty contentId: " + (difficultyContentId ?? "<null>");
                return false;
            }
            if (!CommonEncounterContentCompiler.IsFinite(tileDifficulty))
            {
                error = "An explicit finite RoomTile difficulty is required.";
                return false;
            }
            try
            {
                float normalized = difficulty.Normalize(tileDifficulty);
                // The two signed variance pairs are independent. Curve outputs
                // are not replaced with a radius, symmetrized or clamped.
                var enemyVariance = new CommonEncounterVariance(
                    difficulty.MinEnemyVariance.Evaluate(normalized),
                    difficulty.MaxEnemyVariance.Evaluate(normalized));
                var encounterVariance = new CommonEncounterVariance(
                    difficulty.MinEncounterVariance.Evaluate(normalized),
                    difficulty.MaxEncounterVariance.Evaluate(normalized));
                request = new CommonEncounterRequest(tileDifficulty, encounterVariance,
                    enemyVariance, TemplateTagRequests,
                    difficulty.EnemyRequestedLeafTags ?? EnemyRequestedLeafTags, DefaultTemplateLeafTags);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = "Cannot create native encounter curve request: " + exception.Message;
                return false;
            }
        }
    }
}
