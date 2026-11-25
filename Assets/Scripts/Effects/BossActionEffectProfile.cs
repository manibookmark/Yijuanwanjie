using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    [Serializable]
    public sealed class BossAttackEffectBinding
    {
        [Tooltip("与 EnemyBehaviorProfile 中一致的稳定攻击 actionId")]
        public string actionId;
        public BossActionEffectPreset preset;
    }

    /// <summary>
    /// 单个 BOSS 的动作到复用预设绑定。概率、伤害和动画仍由各自资产拥有；
    /// 本资产只回答一次权威结算应该选用哪个表现预设。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewBossActionEffectProfile",
        menuName = "万界长卷/特效/BOSS 动作特效绑定")]
    public sealed class BossActionEffectProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        [Tooltip("敌人的稳定 contentId")]
        public string enemyContentId;
        [Tooltip("攻击动画已烘焙完整命中特效时，禁止再叠加通用小命中与全屏刀光")]
        public bool suppressGenericAttackFallback;
        public BossActionEffectPreset defaultAttackPreset;
        public List<BossAttackEffectBinding> attackBindings =
            new List<BossAttackEffectBinding>();
        [Tooltip("所有 Defend 意图共用；Wait 永远不会解析此项")]
        public BossActionEffectPreset defendPreset;

        public bool TryResolve(
            EnemyIntentType intentType,
            string actionId,
            out BossActionEffectPreset preset)
        {
            preset = null;
            if (intentType == EnemyIntentType.Defend)
            {
                preset = defendPreset;
                return preset != null;
            }
            if (intentType != EnemyIntentType.Attack)
                return false;

            string normalized = Normalize(actionId);
            if (!string.IsNullOrEmpty(normalized))
            {
                int bindingCount = attackBindings?.Count ?? 0;
                for (int i = 0; i < bindingCount; i++)
                {
                    BossAttackEffectBinding binding = attackBindings[i];
                    if (binding != null &&
                        string.Equals(
                            Normalize(binding.actionId),
                            normalized,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        preset = binding.preset;
                        return preset != null;
                    }
                }
            }

            preset = defaultAttackPreset;
            return preset != null;
        }

        public bool TryValidate(out string error)
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                error = $"{name} schemaVersion 必须为 " +
                        $"{CurrentSchemaVersion}。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(enemyContentId))
            {
                error = $"{name} 缺少稳定 enemyContentId。";
                return false;
            }
            if (!ValidatePreset(
                    defaultAttackPreset,
                    BossActionEffectUsage.Attack,
                    "默认攻击",
                    out error))
            {
                return false;
            }
            if (!ValidatePreset(
                    defendPreset,
                    BossActionEffectUsage.Defend,
                    "统一防御",
                    out error))
            {
                return false;
            }

            var actionIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            int bindingCount = attackBindings?.Count ?? 0;
            for (int i = 0; i < bindingCount; i++)
            {
                BossAttackEffectBinding binding = attackBindings[i];
                if (binding == null)
                {
                    error = $"{enemyContentId}.attackBindings[{i}] 不能为 null。";
                    return false;
                }
                string normalized = Normalize(binding.actionId);
                if (string.IsNullOrEmpty(normalized))
                {
                    error = $"{enemyContentId}.attackBindings[{i}] 缺少 actionId。";
                    return false;
                }
                if (!actionIds.Add(normalized))
                {
                    error = $"{enemyContentId} 的攻击 actionId 重复：" +
                            $"{normalized}。";
                    return false;
                }
                if (binding.preset == null)
                {
                    error = $"{enemyContentId} 的攻击 {normalized} 未绑定特效。";
                    return false;
                }
                if (!ValidatePreset(
                        binding.preset,
                        BossActionEffectUsage.Attack,
                        $"攻击 {normalized}",
                        out error))
                {
                    return false;
                }
            }

            error = null;
            return true;
        }

        private bool ValidatePreset(
            BossActionEffectPreset preset,
            BossActionEffectUsage requiredUsage,
            string label,
            out string error)
        {
            if (preset == null)
            {
                error = null;
                return true;
            }
            if (!preset.TryValidate(out error))
                return false;
            if (preset.usage != requiredUsage)
            {
                error = $"{enemyContentId} 的{label}预设用途必须为 " +
                        $"{requiredUsage}。";
                return false;
            }
            return true;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }

    /// <summary>
    /// 运行时唯一的 BOSS 动作特效解析入口。调用方只提交稳定敌人 ID、
    /// 意图类型和 actionId，不需要理解 Resources 目录或回退规则。
    /// </summary>
    public static class BossActionEffectCatalog
    {
        private const string ProfileResourcePath =
            "BossActionEffects/Profiles";
        private const string PresetResourcePath =
            "BossActionEffects/Presets";

        private static Dictionary<string, BossActionEffectProfile> _profiles;
        private static Dictionary<string, string> _errors;

        /// <summary>
        /// A compound action keeps one authoritative settlement. Its primary
        /// action selects the effect; the secondary payload is not replayed.
        /// </summary>
        public static bool TryResolve(
            string enemyContentId,
            EnemyIntent intent,
            out BossActionEffectPreset preset)
        {
            EnemyIntentType primaryType = intent.Type == EnemyIntentType.Compound
                ? intent.PrimaryType
                : intent.Type;
            return TryResolve(enemyContentId, primaryType, intent.ActionId, out preset);
        }

        public static bool TryResolve(
            string enemyContentId,
            EnemyIntentType intentType,
            string actionId,
            out BossActionEffectPreset preset)
        {
            preset = null;
            if (!TryGetProfile(enemyContentId, out var profile, out _))
                return false;
            return profile.TryResolve(intentType, actionId, out preset) &&
                   preset != null &&
                   preset.TryValidate(out _);
        }

        public static bool SuppressesGenericAttackFallback(
            string enemyContentId)
        {
            return TryGetProfile(enemyContentId, out var profile, out _) &&
                   profile.suppressGenericAttackFallback;
        }

        public static bool TryGetProfile(
            string enemyContentId,
            out BossActionEffectProfile profile,
            out string error)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(enemyContentId))
            {
                error = "BOSS 特效解析需要稳定 enemyContentId。";
                return false;
            }

            EnsureLoaded();
            string normalized = enemyContentId.Trim();
            if (_profiles.TryGetValue(normalized, out profile))
            {
                error = null;
                return true;
            }
            if (_errors.TryGetValue(normalized, out error))
                return false;
            error = $"未找到 BOSS 动作特效绑定：{normalized}。";
            return false;
        }

        public static IReadOnlyList<string> ValidateAll()
        {
            EnsureLoaded();
            var errors = new List<string>(_errors.Values);
            var presetIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            BossActionEffectPreset[] presets =
                Resources.LoadAll<BossActionEffectPreset>(PresetResourcePath);
            for (int i = 0; i < presets.Length; i++)
            {
                BossActionEffectPreset preset = presets[i];
                if (preset == null)
                    continue;
                if (!preset.TryValidate(out string error))
                    errors.Add(error);
                else if (!presetIds.Add(preset.effectId.Trim()))
                    errors.Add($"BOSS 特效 effectId 重复：{preset.effectId}。");
            }
            return errors;
        }

        public static void ClearCache()
        {
            _profiles = null;
            _errors = null;
        }

        private static void EnsureLoaded()
        {
            if (_profiles != null)
                return;

            _profiles = new Dictionary<string, BossActionEffectProfile>(
                StringComparer.OrdinalIgnoreCase);
            _errors = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            BossActionEffectProfile[] profiles =
                Resources.LoadAll<BossActionEffectProfile>(
                    ProfileResourcePath);
            for (int i = 0; i < profiles.Length; i++)
            {
                BossActionEffectProfile profile = profiles[i];
                string id = profile?.enemyContentId?.Trim();
                if (string.IsNullOrEmpty(id))
                    continue;
                if (_profiles.ContainsKey(id) || _errors.ContainsKey(id))
                {
                    _profiles.Remove(id);
                    _errors[id] = $"BOSS 动作特效 enemyContentId 重复：" +
                                  $"{id}。";
                    continue;
                }
                if (profile.TryValidate(out string error))
                    _profiles.Add(id, profile);
                else
                    _errors.Add(id, error);
            }
        }
    }
}
