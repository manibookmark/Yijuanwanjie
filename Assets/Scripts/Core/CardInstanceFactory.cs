using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// CardConfig 迁移期唯一的运行实例创建边界。新建与存档恢复是两个显式
    /// 语义；调用方不再通过 runtimeInstanceId 的零值猜测来源类型。
    /// Scale review: construction, detached permanent input and failed-allocation cleanup
    /// form one knowledge boundary; mutable physical state remains solely CardInstance's.
    /// Portable wire and Run grant/ID allocation remain outside this factory. Review again
    /// before adding another state owner, serialization protocol or independent lifecycle.
    /// </summary>
    public static class CardInstanceFactory
    {
        /// <summary>Exports the already frozen base/current author configurations.
        /// Physical mutable state and unknown historical Run provenance are not inferred.
        /// No recapture of this card's mutable facade or native allocation occurs.</summary>
        public static CardAuthorConfigurationPair CaptureAuthorConfigurations(CardConfig source,
            ICardAuthorReferenceResolver resolver)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            CardInstance instance = source.RuntimeInstance;
            if (instance == null)
                throw new ArgumentException("Author export requires a bound physical card.", nameof(source));
            RequireKnownAuthorConfigurations(source);
            CardAuthorDocument basis = instance.BaseTemplate.Export(resolver);
            CardAuthorDocument current = instance.CurrentTemplate.Export(resolver);
            // Export may call external resolvers twice. Validate the complete pair at
            // the final callback boundary; a later current callback cannot stale base.
            CardBaseTemplate first = CardBaseTemplate.FromDocument(basis, resolver);
            CardBaseTemplate second = CardBaseTemplate.FromDocument(current, resolver);
            RequirePairBindings(first, second);
            return new CardAuthorConfigurationPair(basis, current);
        }

        /// <summary>Captures only permanent physical fields. LegacyUnknown may be captured,
        /// but this is not evidence of an original configuration or a portable author format.</summary>
        public static CardPersistentState CapturePersistentState(CardConfig source)
        {
            if (source == null || source.RuntimeInstance == null || source.runtimeInstanceId <= 0)
                throw new ArgumentException("A bound physical card is required.", nameof(source));
            CardInstance state = source.RuntimeInstance;
            if (state.IsDestroyed || state.Lifetime != CardLifetime.Persistent || state.IsSealCopy ||
                !string.IsNullOrEmpty(state.EnemyInjectedCardContentId) || !string.IsNullOrEmpty(state.EnemySourceBehaviorContentId))
                throw new ArgumentException("Encounter or destroyed state cannot be captured as permanent.", nameof(source));
            if (source.contentId != state.DefinitionId)
                throw new ArgumentException("The physical facade changed its owned definition identity.", nameof(source));
            return new CardPersistentState(state.InstanceId, state.LimitBreakCount, source.gemSlots, source.seals);
        }

        /// <summary>Restores a true persistent card from independently frozen base/current
        /// documents. All borrowed closures are resolved and jointly rechecked before the
        /// only native allocation. The original basis may have a different stable content ID.
        /// Parsing/fingerprints establish integrity, not historical authority or a Run grant.</summary>
        public static CardConfig RestoreFrozenAuthors(CardAuthorDocument basis, CardAuthorDocument current,
            ICardAuthorReferenceResolver resolver, CardPersistentState state)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (state == null) throw new ArgumentNullException(nameof(state));
            CardBaseTemplate baseTemplate = CardBaseTemplate.FromDocument(basis, resolver);
            CardBaseTemplate currentTemplate = CardBaseTemplate.FromDocument(current, resolver);
            RequirePairBindings(baseTemplate, currentTemplate);
            return RestorePrepared(currentTemplate, baseTemplate, CardAuthorProvenance.FrozenKnown, state);
        }

        /// <summary>Explicit compatibility bridge for a historical card whose original
        /// configuration was never saved. Today's unbound author supplies operational
        /// behavior only; no base evidence is created, and clones remain LegacyUnknown.</summary>
        public static CardConfig RestoreLegacyUnknown(CardConfig todayAuthor, CardPersistentState state)
        {
            if (todayAuthor == null || todayAuthor.RuntimeInstance != null)
                throw new ArgumentException("Legacy compatibility needs an unbound current author.", nameof(todayAuthor));
            if (state == null) throw new ArgumentNullException(nameof(state));
            CardBaseTemplate current = CardBaseTemplate.CaptureCurrent(todayAuthor);
            return RestorePrepared(current, null, CardAuthorProvenance.LegacyUnknown, state);
        }

        private static CardConfig RestorePrepared(CardBaseTemplate current, CardBaseTemplate basis,
            CardAuthorProvenance provenance, CardPersistentState state)
        {
            CardConfig result = current.Instantiate();
            try
            {
                result.BindRuntimeInstance(state.InstanceId, basis, current, provenance);
                result.RestoreLimitBreakCount(state.LimitBreakCount);
                result.gemSlots = state.GemSlotCapacity;
                result.seals = state.CopySeals();
                // The frozen document owns innate author flags. Legacy compatibility
                // retains the former Restore policy of clearing temporary free state.
                if (provenance == CardAuthorProvenance.LegacyUnknown) result.isFreeToPlay = false;
                return result;
            }
            catch { DestroyUnreturned(result); throw; }
        }

        private static void RequirePairBindings(CardBaseTemplate basis, CardBaseTemplate current)
        {
            basis.RequireCompatibleBindings(current);
            basis.RequireUnchangedBindings();
            current.RequireUnchangedBindings();
        }

        internal static void RequireKnownAuthorConfigurations(CardConfig source)
        {
            CardInstance state = source?.RuntimeInstance;
            if (state == null || !state.HasKnownAuthorConfigurations || state.BaseTemplate == null || state.CurrentTemplate == null)
                throw new InvalidOperationException("LegacyUnknown cannot supply an original author configuration.");
            if (source.contentId != state.DefinitionId || state.CurrentTemplate.ContentId != state.DefinitionId)
                throw new InvalidOperationException("Physical and current author identities are inconsistent.");
        }

        /// <summary>
        /// Constructs one persistent duplicate from its frozen current author configuration,
        /// independently retaining the original base template. The caller supplies a globally
        /// fresh ID and an explicit breakthrough choice; this factory performs no Run grant.
        /// Live capacity and ordered installed seals are copied, while encounter state is fresh.
        /// </summary>
        public static CardConfig CreatePersistentDuplicate(CardConfig source, int newInstanceId,
            PersistentDuplicateLimitBreakPolicy policy)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            CardInstance state = source.RuntimeInstance;
            if (state == null || state.InstanceId <= 0)
                throw new ArgumentException("Persistent duplication requires a bound physical card.", nameof(source));
            if (newInstanceId <= 0 || newInstanceId == state.InstanceId)
                throw new ArgumentOutOfRangeException(nameof(newInstanceId), "The duplicate must have a fresh positive ID.");
            if (policy != PersistentDuplicateLimitBreakPolicy.Reset && policy != PersistentDuplicateLimitBreakPolicy.Preserve)
                throw new ArgumentOutOfRangeException(nameof(policy));
            if (state.IsDestroyed || state.Lifetime != CardLifetime.Persistent || state.IsSealCopy ||
                !string.IsNullOrEmpty(state.EnemyInjectedCardContentId) || !string.IsNullOrEmpty(state.EnemySourceBehaviorContentId))
                throw new ArgumentException("Destroyed or encounter-temporary cards cannot become persistent duplicates.", nameof(source));
            RequireKnownAuthorConfigurations(source);
            if (state.BaseTemplate == null || state.CurrentTemplate == null ||
                string.IsNullOrWhiteSpace(state.BaseTemplate.ContentId) ||
                string.IsNullOrWhiteSpace(state.DefinitionId) ||
                !string.Equals(source.contentId, state.DefinitionId, StringComparison.Ordinal) ||
                !string.Equals(state.CurrentTemplate.ContentId, state.DefinitionId, StringComparison.Ordinal))
                throw new InvalidOperationException("The source's configured identity and author templates are inconsistent.");
            int capacity = source.gemSlots;
            if (capacity < 0 || capacity > CardSealRules.MaximumSealSlots ||
                (source.seals?.Count ?? 0) > capacity)
                throw new ArgumentException("Installed seals must fit the exact live slot capacity.", nameof(source));
            if (source.seals != null)
                for (int i = 0; i < source.seals.Count; i++)
                    if (source.seals[i] == null || !Enum.IsDefined(typeof(CardSealEffect), source.seals[i].effect))
                        throw new ArgumentException("Installed seals contain an invalid effect.", nameof(source));
            List<CardSealSpec> seals = CloneSeals(source.seals);
            int breakthrough = policy == PersistentDuplicateLimitBreakPolicy.Preserve ? source.LimitBreakCount : 0;
            CardConfig duplicate = state.CurrentTemplate.Instantiate();
            try
            {
                duplicate.BindRuntimeInstance(newInstanceId, state.BaseTemplate, state.CurrentTemplate);
                duplicate.RestoreLimitBreakCount(breakthrough);
                duplicate.gemSlots = capacity;
                duplicate.seals = seals;
                // isFreeToPlay remains the frozen author's innate flag, never the host's temporary override.
                return duplicate;
            }
            catch
            {
                if (duplicate != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(duplicate);
                    else UnityEngine.Object.DestroyImmediate(duplicate);
                }
                throw;
            }
        }

        /// <summary>
        /// 从作者定义创建全新实体牌。任何来源实例携带的突破、灵印和临时免耗
        /// 都会被丢弃。若来源本身为遭遇临时牌，新复制品仍临时，但不继承敌方
        /// 注入来源；只有真实注入事务才能增加该来源的残留数。
        /// </summary>
        public static CardConfig CreateNew(
            CardConfig definition,
            int instanceId)
        {
            CardConfig instance = CloneAndBind(definition, instanceId);
            instance.seals = new List<CardSealSpec>();
            instance.isFreeToPlay = false;
            return instance;
        }

        /// <summary>
        /// Creates a fresh card whose definition/effects still come from
        /// author content, while its instance lifetime is limited to the
        /// current encounter.  No placeholder card or guessed effect is made.
        /// </summary>
        public static CardConfig CreateEncounterTemporary(
            CardConfig definition,
            int instanceId)
        {
            CardConfig instance = CreateNew(definition, instanceId);
            instance.MarkEncounterTemporary();
            return instance;
        }

        /// <summary>
        /// A new encounter-only, unsealed, destroy-on-use copy of the author's base
        /// template, never a clone of the played/upgraded host. The deck supplies a
        /// fresh ID; generating this object is neither a play nor a draw.
        /// </summary>
        public static CardConfig CreateBaseTemporaryCopy(CardConfig source, int instanceId)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.runtimeInstanceId <= 0)
                throw new ArgumentException("化影复制源必须是已绑定的实体牌。", nameof(source));
            if (instanceId <= 0 || instanceId == source.runtimeInstanceId)
                throw new ArgumentOutOfRangeException(nameof(instanceId), "化影副本必须使用新的正实例 ID。");
            RequireKnownAuthorConfigurations(source);
            CardBaseTemplate template = source.RuntimeInstance.BaseTemplate ??
                throw new InvalidOperationException("实体牌缺少基础模板，不能从升级宿主猜测原始定义。");
            CardConfig copy = template.Instantiate();
            copy.BindRuntimeInstance(instanceId, template, template);
            copy.seals = new List<CardSealSpec>();
            copy.isFreeToPlay = false;
            copy.MarkSealCopy();
            return copy;
        }

        /// <summary>
        /// 从版本化快照恢复实体牌。所有持久实例字段只在此处映射，新增字段无需
        /// 再让 DeckManager、授予事务和各个调用方复制恢复知识。
        /// </summary>
        public static CardConfig Restore(
            CardConfig definition,
            int instanceId,
            int limitBreakCount,
            IReadOnlyList<CardSealSpec> seals)
        {
            if (limitBreakCount < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(limitBreakCount),
                    "突破层数不能小于 0。");

            CardConfig instance = CloneAndBind(definition, instanceId);
            instance.RestoreLimitBreakCount(limitBreakCount);
            instance.seals = CloneSeals(seals);
            instance.isFreeToPlay = false;
            if (definition.runtimeInstanceId == instanceId)
                instance.RuntimeInstance.RestoreEncounterStateFrom(definition.RuntimeInstance);
            return instance;
        }

        /// <summary>Replaces the same physical entity while retaining its provenance. Evolution
        /// keeps the existing project target-as-base policy; a legacy entity still makes no
        /// original-author claim merely because the replacement is an unbound target.</summary>
        public static CardConfig RestoreReplacement(CardConfig source, CardConfig replacementAuthor,
            int limitBreakCount, IReadOnlyList<CardSealSpec> seals)
        {
            if (source == null || source.RuntimeInstance == null)
                throw new ArgumentException("Replacement requires an existing physical source.", nameof(source));
            if (replacementAuthor == null) throw new ArgumentNullException(nameof(replacementAuthor));
            if (ReferenceEquals(source, replacementAuthor))
                return Restore(source, source.runtimeInstanceId, limitBreakCount, seals);
            if (replacementAuthor.RuntimeInstance != null)
                throw new ArgumentException("A different replacement must be an unbound author.", nameof(replacementAuthor));
            if (limitBreakCount < 0) throw new ArgumentOutOfRangeException(nameof(limitBreakCount));
            List<CardSealSpec> copied = CloneSeals(seals);
            CardAuthorProvenance provenance = source.AuthorProvenance;
            CardBaseTemplate current = CardBaseTemplate.CaptureCurrent(replacementAuthor);
            CardConfig result = current.Instantiate();
            try
            {
                result.BindRuntimeInstance(source.runtimeInstanceId,
                    provenance == CardAuthorProvenance.FrozenKnown ? current : null, current, provenance);
                result.RestoreLimitBreakCount(limitBreakCount); result.seals = copied;
                // A known replacement owns the frozen target's innate author flag;
                // the source's temporary free override is never inherited. Legacy
                // replacement retains the former operational Restore policy.
                if (provenance == CardAuthorProvenance.LegacyUnknown) result.isFreeToPlay = false;
                result.RuntimeInstance.RestoreEncounterStateFrom(source.RuntimeInstance);
                if (source.RuntimeInstance.IsSealCopy) result.MarkSealCopy();
                return result;
            }
            catch { DestroyUnreturned(result); throw; }
        }

        /// <summary>恢复同一实体牌，保留稳定身份及遭遇临时生命周期，绝不将临时牌升为持久牌。</summary>
        public static CardConfig CloneRestored(CardConfig source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.runtimeInstanceId <= 0)
                throw new ArgumentException(
                    "恢复源必须是具有正 runtimeInstanceId 的实体牌。",
                    nameof(source));
            return Restore(
                source,
                source.runtimeInstanceId,
                source.LimitBreakCount,
                source.seals);
        }

        private static CardConfig CloneAndBind(
            CardConfig definition,
            int instanceId)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (instanceId <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(instanceId),
                    "卡牌实例 ID 必须大于 0。");

            CardInstance previous = definition.RuntimeInstance;
            CardAuthorProvenance provenance = previous?.AuthorProvenance ?? CardAuthorProvenance.FrozenKnown;
            CardBaseTemplate baseTemplate = previous == null ? CardBaseTemplate.Capture(definition) : previous.BaseTemplate;
            CardBaseTemplate currentTemplate = previous == null ? baseTemplate : CardBaseTemplate.CaptureCurrent(definition);
            CardConfig instance = UnityEngine.Object.Instantiate(definition);
            try
            {
                instance.BindRuntimeInstance(instanceId, baseTemplate, currentTemplate, provenance);
                if (definition.IsEncounterTemporary)
                    instance.MarkEncounterTemporary();
                if (definition.RuntimeInstance?.IsSealCopy == true)
                    instance.MarkSealCopy();
                return instance;
            }
            catch
            {
                // Until returned, this native clone belongs to the factory. The
                // caller cannot clean an object it never received; IDs stay retired.
                if (instance != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
                    else UnityEngine.Object.DestroyImmediate(instance);
                }
                throw;
            }
        }

        private static void DestroyUnreturned(CardConfig card)
        {
            if (card == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(card);
            else UnityEngine.Object.DestroyImmediate(card);
        }

        private static List<CardSealSpec> CloneSeals(
            IReadOnlyList<CardSealSpec> seals)
        {
            var result = new List<CardSealSpec>();
            if (seals == null) return result;
            for (int i = 0; i < seals.Count; i++)
            {
                CardSealSpec seal = seals[i];
                if (seal == null ||
                    string.IsNullOrWhiteSpace(seal.contentId))
                {
                    throw new ArgumentException(
                        $"第 {i} 个灵印快照无效。",
                        nameof(seals));
                }
                result.Add(seal.Clone());
            }
            return result;
        }
    }

    /// <summary>Detached permanent physical state, deliberately excluding turn use, temporary
    /// cost/free flags, return budget, injection provenance and encounter lifetime. Those
    /// belong to the encounter owner and cannot be promoted through this persistent API.</summary>
    public sealed class CardPersistentState
    {
        private readonly List<CardSealSpec> _seals;
        public int InstanceId { get; }
        public int LimitBreakCount { get; }
        public int GemSlotCapacity { get; }
        public IReadOnlyList<CardSealSpec> Seals => CopySeals().AsReadOnly();
        public CardPersistentState(int instanceId, int limitBreakCount, int gemSlotCapacity,
            IReadOnlyList<CardSealSpec> seals)
        {
            if (instanceId <= 0) throw new ArgumentOutOfRangeException(nameof(instanceId));
            if (limitBreakCount < 0) throw new ArgumentOutOfRangeException(nameof(limitBreakCount));
            if (gemSlotCapacity < 0 || gemSlotCapacity > CardSealRules.MaximumSealSlots)
                throw new ArgumentOutOfRangeException(nameof(gemSlotCapacity));
            _seals = new List<CardSealSpec>();
            if (seals != null)
                foreach (CardSealSpec seal in seals)
                {
                    if (seal == null || string.IsNullOrWhiteSpace(seal.contentId) ||
                        !Enum.IsDefined(typeof(CardSealEffect), seal.effect))
                        throw new ArgumentException("Permanent seals require exact IDs and known effect kinds.", nameof(seals));
                    _seals.Add(seal.Clone());
                }
            if (_seals.Count > gemSlotCapacity)
                throw new ArgumentException("Installed seals exceed the exact saved capacity.", nameof(seals));
            InstanceId = instanceId; LimitBreakCount = limitBreakCount; GemSlotCapacity = gemSlotCapacity;
        }
        internal List<CardSealSpec> CopySeals()
        {
            var result = new List<CardSealSpec>();
            foreach (CardSealSpec seal in _seals) result.Add(seal.Clone());
            return result;
        }
    }

}
