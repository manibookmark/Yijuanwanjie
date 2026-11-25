using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Read-only source/admission dependencies. Dungeon freezes every
    /// actual offer; these providers are never consulted to replace a pending choice.</summary>
    internal sealed class AgentGameSourceInputs
    {
        internal Func<string, CardStatOfferingPreparedContext> PrepareStats;
        internal Func<string, DuplicateOfferingPreparedContext> PrepareDuplicate;
        internal Func<DungeonRunArcanaCatalog> ArcanaCatalog;
        internal Func<string, ArcanaConfig> ArcanaPresentation;
        internal Func<InvitationPreparedContext> InvitationContext;
        internal Func<string, bool> CanAdmitInvitation;

        internal static AgentGameSourceInputs Production(DungeonRunSession run,
            IReadOnlyList<CardConfig> cards, MetaProgression account)
        {
            var invitation = new DungeonRunInvitationContext(account);
            return new AgentGameSourceInputs {
                PrepareStats = node => {
                    AgentGameInteractions.Require(DungeonRunCardStatOfferingSource.TryPrepare(run, node, cards,
                        out var context, out var error), error); return context;
                },
                PrepareDuplicate = node => {
                    AgentGameInteractions.Require(DungeonRunDuplicateOfferingSource.TryPrepare(run, node, cards,
                        account.EventUnlocks.QueryDuplicateOfferingEligibility(), out var context, out var error), error);
                    return context;
                },
                ArcanaCatalog = () => {
                    ContentCatalog.Current.TryGetRelic(DungeonRunArcanaCatalogResolver.LegacyAcquisitionRelicContentId, out var relic);
                    return DungeonRunArcanaCatalogResolver.ResolveFromContent(account,
                        Resources.LoadAll<ArcanaConfig>("Content/Arcanas"), relic);
                },
                ArcanaPresentation = id => Resources.LoadAll<ArcanaConfig>("Content/Arcanas").SingleOrDefault(value => value.contentId == id),
                InvitationContext = () => invitation.Prepare(cards),
                CanAdmitInvitation = invitation.CanAdmitCard
            };
        }
    }

    /// <summary>Adapts dedicated source owners to explicit text commands. The route
    /// registry is both capability discovery and dispatch; no local offer, payment,
    /// random stream, default selection or completion flag competes with Dungeon.</summary>
    internal sealed class AgentGameSourceInteractions
    {
        private readonly DungeonRunSession _run;
        private readonly IReadOnlyList<CardConfig> _cards;
        private readonly Action _persist;
        private readonly Func<IDungeonRunFlatCashOutWallet> _wallet;
        private readonly AgentGameSourceInputs _inputs;
        private readonly Dictionary<LevelInteractionEffectKind, Route> _routes;
        private static readonly Action ForbidWrite = () => throw new InvalidOperationException("Observation attempted to open or save a source choice.");

        internal AgentGameSourceInteractions(DungeonRunSession run, IReadOnlyList<CardConfig> cards,
            Action persist, Func<IDungeonRunFlatCashOutWallet> wallet, AgentGameSourceInputs inputs)
        {
            _run = run; _cards = cards; _persist = persist; _wallet = wallet; _inputs = inputs;
            var invitation = new Route(CheckInvitation, BeginInvitation, ObserveInvitation, CommitInvitation);
            _routes = new Dictionary<LevelInteractionEffectKind, Route> {
                { LevelInteractionEffectKind.CardStatOffering, new Route(CheckStats, BeginStats, ObserveStats, CommitStats) },
                { LevelInteractionEffectKind.DuplicateOffering, new Route(CheckDuplicate, BeginDuplicate, ObserveDuplicate, CommitDuplicate) },
                { LevelInteractionEffectKind.ChooseArcana, new Route(CheckArcana, BeginArcana, ObserveArcana, CommitArcana) },
                { LevelInteractionEffectKind.CrawlerInvitation, invitation },
                { LevelInteractionEffectKind.GunForHire, invitation }
            };
        }

        internal bool Handles(LevelNode node) => node?.completionEffects?.Any(e => e != null && _routes.ContainsKey(e.kind)) == true;
        private Route Resolve(LevelNode node)
            => node?.completionEffects?.Count == 1 && node.completionEffects[0] != null &&
                _routes.TryGetValue(node.completionEffects[0].kind, out var route) ? route : null;
        internal string Availability(LevelNode node, out string reason)
        {
            reason = null; Route route = Resolve(node);
            if (route == null) { reason = "A dedicated source interaction requires one complete typed effect."; return "Unimplemented"; }
            try { reason = route.Check(node); }
            catch (Exception exception) { reason = exception.Message; }
            return reason == null ? null : "ConditionUnavailable";
        }
        internal void Begin(LevelNode node) => RequireRoute(node).Begin(node);
        internal AgentGameOffer Observe(LevelNode node) => RequireRoute(node).Observe(node);
        internal void Commit(LevelNode node, AgentGameOption option)
        {
            Route route = RequireRoute(node);
            // Only an exact currently advertised choice can cross the owner boundary.
            AgentGameOption current = route.Observe(node).options.SingleOrDefault(value => value.choiceId == option.choiceId);
            AgentGameInteractions.Require(current != null && current.kind == option.kind && current.contentId == option.contentId &&
                current.runtimeInstanceId == option.runtimeInstanceId, "Source choice is outside the current frozen offer.");
            route.Commit(node, current);
        }
        private Route RequireRoute(LevelNode node) => Resolve(node) ?? throw new InvalidOperationException("No executable source interaction route.");

        private string CheckStats(LevelNode node)
        {
            if (_run.CardStatOfferings.HasPendingChoice) return null;
            _inputs.PrepareStats(node.contentId); return null; // Pure preparation; empty subsets still permit flat cash.
        }
        private void BeginStats(LevelNode node)
        {
            if (_run.CardStatOfferings.TryInspect(node.contentId, out _, out _)) return;
            AgentGameInteractions.Require(_run.CardStatOfferings.TryOpen(node.contentId, _inputs.PrepareStats(node.contentId),
                out _, out var error, _persist), error);
        }
        private AgentGameOffer ObserveStats(LevelNode node)
        {
            AgentGameInteractions.Require(_run.CardStatOfferings.TryInspect(node.contentId, out var source, out var error), error);
            var offer = Offer(node, "CardStatOffering", "属性供奉", "献出精确实例请求本局属性增量，实际按上限结算；也可不献牌领取200灵玉，化玉不受敛玉加成。");
            WithCards(cards => {
                foreach (var candidate in source.Candidates)
                {
                    CardConfig card = cards.Single(value => value.runtimeInstanceId == candidate.RuntimeInstanceId);
                    offer.options.Add(CardOption("offer/", "OfferCard", card,
                        "移除此实例及附印；请求增加本局属性：" + string.Join("、", candidate.RequestedRewards.Select(value => StatText(value.SourceStat, value.RequestedAmount)))));
                }
            });
            offer.options.Add(new AgentGameOption { choiceId = "cash-out", kind = "CashOut", title = "不献牌 · 领取200灵玉", amount = 200 });
            offer.options.Add(Leave()); return offer;
        }
        private void CommitStats(LevelNode node, AgentGameOption option)
        {
            string error;
            if (option.choiceId == "return-map") AgentGameInteractions.Require(_run.CardStatOfferings.TryLeave(node.contentId, out error, _persist), error);
            else if (option.choiceId == "cash-out") AgentGameInteractions.Require(_run.CardStatOfferings.TryCashOutAndComplete(node.contentId, _wallet(), out _, out error, _persist), error);
            else AgentGameInteractions.Require(_run.CardStatOfferings.TrySubmitAndComplete(node.contentId, option.runtimeInstanceId, out _, out error, _persist), error);
        }

        private string CheckDuplicate(LevelNode node)
        {
            if (_run.DuplicateOfferings.HasPendingChoice) return null;
            _inputs.PrepareDuplicate(node.contentId); return null;
        }
        private void BeginDuplicate(LevelNode node)
        {
            if (_run.DuplicateOfferings.TryInspect(node.contentId, out var source, out _) && !source.IsDormant) return;
            AgentGameInteractions.Require(_run.DuplicateOfferings.TryOpen(node.contentId, _inputs.PrepareDuplicate(node.contentId),
                out _, out var error, _persist), error);
        }
        private AgentGameOffer ObserveDuplicate(LevelNode node)
        {
            AgentGameInteractions.Require(_run.DuplicateOfferings.TryInspect(node.contentId, out var source, out var error), error);
            var offer = Offer(node, "DuplicateOffering", "拓印处", "保留原牌，选择一个精确实例拓印为本局新实体；没有候选时可以离开。");
            WithCards(cards => {
                foreach (var candidate in source.Candidates.Where(value => value.IsAvailable))
                {
                    CardConfig original = cards.Single(value => value.runtimeInstanceId == candidate.RuntimeInstanceId);
                    CardConfig preview = DungeonRunDuplicateOfferingSource.BuildPreview(original, _cards, source.LimitBreakPolicy);
                    try {
                        AgentGameOption option = CardOption("duplicate/", "DuplicateCard", original,
                            source.LimitBreakPolicy == PersistentDuplicateLimitBreakPolicy.Reset ? "保留附印，拓印牌突破归零；不消耗原牌。" : "按已冻结突破规则拓印；不消耗原牌。");
                        option.card = AgentGameCard.Copy(preview); offer.options.Add(option);
                    } finally { AgentGameInteractions.DestroyCards(new[] { preview }); }
                }
            });
            offer.options.Add(Leave()); return offer;
        }
        private void CommitDuplicate(LevelNode node, AgentGameOption option)
        {
            string error;
            if (option.choiceId == "return-map") AgentGameInteractions.Require(_run.DuplicateOfferings.TryLeave(node.contentId, out error, _persist), error);
            else AgentGameInteractions.Require(DungeonRunDuplicateOfferingSource.TrySubmit(_run, node.contentId,
                option.runtimeInstanceId, _cards, _persist, out _, out error), error);
        }

        private string CheckArcana(LevelNode node)
            => _run.Arcanas.HasPendingChoice || _inputs.ArcanaCatalog().AcquisitionUnlocked ? null : "尚未获得局内天机的永久资格。";
        private void BeginArcana(LevelNode node)
        {
            if (_run.Arcanas.HasPendingChoice)
            {
                // A domain/scene interruption can retract the floor entry while
                // retaining the durable Arcana offer. Explicit re-entry restores
                // that matching floor marker without consulting today's catalog.
                ObserveArcana(node);
                if (_run.CurrentFloor.GetNodeState(node.contentId) == LevelNodeViewState.Available)
                    AgentGameInteractions.Require(_run.CurrentFloor.TryBeginInteraction(node.contentId, out var resumeError), resumeError);
                return;
            }
            DungeonRunArcanaCatalog catalog = _inputs.ArcanaCatalog();
            AgentGameInteractions.Require(_run.CurrentFloor.TryBeginInteraction(node.contentId, out var error), error);
            if (_run.Arcanas.TryOpen(node.contentId, catalog, out _, out error, _persist)) return;
            // The Arcana owner restores its failed transaction; retract the
            // prerequisite unsaved floor entry as well.
            AgentGameInteractions.Require(_run.CurrentFloor.TryCancelUncommittedInteraction(node.contentId, out var cancelError), cancelError);
            throw new InvalidOperationException(error);
        }
        private AgentGameOffer ObserveArcana(LevelNode node)
        {
            AgentGameInteractions.Require(_run.Arcanas.HasPendingChoice, "No frozen Arcana offer remains.");
            AgentGameInteractions.Require(_run.Arcanas.TryOpen(node.contentId, null, out var source, out var error, ForbidWrite), error);
            var offer = Offer(node, "ArcanaChoice", "天机选择", "选择一项本局天机，或放弃并完成此节点；不更换账号装备。");
            foreach (var candidate in source.Candidates)
            {
                DungeonRunArcanaCatalogResolver.RequireImplementedDefinition(candidate);
                ArcanaConfig presentation = _inputs.ArcanaPresentation?.Invoke(candidate.ContentId);
                offer.options.Add(new AgentGameOption { choiceId = "arcana/" + candidate.ContentId, kind = "ChooseArcana",
                    contentId = candidate.ContentId, title = presentation?.arcanaName ?? candidate.ContentId,
                    description = presentation?.description ?? candidate.ContentId });
            }
            offer.options.Add(new AgentGameOption { choiceId = "skip", kind = "Skip", title = "放弃天机并完成节点" }); return offer;
        }
        private void CommitArcana(LevelNode node, AgentGameOption option)
        {
            string error;
            if (option.choiceId == "skip") AgentGameInteractions.Require(_run.Arcanas.TrySkipAndComplete(node.contentId, out _, out error, _persist), error);
            else AgentGameInteractions.Require(_run.Arcanas.TryChooseAndComplete(node.contentId, option.contentId, out _, out error, _persist), error);
        }

        private string CheckInvitation(LevelNode node)
        {
            if (_run.Invitations.HasPendingChoice) return null;
            if (node.completionEffects[0].kind == LevelInteractionEffectKind.GunForHire &&
                !_run.Invitations.TryCanAdoptLegacy(node.contentId, out var error)) return error;
            return _run.Invitations.CanGenerate(_inputs.InvitationContext()) ? null : "当前资格、英灵容量或已解锁且未拥有的正式召唤牌不满足请灵条件。";
        }
        private void BeginInvitation(LevelNode node)
        {
            if (_run.Invitations.TryInspect(node.contentId, out _, out _)) return;
            AgentGameInteractions.Require(_run.Invitations.TryOpen(node.contentId, _inputs.InvitationContext(), out _, out var error, _persist), error);
        }
        private AgentGameOffer ObserveInvitation(LevelNode node)
        {
            if (node.completionEffects[0].kind == LevelInteractionEffectKind.GunForHire &&
                !_run.Invitations.TryCanAdoptLegacy(node.contentId, out var legacyError))
            {
                // A frozen native paid quote is evidence for its old owner,
                // never authorization to replace it with today's free policy.
                var blocked = Offer(node, "GunForHire", "旧版雇佣需要诊断", legacyError);
                blocked.phase = "Unavailable";
                return blocked;
            }
            AgentGameInteractions.Require(_run.Invitations.TryInspect(node.contentId, out var source, out var error), error);
            var offer = Offer(node, "CrawlerInvitation", "请灵台", "免费选择一位已解锁英灵加入本局，下一场起手保证；已打开的选择必须结清。");
            List<CardConfig> previews = InvitationCardProjection.InstantiateCandidates(source, _cards);
            try {
                foreach (CardConfig card in previews)
                    offer.options.Add(new AgentGameOption { choiceId = "invite/" + card.contentId, kind = "InviteCrawler",
                        contentId = card.contentId, title = card.cardName, description = offer.message, card = AgentGameCard.Copy(card) });
            } finally { InvitationCardProjection.Release(previews); }
            return offer;
        }
        private void CommitInvitation(LevelNode node, AgentGameOption option)
        {
            AgentGameInteractions.Require(_inputs.CanAdmitInvitation(option.contentId), "召唤牌尚未通过正式发布门禁。");
            AgentGameInteractions.Require(_run.Invitations.TryChooseAndComplete(node.contentId, option.contentId, _cards,
                out _, out var error, _persist), error);
        }

        private void WithCards(Action<List<CardConfig>> read)
        {
            AgentGameInteractions.Require(_run.TryGetCurrentCards(_cards, out var cards, out var error), error);
            try { read(cards); } finally { AgentGameInteractions.DestroyCards(cards); }
        }
        private static AgentGameOption CardOption(string prefix, string kind, CardConfig card, string description)
            => new AgentGameOption { choiceId = prefix + card.runtimeInstanceId, kind = kind, runtimeInstanceId = card.runtimeInstanceId,
                contentId = card.contentId, title = card.cardName, card = AgentGameCard.Copy(card), description = description };
        private static AgentGameOffer Offer(LevelNode node, string kind, string title, string message)
            => new AgentGameOffer { kind = kind, nodeContentId = node.contentId, phase = "Choice", title = title, message = message };
        private static AgentGameOption Leave() => new AgentGameOption { choiceId = "return-map", kind = "ReturnToMap", title = "离开（不支付、不完成）" };
        private static string StatText(int sourceStat, int amount)
        {
            if (sourceStat == 2) return "每回合护甲 +" + amount;
            if (sourceStat == 13) return "还魂 +" + amount + "%";
            string label = sourceStat switch { 0 => "裂空", 1 => "数量", 4 => "锋锐", 5 => "明悟", 6 => "敛玉", 7 => "天运", 10 => "标准抽牌", 11 => "引卷", 17 => "招煞", _ => "来源属性 " + sourceStat };
            return label + " +" + amount;
        }
        private sealed class Route
        {
            internal readonly Func<LevelNode, string> Check;
            internal readonly Action<LevelNode> Begin;
            internal readonly Func<LevelNode, AgentGameOffer> Observe;
            internal readonly Action<LevelNode, AgentGameOption> Commit;
            internal Route(Func<LevelNode, string> check, Action<LevelNode> begin,
                Func<LevelNode, AgentGameOffer> observe, Action<LevelNode, AgentGameOption> commit)
            { Check = check; Begin = begin; Observe = observe; Commit = commit; }
        }
    }
}
