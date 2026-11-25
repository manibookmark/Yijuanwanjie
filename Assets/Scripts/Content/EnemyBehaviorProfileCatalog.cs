using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// 行为资源的唯一解析入口。定义与完整作者输入指纹在同一缓存批次
    /// 产生、失效和清除；调用方不能另读资源来猜测已编译定义的版本。
    /// </summary>
    public static class EnemyBehaviorProfileCatalog
    {
        private const string ResourcesPath = "Content/EnemyBehaviors";
        private static Dictionary<string, EnemyBehaviorDefinition> _definitions;
        private static Dictionary<string, string> _errors;
        private static Dictionary<string, string> _fingerprints;
        private static string _cachedSignature;

        /// <summary>Stable-ID ordered effective-input signatures of successful
        /// definitions and invalid-ID markers, not Core implementation hashes.</summary>
        public static string CachedSignature
        {
            get { EnsureLoaded(); return _cachedSignature; }
        }

        public static bool TryResolve(string contentId,
            out EnemyBehaviorDefinition definition, out string error) =>
            TryResolveWithFingerprint(contentId, out definition, out _, out error);

        public static bool TryResolveWithFingerprint(string contentId,
            out EnemyBehaviorDefinition definition, out string effectiveInputFingerprint,
            out string error)
        {
            definition = null;
            effectiveInputFingerprint = null;
            if (string.IsNullOrWhiteSpace(contentId))
            {
                error = "敌人行为 contentId 不能为空。";
                return false;
            }
            EnsureLoaded();
            if (_definitions.TryGetValue(contentId, out definition))
            {
                effectiveInputFingerprint = _fingerprints[contentId];
                error = null;
                return true;
            }
            if (_errors.TryGetValue(contentId, out error)) return false;
            error = $"未找到敌人行为资源：{contentId}。";
            return false;
        }

        private static void EnsureLoaded()
        {
            if (_definitions != null) return;
            // Publish only after the entire batch is complete. A definition can
            // never be visible without its same-build effective-input fingerprint.
            var definitions = new Dictionary<string, EnemyBehaviorDefinition>(StringComparer.OrdinalIgnoreCase);
            var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EnemyBehaviorProfile[] profiles = Resources.LoadAll<EnemyBehaviorProfile>(ResourcesPath);
            foreach (EnemyBehaviorProfile profile in profiles)
            {
                string id = profile != null ? profile.contentId : null;
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (definitions.ContainsKey(id) || errors.ContainsKey(id))
                {
                    definitions.Remove(id);
                    fingerprints.Remove(id);
                    errors[id] = $"敌人行为 contentId 重复：{id}。";
                    continue;
                }
                if (!profile.TryBuildDefinition(out EnemyBehaviorDefinition definition, out string error))
                {
                    errors.Add(id, error);
                    continue;
                }
                try
                {
                    string fingerprint = CaptureEffectiveInput(profile, definition);
                    definitions.Add(id, definition);
                    fingerprints.Add(id, fingerprint);
                }
                catch (Exception exception)
                {
                    definitions.Remove(id);
                    fingerprints.Remove(id);
                    errors.Add(id, "无法捕获敌人行为编译输入：" + exception.Message);
                }
            }
            string signature = CaptureSignature(fingerprints, errors);
            _errors = errors;
            _fingerprints = fingerprints;
            _cachedSignature = signature;
            _definitions = definitions;
        }

        private static string CaptureEffectiveInput(EnemyBehaviorProfile profile,
            EnemyBehaviorDefinition definition)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("enemy-behavior-effective-input-v1");
                // All serialized author fields: legacy phase thresholds, every
                // primary/secondary action and card reference, and all health groups.
                writer.Write(JsonUtility.ToJson(profile));
                // JSON expands references into values. Preserve topology with
                // deterministic first-occurrence indices, never Unity instance IDs.
                var authoredActions = new Dictionary<EnemyBehaviorActionData, int>();
                WriteActions(writer, profile.actions, authoredActions);
                WriteActions(writer, profile.phaseTwoActions, authoredActions);
                var authoredGroups = new Dictionary<EnemyHealthBehaviorGroupData, int>();
                WriteReferences(writer, profile.healthGroups, authoredGroups);
                if (profile.healthGroups != null)
                    foreach (EnemyHealthBehaviorGroupData group in profile.healthGroups)
                    {
                        if (group != null)
                            writer.Write(BitConverter.ToInt32(BitConverter.GetBytes(group.requiredHealthPercent), 0));
                        WriteActions(writer, group?.actions, authoredActions);
                    }
                EnemyHealthBehaviorDefinition health = definition.HealthGroups;
                writer.Write(health != null);
                if (health != null)
                {
                    var groups = new Dictionary<EnemyHealthBehaviorGroup, int>();
                    var choices = new Dictionary<EnemyHealthBehaviorChoice, int>();
                    WriteReferences(writer, health.Groups, groups);
                    foreach (EnemyHealthBehaviorGroup group in health.Groups)
                        WriteReferences(writer, group.Choices, choices);
                }
                writer.Flush();
                return Hash(stream.ToArray());
            }
        }

        private static void WriteActions(BinaryWriter writer,
            IReadOnlyList<EnemyBehaviorActionData> actions, Dictionary<EnemyBehaviorActionData, int> firstOccurrence)
        {
            WriteReferences(writer, actions, firstOccurrence);
            if (actions != null)
                foreach (EnemyBehaviorActionData action in actions)
                    if (action != null)
                        writer.Write(BitConverter.ToInt32(BitConverter.GetBytes(action.healthWeight), 0));
        }

        private static void WriteReferences<T>(BinaryWriter writer,
            IReadOnlyList<T> values, Dictionary<T, int> firstOccurrence) where T : class
        {
            writer.Write(values?.Count ?? -1);
            if (values == null) return;
            foreach (T value in values)
            {
                if (value == null) { writer.Write(-1); continue; }
                if (!firstOccurrence.TryGetValue(value, out int index))
                {
                    index = firstOccurrence.Count;
                    firstOccurrence.Add(value, index);
                }
                writer.Write(index);
            }
        }

        private static string CaptureSignature(Dictionary<string, string> fingerprints,
            Dictionary<string, string> errors)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("enemy-behavior-cache-v1");
                var ids = new List<string>(fingerprints.Keys);
                ids.AddRange(errors.Keys);
                ids.Sort(StringComparer.Ordinal);
                writer.Write(ids.Count);
                foreach (string id in ids)
                {
                    writer.Write(id.ToLowerInvariant());
                    bool valid = fingerprints.TryGetValue(id, out string fingerprint);
                    writer.Write(valid);
                    if (valid) writer.Write(fingerprint);
                }
                writer.Flush();
                return Hash(stream.ToArray());
            }
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

#if UNITY_EDITOR
        /// <summary>Clear compiled definitions and their same-build identities together.</summary>
        public static void ClearCacheEditor()
        {
            _definitions = null;
            _errors = null;
            _fingerprints = null;
            _cachedSignature = null;
        }
        internal static void ClearCacheForTests() => ClearCacheEditor();
#endif
    }
}
