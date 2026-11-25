using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Terrain
{
    /// <summary>
    /// 以稳定 terrain effectId 为主键的表现皮肤。核心规则不知道颜色、粒子或材质；
    /// 新地形通常只需增加配置，只有出现新的视觉语言时才增加 visualType 实现。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewTerrainVfxProfile",
        menuName = "万界长卷/地形特效配置")]
    public sealed class TerrainVfxProfile : ScriptableObject
    {
        public string effectId;
        public string visualType = "ProceduralFlamePool";
        public Color primaryColor = new Color(1f, 0.22f, 0.025f, 1f);
        public Color coreColor = new Color(1f, 0.84f, 0.2f, 1f);
        public Color secondaryColor = new Color(0.22f, 0.06f, 0.025f, 0.55f);
        [Min(0.1f)] public float radius = 0.52f;
        [Min(0.1f)] public float height = 0.62f;
        [Min(1)] public int baseEmission = 16;
        [Min(0)] public int emissionPerStack = 7;
        [Min(0)] public int placementBurst = 10;
        [Min(0)] public int triggerBurst = 22;
        [Range(1f, 1.8f)] public float triggerPulseScale = 1.3f;
        [Min(0.05f)] public float removeDuration = 0.38f;
        public int sortingOrder = 13;
        [Min(0f)] public float triggerWaveHeight = 1.35f;
        [Range(0.5f, 2f)] public float flameWidthScale = 1f;
        [Range(0.1f, 1.5f)] public float coreIntensity = 0.65f;
        [Range(0f, 1f)] public float swirlStrength = 0.12f;
        [Range(0f, 1f)] public float groundGlowStrength = 0.18f;
        public string triggerSfxPath;
        [Range(0f, 2f)] public float triggerSfxGain = 1f;
    }

    public static class TerrainVfxProfileRegistry
    {
        private static readonly Dictionary<string, TerrainVfxProfile> Profiles =
            new Dictionary<string, TerrainVfxProfile>();
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            Profiles.Clear();
            TerrainVfxProfile[] loaded =
                Resources.LoadAll<TerrainVfxProfile>("TerrainVfxProfiles");
            for (int i = 0; i < loaded.Length; i++)
            {
                TerrainVfxProfile profile = loaded[i];
                if (profile == null || string.IsNullOrWhiteSpace(profile.effectId))
                    continue;
                Profiles[profile.effectId] = profile;
            }
        }

        public static TerrainVfxProfile Get(string effectId)
        {
            Initialize();
            if (string.IsNullOrWhiteSpace(effectId)) return null;
            Profiles.TryGetValue(effectId, out TerrainVfxProfile profile);
            return profile;
        }

        public static void ResetForTests()
        {
            Profiles.Clear();
            _initialized = false;
        }
    }
}
