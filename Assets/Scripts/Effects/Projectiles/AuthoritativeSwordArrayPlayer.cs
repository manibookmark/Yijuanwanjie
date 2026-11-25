using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Owns the evolved returning-sword presentation. Core remains the only
    /// source of attack count, target order, critical results and damage. The
    /// formation is cosmetic: every visible contact is backed by a captured
    /// Direct/Bounce receipt, while return and collapse never publish hits.
    /// One coroutine samples the complete cast so cancellation/domain reload
    /// cannot leave independently-owned blades or array marks alive.
    /// </summary>
    public sealed class AuthoritativeSwordArrayPlayer : MonoBehaviour
    {
        public const string VisualTypeName = "SwordArray";
        internal const int BaseFormationBladeCount = 4;
        internal const int MaximumContactsPerBlade = 5;

        private const string SpriteResource =
            "Sprites/Cards/Cutout/huixuanjian-v1";
        private const int MaximumCriticalAccents = 4;
        private const float CriticalAccentDuration = 0.11f;
        private static readonly Vector2 TipUv = new Vector2(0.930f, 0.930f);
        private static readonly Vector2 PommelUv = new Vector2(0.155f, 0.145f);

        internal readonly struct BladeContract
        {
            internal BladeContract(int attackIndex, int waveIndex,
                bool outer, int contactCount, int receiptCount)
            {
                AttackIndex = attackIndex;
                WaveIndex = waveIndex;
                IsOuter = outer;
                ContactCount = contactCount;
                ReceiptCount = receiptCount;
            }

            internal int AttackIndex { get; }
            internal int WaveIndex { get; }
            internal bool IsOuter { get; }
            internal int ContactCount { get; }
            internal int ReceiptCount { get; }
        }

        private sealed class Contact
        {
            internal readonly List<AuthoritativeProjectileHit> Receipts =
                new List<AuthoritativeProjectileHit>();
            internal Vector3 Position;
            internal float At;
        }

        private sealed class Blade
        {
            internal int AttackIndex;
            internal int WaveIndex;
            internal int SlotIndex;
            internal int OrderInWave;
            internal bool IsOuter;
            internal bool IsFinalWave;
            internal readonly List<Contact> Contacts = new List<Contact>();
            internal GameObject Root;
            internal SpriteRenderer Sprite;
            internal TrailRenderer Trail;
            internal Vector3 Origin;
            internal Vector3 Slot;
            internal float WaveStart;
            internal float AssembleEnd;
            internal float Launch;
            internal float ReturnAt;
            internal float WaveEnd;
        }

        private sealed class Stroke
        {
            internal LineRenderer Line;
            internal Color Color;
            internal float Width;
        }

        private sealed class ArrayRig
        {
            internal GameObject Root;
            internal readonly List<Stroke> Strokes = new List<Stroke>();
        }

        private sealed class CriticalAccent
        {
            internal GameObject Root;
            internal readonly List<Stroke> Strokes = new List<Stroke>();
            internal float Birth;
        }

        private sealed class Cast
        {
            internal readonly List<Blade> Blades = new List<Blade>();
            internal readonly List<CriticalAccent> Accents =
                new List<CriticalAccent>();
            internal ArrayRig Rig;
            internal Vector3 Center;
            internal float FinalStart;
            internal float End;
            internal bool Released;
        }

        private readonly HashSet<Cast> _casts = new HashSet<Cast>();

        internal static IEnumerator Play(MonoBehaviour host,
            VisualEffectManager manager,
            IReadOnlyList<CombatAttackPresentationQueue.PendingHit> receipts,
            VfxProfile profile, BoardVisualFrame? board, Vector3? castOrigin,
            AttackPlan plan, Func<bool> active,
            Action<AuthoritativeProjectileHit> onContact,
            AttackEffectCompletion completion = null)
        {
            if (host == null || manager == null || profile == null ||
                active == null || !active())
                yield break;

            var owner = host.GetComponent<AuthoritativeSwordArrayPlayer>() ??
                host.gameObject.AddComponent<AuthoritativeSwordArrayPlayer>();
            yield return owner.Animate(manager, receipts, profile, board,
                castOrigin, plan, active, onContact, completion);
        }

        internal static IReadOnlyList<BladeContract> DescribeContract(
            AttackPlan plan,
            IReadOnlyList<CombatAttackPresentationQueue.PendingHit> receipts)
        {
            Cast cast = BuildCast(receipts, plan);
            var result = new List<BladeContract>(cast.Blades.Count);
            for (int i = 0; i < cast.Blades.Count; i++)
            {
                Blade blade = cast.Blades[i];
                int receiptCount = 0;
                for (int contact = 0; contact < blade.Contacts.Count; contact++)
                    receiptCount += blade.Contacts[contact].Receipts.Count;
                result.Add(new BladeContract(blade.AttackIndex,
                    blade.WaveIndex, blade.IsOuter,
                    blade.Contacts.Count, receiptCount));
            }
            return result.AsReadOnly();
        }

        private IEnumerator Animate(VisualEffectManager manager,
            IReadOnlyList<CombatAttackPresentationQueue.PendingHit> receipts,
            VfxProfile profile, BoardVisualFrame? board, Vector3? castOrigin,
            AttackPlan plan, Func<bool> active,
            Action<AuthoritativeProjectileHit> onContact,
            AttackEffectCompletion completion)
        {
            if (plan == null || plan.SubHits.Count == 0)
            {
                Debug.LogError("回旋剑阵缺少Core已选定的完整攻击计划；不从部分命中推测攻击物数量。");
                yield break;
            }

            Cast cast = BuildCast(receipts, plan);
            if (cast.Blades.Count == 0) yield break;
            Sprite sprite = profile.projectileSprite != null
                ? profile.projectileSprite
                : Resources.Load<Sprite>(SpriteResource);
            if (sprite == null)
            {
                Debug.LogError("回旋剑阵缺少已批准的回旋剑Sprite；不生成替代轮廓。");
                yield break;
            }

            cast.Center = ResolveFormationCenter(
                castOrigin ?? manager.playerAttackOrigin?.position ??
                Vector3.zero, profile);
            Schedule(cast, profile);
            cast.Rig = CreateArrayRig(manager.effectContainer, cast.Center,
                profile, ResolveSorting(board, cast.Center) - 3);

            var arrivals = new List<(Blade blade, Contact contact)>();
            for (int i = 0; i < cast.Blades.Count; i++)
            {
                Blade blade = cast.Blades[i];
                for (int contact = 0; contact < blade.Contacts.Count; contact++)
                    arrivals.Add((blade, blade.Contacts[contact]));
            }
            arrivals.Sort((left, right) =>
            {
                int time = left.contact.At.CompareTo(right.contact.At);
                return time != 0 ? time :
                    left.blade.AttackIndex.CompareTo(right.blade.AttackIndex);
            });

            _casts.Add(cast);
            double started = Time.timeAsDouble;
            int arrived = 0;
            try
            {
                while (!cast.Released && isActiveAndEnabled && active())
                {
                    float elapsed = (float)(Time.timeAsDouble - started);
                    bool faulted = false;
                    try
                    {
                        SampleRig(cast, elapsed, profile);
                        for (int i = 0; i < cast.Blades.Count; i++)
                            SampleBlade(cast.Blades[i], cast, elapsed, sprite,
                                profile, board, manager.projectileContainer);

                        while (arrived < arrivals.Count &&
                            arrivals[arrived].contact.At <= elapsed && active())
                        {
                            Contact contact = arrivals[arrived++].contact;
                            manager.PlaySmallHitEffect(contact.Position, profile, completion: completion);
                            bool critical = false;
                            for (int receipt = 0;
                                receipt < contact.Receipts.Count; receipt++)
                            {
                                AuthoritativeProjectileHit hit =
                                    contact.Receipts[receipt];
                                critical |= hit.DamageEvent.IsCritical;
                                if (!active()) break;
                                onContact?.Invoke(hit);
                            }
                            if (critical)
                                AddCriticalAccent(cast, manager.effectContainer,
                                    contact.Position, elapsed, profile,
                                    ResolveSorting(board, contact.Position) + 3);
                        }
                        SampleCriticalAccents(cast, elapsed);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        faulted = true;
                    }

                    if (faulted || elapsed >= cast.End) break;
                    yield return null;
                }
            }
            finally
            {
                Release(cast);
            }
        }

        private static Cast BuildCast(
            IReadOnlyList<CombatAttackPresentationQueue.PendingHit> receipts,
            AttackPlan plan)
        {
            var result = new Cast();
            if (plan == null) return result;

            var groups = new SortedDictionary<int, Blade>();
            for (int i = 0; i < plan.SubHits.Count; i++)
            {
                int attackIndex = plan.SubHits[i].AttackInstanceIndex;
                if (!groups.ContainsKey(attackIndex))
                    groups.Add(attackIndex,
                        new Blade { AttackIndex = attackIndex });
            }

            for (int i = 0; receipts != null && i < receipts.Count; i++)
            {
                CombatAttackPresentationQueue.PendingHit receipt = receipts[i];
                DamageEvent damage = receipt.damageEvent;
                if (!receipt.hasWorldAnchor ||
                    (damage.Kind != DamageKind.Direct &&
                     damage.Kind != DamageKind.Bounce) ||
                    !groups.TryGetValue(damage.AttackInstanceIndex,
                        out Blade blade) ||
                    blade.Contacts.Count >= MaximumContactsPerBlade)
                    continue;

                var contact = new Contact { Position = receipt.worldAnchor };
                contact.Receipts.Add(new AuthoritativeProjectileHit(
                    damage, receipt.worldAnchor, i));
                for (int next = i + 1; next < receipts.Count; next++)
                {
                    CombatAttackPresentationQueue.PendingHit extra =
                        receipts[next];
                    if (extra.damageEvent.Kind != DamageKind.Splash ||
                        extra.damageEvent.AttackInstanceIndex !=
                            damage.AttackInstanceIndex ||
                        extra.damageEvent.SubHitIndex != damage.SubHitIndex)
                        break;
                    contact.Receipts.Add(new AuthoritativeProjectileHit(
                        extra.damageEvent,
                        extra.hasWorldAnchor ? extra.worldAnchor :
                            contact.Position, next));
                }
                blade.Contacts.Add(contact);
            }

            int count = groups.Count;
            int fullGroups = count / BaseFormationBladeCount;
            int innerCount = fullGroups * BaseFormationBladeCount;
            int finalCompleteWave = Mathf.Max(0, fullGroups - 1);
            int ordinal = 0;
            foreach (Blade blade in groups.Values)
            {
                if (fullGroups == 0 || ordinal < innerCount)
                {
                    blade.WaveIndex = fullGroups == 0
                        ? 0
                        : ordinal / BaseFormationBladeCount;
                    blade.SlotIndex = ordinal % BaseFormationBladeCount;
                    blade.OrderInWave = blade.SlotIndex;
                }
                else
                {
                    // Amount-created attack objects reinforce the last full
                    // four-symbol formation as an outer ring. This changes no
                    // count or target fact and remains valid when receipts are empty.
                    blade.WaveIndex = finalCompleteWave;
                    blade.SlotIndex = ordinal - innerCount;
                    blade.OrderInWave = BaseFormationBladeCount +
                        blade.SlotIndex;
                    blade.IsOuter = true;
                }
                result.Blades.Add(blade);
                ordinal++;
            }
            return result;
        }

        private static void Schedule(Cast cast, VfxProfile profile)
        {
            int waveCount = 0;
            for (int i = 0; i < cast.Blades.Count; i++)
                waveCount = Mathf.Max(waveCount,
                    cast.Blades[i].WaveIndex + 1);

            float waveStart = 0f;
            float flight = Mathf.Max(0.08f, profile.flightDuration);
            float delay = Mathf.Max(0.04f, profile.multiProjectileDelay);
            float assemble = Mathf.Max(0.12f,
                profile.swordArrayAssembleDuration);
            float returnDuration = Mathf.Max(0.08f,
                profile.swordArrayReturnDuration);
            float radius = Mathf.Max(0.65f, profile.swordArrayRadius);

            for (int wave = 0; wave < waveCount; wave++)
            {
                float waveEnd = waveStart + assemble;
                int outerCount = 0;
                for (int i = 0; i < cast.Blades.Count; i++)
                    if (cast.Blades[i].WaveIndex == wave &&
                        cast.Blades[i].IsOuter)
                        outerCount++;

                for (int i = 0; i < cast.Blades.Count; i++)
                {
                    Blade blade = cast.Blades[i];
                    if (blade.WaveIndex != wave) continue;
                    blade.IsFinalWave = wave == waveCount - 1;
                    blade.Origin = cast.Center;
                    blade.Slot = cast.Center + ResolveSlot(blade,
                        outerCount, radius);
                    blade.WaveStart = waveStart;
                    blade.AssembleEnd = waveStart + assemble;
                    blade.Launch = blade.AssembleEnd +
                        blade.OrderInWave * delay;
                    float cursor = blade.Launch;
                    for (int contact = 0;
                        contact < blade.Contacts.Count; contact++)
                    {
                        cursor += flight;
                        blade.Contacts[contact].At = cursor;
                    }
                    blade.ReturnAt = cursor + returnDuration;
                    waveEnd = Mathf.Max(waveEnd, blade.ReturnAt + 0.08f);
                }

                for (int i = 0; i < cast.Blades.Count; i++)
                    if (cast.Blades[i].WaveIndex == wave)
                        cast.Blades[i].WaveEnd = waveEnd;
                waveStart = waveEnd + 0.05f;
            }

            cast.FinalStart = Mathf.Max(0f, waveStart - 0.05f);
            cast.End = cast.FinalStart + Mathf.Max(0.1f,
                profile.swordArrayCollapseDuration);
        }

        private static Vector3 ResolveSlot(Blade blade, int outerCount,
            float radius)
        {
            if (blade.IsOuter)
            {
                float angle = 90f -
                    360f * blade.SlotIndex / Mathf.Max(1, outerCount);
                float radians = angle * Mathf.Deg2Rad;
                return new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) *
                    radius * 1.38f;
            }

            switch (blade.SlotIndex)
            {
                case 0: return Vector3.up * radius;
                case 1: return Vector3.right * radius * 0.92f;
                case 2: return Vector3.down * radius * 0.72f;
                default: return Vector3.left * radius * 0.92f;
            }
        }

        private static void SampleBlade(Blade blade, Cast cast,
            float elapsed, Sprite sprite, VfxProfile profile,
            BoardVisualFrame? board, Transform parent)
        {
            if (elapsed < blade.WaveStart) return;
            if ((!blade.IsFinalWave && elapsed >= blade.WaveEnd) ||
                elapsed >= cast.End)
            {
                DestroyBlade(blade);
                return;
            }
            EnsureBlade(blade, sprite, profile, parent);

            Vector3 from = blade.Origin;
            Vector3 to = blade.Slot;
            float fromTime = blade.WaveStart;
            float until = blade.AssembleEnd;
            int phase = -2;

            if (elapsed >= blade.AssembleEnd)
            {
                from = blade.Slot;
                to = blade.Slot;
                fromTime = blade.AssembleEnd;
                until = blade.Launch;
                phase = -1;
            }
            if (elapsed >= blade.Launch)
            {
                from = blade.Slot;
                fromTime = blade.Launch;
                if (blade.Contacts.Count == 0)
                {
                    // A planned attack object can legitimately lose every
                    // target before presentation. It remains in formation;
                    // no decorative lunge is allowed to imply a false hit.
                    to = blade.Slot;
                    until = blade.ReturnAt;
                    phase = 1;
                }
                else
                {
                    phase = 0;
                    for (int i = 0; i < blade.Contacts.Count; i++)
                    {
                        to = blade.Contacts[i].Position;
                        until = blade.Contacts[i].At;
                        phase = i;
                        if (elapsed < until) break;
                        from = to;
                        fromTime = until;
                    }
                    if (elapsed >= until)
                    {
                        phase = blade.Contacts.Count;
                        to = blade.Slot;
                        until = blade.ReturnAt;
                    }
                }
            }
            if (elapsed >= blade.ReturnAt)
            {
                from = blade.Slot;
                to = blade.Slot;
                fromTime = blade.ReturnAt;
                until = blade.WaveEnd;
                phase = blade.Contacts.Count + 1;
            }
            if (blade.IsFinalWave && elapsed >= cast.FinalStart)
            {
                from = blade.Slot;
                to = cast.Center;
                fromTime = cast.FinalStart;
                until = cast.End;
                phase = blade.Contacts.Count + 2;
            }

            float progress = Mathf.Clamp01((elapsed - fromTime) /
                Mathf.Max(0.001f, until - fromTime));
            Vector3 position;
            Vector3 direction;
            if (phase >= 0 && phase <= blade.Contacts.Count)
            {
                Vector3 control = (from + to) * 0.5f +
                    ArcOffset(from, to, phase, blade.AttackIndex);
                float oneMinus = 1f - progress;
                position = oneMinus * oneMinus * from +
                    2f * oneMinus * progress * control +
                    progress * progress * to;
                direction = 2f * oneMinus * (control - from) +
                    2f * progress * (to - control);
            }
            else
            {
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                position = Vector3.LerpUnclamped(from, to, eased);
                direction = phase == -2 ? blade.Slot - cast.Center :
                    new Vector3(-(blade.Slot - cast.Center).y,
                        (blade.Slot - cast.Center).x, 0f);
            }

            if (direction.sqrMagnitude > 0.00001f)
                blade.Root.transform.rotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 45f);
            Vector2 localTip = (Vector2.Scale(TipUv, sprite.rect.size) -
                sprite.pivot) / sprite.pixelsPerUnit;
            Vector3 tipOffset = blade.Root.transform.rotation *
                ((Vector3)localTip * blade.Root.transform.localScale.x);
            blade.Root.transform.position = position - tipOffset;

            int sorting = ResolveSorting(board, position);
            blade.Sprite.sortingOrder = sorting;
            if (blade.Trail != null)
            {
                blade.Trail.sortingOrder = sorting - 1;
                blade.Trail.emitting = blade.Contacts.Count > 0 &&
                    elapsed >= blade.Launch &&
                    elapsed < blade.ReturnAt;
            }

            float alpha = Mathf.Clamp01((elapsed - blade.WaveStart) / 0.06f);
            if (blade.IsFinalWave && elapsed >= cast.FinalStart)
                alpha *= 1f - Mathf.SmoothStep(0f, 1f,
                    (elapsed - cast.FinalStart) /
                    Mathf.Max(0.001f, cast.End - cast.FinalStart));
            blade.Sprite.color = new Color(0.84f, 0.96f, 1f, alpha);
        }

        private static void EnsureBlade(Blade blade, Sprite sprite,
            VfxProfile profile, Transform parent)
        {
            if (blade.Root != null) return;
            blade.Root = new GameObject("AuthoritySwordArrayBlade",
                typeof(SpriteRenderer));
            blade.Root.transform.SetParent(parent, false);
            blade.Sprite = blade.Root.GetComponent<SpriteRenderer>();
            blade.Sprite.sprite = sprite;
            Vector2 swordPixels = Vector2.Scale(TipUv - PommelUv,
                sprite.rect.size);
            float length = swordPixels.magnitude / sprite.pixelsPerUnit;
            blade.Root.transform.localScale = Vector3.one *
                Mathf.Max(0.1f, profile.projectileScale) /
                Mathf.Max(0.01f, length);

            if (!profile.enableTrail) return;
            blade.Trail = blade.Root.AddComponent<TrailRenderer>();
            blade.Trail.sharedMaterial =
                ProceduralParticleMaterial.GetAlphaBlendedBox();
            blade.Trail.time = profile.trailTime;
            blade.Trail.startWidth = profile.trailWidth;
            blade.Trail.endWidth = 0f;
            blade.Trail.startColor = profile.trailColor;
            Color clear = profile.trailColor;
            clear.a = 0f;
            blade.Trail.endColor = clear;
            blade.Trail.minVertexDistance = 0.03f;
            blade.Trail.emitting = false;
        }

        private static ArrayRig CreateArrayRig(Transform parent,
            Vector3 center, VfxProfile profile, int sortingOrder)
        {
            var rig = new ArrayRig
            {
                Root = new GameObject("AuthoritySwordArrayFormation")
            };
            rig.Root.transform.SetParent(parent, false);
            rig.Root.transform.position = center;
            float radius = Mathf.Max(0.65f, profile.swordArrayRadius);
            AddLoop(rig, "GoldOuterRing", 32, radius * 1.08f,
                profile.swordArrayRuneColor, 0.026f, sortingOrder);
            AddLoop(rig, "CyanInnerRing", 24, radius * 0.57f,
                profile.swordArrayEnergyColor, 0.018f, sortingOrder + 1);
            AddDiamond(rig, radius * 0.78f,
                profile.swordArrayRuneColor, 0.020f, sortingOrder + 1);
            for (int i = 0; i < BaseFormationBladeCount; i++)
            {
                float angle = (45f + i * 90f) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle),
                    Mathf.Sin(angle), 0f);
                AddSegment(rig, "FourSymbolSpoke", direction * radius * 0.2f,
                    direction * radius * 0.92f,
                    profile.swordArrayEnergyColor, 0.014f,
                    sortingOrder + 1);
            }
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle),
                    Mathf.Sin(angle), 0f);
                Vector3 tangent = new Vector3(-direction.y, direction.x, 0f);
                Vector3 centerPoint = direction * radius * 1.08f;
                AddSegment(rig, "GoldArrayTick",
                    centerPoint - tangent * radius * 0.08f,
                    centerPoint + tangent * radius * 0.08f,
                    profile.swordArrayRuneColor, 0.024f, sortingOrder);
            }
            return rig;
        }

        private static void AddLoop(ArrayRig rig, string name, int count,
            float radius, Color color, float width, int sortingOrder)
        {
            LineRenderer line = AddStroke(rig, name, color, width,
                sortingOrder);
            line.loop = true;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle),
                    Mathf.Sin(angle), 0f) * radius);
            }
        }

        private static void AddDiamond(ArrayRig rig, float radius,
            Color color, float width, int sortingOrder)
        {
            LineRenderer line = AddStroke(rig, "GoldArrayDiamond", color,
                width, sortingOrder);
            line.loop = true;
            line.positionCount = 4;
            line.SetPosition(0, Vector3.up * radius);
            line.SetPosition(1, Vector3.right * radius);
            line.SetPosition(2, Vector3.down * radius);
            line.SetPosition(3, Vector3.left * radius);
        }

        private static void AddSegment(ArrayRig rig, string name,
            Vector3 from, Vector3 to, Color color, float width,
            int sortingOrder)
        {
            LineRenderer line = AddStroke(rig, name, color, width,
                sortingOrder);
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
        }

        private static LineRenderer AddStroke(ArrayRig rig, string name,
            Color color, float width, int sortingOrder)
        {
            var child = new GameObject(name, typeof(LineRenderer));
            child.transform.SetParent(rig.Root.transform, false);
            LineRenderer line = child.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = ProceduralParticleMaterial.GetAdditive();
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 2;
            line.sortingOrder = sortingOrder;
            rig.Strokes.Add(new Stroke
                { Line = line, Color = color, Width = width });
            return line;
        }

        private static void SampleRig(Cast cast, float elapsed,
            VfxProfile profile)
        {
            if (cast.Rig?.Root == null) return;
            float assemble = Mathf.Max(0.12f,
                profile.swordArrayAssembleDuration);
            float alpha = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(elapsed / assemble));
            float scale = Mathf.Lerp(0.32f, 1f, alpha);
            if (elapsed >= cast.FinalStart)
            {
                float collapse = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((elapsed - cast.FinalStart) /
                    Mathf.Max(0.001f, cast.End - cast.FinalStart)));
                alpha *= 1f - collapse;
                scale *= 1f - collapse * 0.92f;
            }
            cast.Rig.Root.transform.localScale = Vector3.one * scale;
            cast.Rig.Root.transform.localRotation = Quaternion.Euler(0f, 0f,
                elapsed * 34f);
            for (int i = 0; i < cast.Rig.Strokes.Count; i++)
            {
                Stroke stroke = cast.Rig.Strokes[i];
                if (stroke.Line == null) continue;
                Color color = stroke.Color;
                color.a *= alpha * (0.88f +
                    Mathf.Sin(elapsed * 8f + i * 0.7f) * 0.12f);
                stroke.Line.startColor = color;
                stroke.Line.endColor = color;
                stroke.Line.startWidth = stroke.Width *
                    Mathf.Lerp(0.6f, 1f, alpha);
                stroke.Line.endWidth = stroke.Line.startWidth;
            }
        }

        private static void AddCriticalAccent(Cast cast, Transform parent,
            Vector3 position, float elapsed, VfxProfile profile,
            int sortingOrder)
        {
            while (cast.Accents.Count >= MaximumCriticalAccents)
            {
                CriticalAccent oldest = cast.Accents[0];
                cast.Accents.RemoveAt(0);
                if (oldest.Root != null) Destroy(oldest.Root);
            }

            var accent = new CriticalAccent
            {
                Root = new GameObject("AuthoritySwordArrayCriticalCross"),
                Birth = elapsed
            };
            accent.Root.transform.SetParent(parent, false);
            accent.Root.transform.position = position;
            accent.Root.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            float length = Mathf.Max(0.32f,
                profile.projectileScale * 0.42f);
            AddAccentStroke(accent, Vector3.left * length,
                Vector3.right * length, Color.white, 0.075f,
                sortingOrder);
            AddAccentStroke(accent, Vector3.down * length * 0.72f,
                Vector3.up * length * 0.72f,
                profile.swordArrayRuneColor, 0.052f,
                sortingOrder + 1);
            cast.Accents.Add(accent);
        }

        private static void AddAccentStroke(CriticalAccent accent,
            Vector3 from, Vector3 to, Color color, float width,
            int sortingOrder)
        {
            var child = new GameObject("CriticalSwordLight",
                typeof(LineRenderer));
            child.transform.SetParent(accent.Root.transform, false);
            LineRenderer line = child.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = ProceduralParticleMaterial.GetAdditive();
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = 0f;
            line.numCapVertices = 2;
            line.sortingOrder = sortingOrder;
            accent.Strokes.Add(new Stroke
                { Line = line, Color = color, Width = width });
        }

        private static void SampleCriticalAccents(Cast cast, float elapsed)
        {
            for (int i = cast.Accents.Count - 1; i >= 0; i--)
            {
                CriticalAccent accent = cast.Accents[i];
                float progress = (elapsed - accent.Birth) /
                    CriticalAccentDuration;
                if (progress >= 1f)
                {
                    if (accent.Root != null) Destroy(accent.Root);
                    cast.Accents.RemoveAt(i);
                    continue;
                }
                float eased = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(progress));
                accent.Root.transform.localScale = Vector3.one *
                    Mathf.Lerp(0.45f, 1.2f, eased);
                for (int strokeIndex = 0;
                    strokeIndex < accent.Strokes.Count; strokeIndex++)
                {
                    Stroke stroke = accent.Strokes[strokeIndex];
                    Color color = stroke.Color;
                    color.a *= 1f - eased;
                    stroke.Line.startColor = color;
                    stroke.Line.endColor = color;
                }
            }
        }

        private static Vector3 ArcOffset(Vector3 from, Vector3 to,
            int phase, int index)
        {
            Vector3 direction = to - from;
            float side = ((phase + index) & 1) == 0 ? 1f : -1f;
            if (direction.sqrMagnitude < 0.01f)
                return new Vector3(side * 0.72f, 0.46f, 0f);
            return new Vector3(-direction.y, direction.x, 0f).normalized *
                side * Mathf.Clamp(direction.magnitude * 0.18f,
                    0.28f, 0.82f);
        }

        private static Vector3 ResolveFormationCenter(Vector3 origin,
            VfxProfile profile)
        {
            Camera camera = Camera.main;
            if (camera == null || !camera.orthographic)
                return origin + Vector3.up * 1.05f;
            float worldPerPixel = 2f * camera.orthographicSize /
                Mathf.Max(1, Screen.height);
            float clearance = profile.castingArtifactCardClearancePixels *
                Screen.height / 1080f * worldPerPixel;
            return origin + Vector3.up *
                (clearance + profile.projectileScale * 0.9f);
        }

        private static int ResolveSorting(BoardVisualFrame? board,
            Vector3 position)
        {
            if (!board.HasValue) return 44;
            Vector3 front = board.Value.Project(0.5f, 0f);
            Vector3 back = board.Value.Project(0.5f, 1f);
            Vector3 axis = back - front;
            float depth = axis.sqrMagnitude > 0.00001f
                ? Vector3.Dot(position - front, axis) / axis.sqrMagnitude
                : 0f;
            return Mathf.Max(44,
                board.Value.SortingOrderAtDepth(Mathf.Clamp01(depth)) + 4);
        }

        private static void DestroyBlade(Blade blade)
        {
            if (blade.Root == null) return;
            Destroy(blade.Root);
            blade.Root = null;
            blade.Sprite = null;
            blade.Trail = null;
        }

        private void Release(Cast cast)
        {
            if (cast.Released) return;
            cast.Released = true;
            _casts.Remove(cast);
            for (int i = 0; i < cast.Blades.Count; i++)
                DestroyBlade(cast.Blades[i]);
            if (cast.Rig?.Root != null)
                Destroy(cast.Rig.Root);
            for (int i = 0; i < cast.Accents.Count; i++)
                if (cast.Accents[i].Root != null)
                    Destroy(cast.Accents[i].Root);
            cast.Accents.Clear();
        }

        public void CancelAll()
        {
            foreach (Cast cast in new List<Cast>(_casts))
                Release(cast);
        }

        private void OnDisable() => CancelAll();
        private void OnDestroy() => CancelAll();
    }
}
