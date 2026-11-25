using System;
using System.Collections.Generic;

namespace WanjieScroll.Content
{
    /// <summary>Author-only ordinary encounter data. Order and source floats are
    /// explicit; no member synthesizes an identity, curve or RoomTile input.</summary>
    [Serializable]
    public sealed class CommonEncounterContentData
    {
        public string contentId;
        public List<string> defaultTemplateLeafTagContentIds;
        public List<CommonEncounterTemplateData> templates;
        public List<CommonEncounterEnemyData> enemies;
        public List<CommonEncounterDifficultyData> difficultyProfiles;
        public List<CommonEncounterTagRequestData> templateTagRequests;
        public List<string> enemyRequestedLeafTagContentIds;
    }

    [Serializable]
    public sealed class CommonEncounterTemplateData
    {
        public string contentId;
        public float difficulty;
        public int firstAxisLength;
        public int secondAxisLength;
        public List<int> cells;
        public bool preSelectEnemies;
        public List<string> allowedLeafTagContentIds;
    }

    [Serializable]
    public sealed class CommonEncounterEnemyData
    {
        public string enemyContentId;
        public float sourceDifficulty;
        public bool allowInTemplate;
        public List<string> dungeonLeafTagContentIds;
    }

    [Serializable]
    public sealed class CommonEncounterTagRequestData
    {
        public string contentId;
        public bool invertFilter;
    }

    [Serializable]
    public sealed class CommonEncounterDifficultyData
    {
        public string contentId;
        // Null inherits the dataset enemy filter; an explicit empty list admits
        // every template-eligible enemy. Order is part of the author contract.
        public List<string> enemyRequestedLeafTagContentIds;
        public float minDifficulty;
        public float maxDifficulty;
        public CommonEncounterCurveData minEnemyVariance;
        public CommonEncounterCurveData maxEnemyVariance;
        public CommonEncounterCurveData minEncounterVariance;
        public CommonEncounterCurveData maxEncounterVariance;
    }

    /// <summary>These are native serialized curve fields, not WrapMode enum
    /// values. Their interpretation belongs to the engine serializer.</summary>
    [Serializable]
    public sealed class CommonEncounterCurveData
    {
        public List<CommonEncounterCurveKeyData> m_Curve;
        public int m_PreInfinity;
        public int m_PostInfinity;
        public int m_RotationOrder;
    }

    [Serializable]
    public sealed class CommonEncounterCurveKeyData
    {
        public float time;
        public float value;
        public float inSlope;
        public float outSlope;
        public int weightedMode;
        public float inWeight;
        public float outWeight;
    }
}
