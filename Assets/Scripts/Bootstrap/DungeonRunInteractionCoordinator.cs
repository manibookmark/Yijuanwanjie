using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 将 Run 事务的只读阶段映射为选择面板、宿主页和获得反馈。候选、规则、
    /// 回执及恢复进度只属于 Dungeon；本类仅拥有当前面板和输入生命周期。
    /// 每个成功动作先保存，再关闭选择页或播放结果；关闭面板不等于提交。
    /// 规模审查：超过 500 行及字段关注阈值，但可变字段仍只属于这一面板生命周期；
    /// 文件尾的作者内容适配器无业务状态。新增独立窗口流程时应重新划分边界。
    /// </summary>
    public sealed class DungeonRunInteractionCoordinator : MonoBehaviour
    {
        private DungeonRunSession _run;
        private Func<DungeonRunInteractionCatalog> _getCatalog;
        private Func<CombatRuleSet> _getCombatRules;
        private SealAssignmentCoordinator _assignment;
        private Action _persist;
        private Action _gemPersist;
        private Action _crystalPersist;
        private Action _persistPayment;
        private DungeonRunDoubleKeyCoordinator _payment;
        private Canvas _canvas;
        private RewardChoiceOverlay _choices;
        private RewardFeedbackPresenter _reveal;
        private IDisposable _flowLease;
        private CardConfig _resultCard;
        private readonly List<LingyinConfig> _temporarySeals =
            new List<LingyinConfig>();
        private readonly List<CardConfig> _targetCards = new List<CardConfig>();
        private string _nodeId;
        private Action<DungeonRunInteractionOffer> _onCommitted;
        private Action _onCancelled;
        private DungeonRunInteractionOffer _completed;
        private bool _active;
        private bool _actionPending;
        private bool _assigning;
        private int _generation;

        public void Bind(
            DungeonRunSession run,
            Func<DungeonRunInteractionCatalog> getCatalog,
            SealAssignmentCoordinator assignment,
            Action persist,
            Func<CombatRuleSet> getCombatRules = null,
            Action persistPayment = null, Action persistGem = null)
        {
            _run = run;
            _getCatalog = getCatalog;
            _assignment = assignment;
            _persist = persist;
            _gemPersist = persistGem;
            _persistPayment = persistPayment;
            _getCombatRules = getCombatRules;
        }

        public static bool Handles(LevelNode node)
        {
            if (node?.completionEffects == null)
                return false;
            foreach (LevelInteractionEffect effect in node.completionEffects)
            {
                if (effect == null) continue;
                if (DungeonRunInteractionTransaction.Handles(effect.kind))
                    return true;
            }
            return false;
        }

        public bool TryBegin(
            string nodeId,
            Action<DungeonRunInteractionOffer> onCommitted,
            Action onCancelled,
            out string error)
        {
            error = null;
            if (_active)
            {
                error = "已有地图选择正在处理中。";
                return false;
            }
            _active = true;
            _actionPending = false;
            _nodeId = nodeId;
            _onCommitted = onCommitted;
            _onCancelled = onCancelled;
            _generation++;
            _flowLease = SceneTransitionRuntime.ExternalModalBarrier?.Hold(
                "modal.map.run-interaction");
            try
            {
                if (_run == null || _getCatalog == null)
                    ShowProblem("探索状态尚未准备好，请返回地图后重试。");
                else if (_run.DoubleKeyPayments.RequiresPayment(nodeId))
                {
                    _payment ??= gameObject.AddComponent<DungeonRunDoubleKeyCoordinator>();
                    _payment.Begin(_run, nodeId, _getCatalog, _persistPayment,
                        EnsureCanvas(), ResumeReward, Cancel);
                }
                else if (!_run.Interactions.TryInspect(
                             nodeId, _getCatalog(), out var offer, out error))
                    ShowProblem(error);
                else
                    Present(offer);
            }
            catch (Exception exception)
            {
                ShowProblem(exception.Message);
            }
            return true;
        }

        private void ResumeReward()
        {
            try
            {
                if (!_run.Interactions.TryInspect(_nodeId, _getCatalog(), out var offer, out string error))
                    ShowProblem(error);
                else Present(offer);
            }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private bool IsPaidDoubleKey()
        {
            LevelNode node = _run?.CurrentFloor.FindNode(_nodeId);
            return node?.completionEffects?.Count == 1 && node.completionEffects[0]?.HasDoubleKeyPayment == true &&
                !_run.DoubleKeyPayments.RequiresPayment(_nodeId);
        }

        /// <summary>Resume only the durable natural choices from a real Crystal hit.
        /// Draining these choices never completes the Crystal node. The Crystal
        /// presentation owner resumes its clock after this callback.</summary>
        public bool TryResumeCrystalRewards(string nodeId, Action strictPersist,
            Action onRewardsDrained, out string error)
        {
            error = null;
            if (_active)
            { error = "已有地图选择正在处理中。"; return false; }
            if (_run == null || _run.Crystals.PendingNodeContentId != nodeId ||
                !_run.Interactions.HasPendingChoice || _run.Interactions.PendingNodeContentId != nodeId)
            { error = "晶体没有待处理的升级奖励。"; return false; }
            if (onRewardsDrained == null || strictPersist == null)
            { error = "晶体奖励缺少严格保存或继续投牌的消费者。"; return false; }
            _crystalPersist = strictPersist;
            return TryBegin(nodeId, _ =>
            {
                if (_run.Interactions.HasPendingChoice)
                { ShowProblem("本次升级奖励尚未领完，请继续选择。"); return; }
                CloseSilently();
                onRewardsDrained();
            }, null, out error);
        }

        private bool IsCrystalReward()
            => _run != null && _nodeId != null &&
                _run.Crystals.PendingNodeContentId == _nodeId;

        private void Present(DungeonRunInteractionOffer offer)
        {
            if (!_active) return;
            if (offer.Phase == DungeonRunInteractionPhase.Completed)
            {
                _completed = offer;
                _actionPending = true;
                Action<DungeonRunInteractionOffer> committed = _onCommitted;
                _onCommitted = null;
                committed?.Invoke(offer);
                return;
            }
            ClosePanels();
            switch (offer.Phase)
            {
                case DungeonRunInteractionPhase.Ready:
                    Commit(null);
                    break;
                case DungeonRunInteractionPhase.SilverPayment:
                case DungeonRunInteractionPhase.RewardChoice:
                case DungeonRunInteractionPhase.EvolutionChoice:
                case DungeonRunInteractionPhase.SealChoice:
                    ShowChoices(offer, 0);
                    break;
                case DungeonRunInteractionPhase.SealTarget:
                    ShowSealTargets(offer);
                    break;
                default:
                    ShowProblem(offer.Message, offer.Title);
                    break;
            }
        }

        private void ShowChoices(DungeonRunInteractionOffer offer, int page)
        {
            var selectable = new List<DungeonRunInteractionOption>();
            var actions = new List<RewardUtilityAction>();
            string title = offer.Phase == DungeonRunInteractionPhase.SilverPayment
                ? offer.Title + " · 尚需 " + string.Join("、", offer.PaymentRemainingRequirements.Select(type =>
                    type == SideEventSilverGemChestPaymentRules.AttackSourceAssetId ? "攻击" :
                    type == SideEventSilverGemChestPaymentRules.DefenseSourceAssetId ? "防御" : "特殊"))
                : offer.Title;
            foreach (DungeonRunInteractionOption option in offer.Options)
            {
                if (option.Id == "cash-out")
                {
                    string choiceId = option.Id;
                    actions.Add(new RewardUtilityAction(
                        RewardUtilityActionKind.CashOut,
                        option.Title,
                        () => Commit(choiceId, closeChoices: true)));
                }
                else
                    selectable.Add(option);
            }
            if (selectable.Count == 0)
            {
                // A frozen empty offer can still carry a legal cash-out. The
                // choice grid requires an item; use the existing action-only
                // decision without creating a reward or committing on close.
                if (actions.Count > 0)
                {
                    if (!IsPaidDoubleKey() && !IsCrystalReward())
                        actions.Add(new RewardUtilityAction(
                            RewardUtilityActionKind.Abandon, "返回地图", Cancel));
                    _reveal ??= RewardFeedbackPresenter.Create(this);
                    _reveal.ShowDecision(EnsureCanvas(), new RewardFeedbackItem
                    {
                        Kind = RewardFeedbackKind.Unavailable,
                        Title = title,
                        DisplayName = offer.Phase == DungeonRunInteractionPhase.SealChoice
                            ? "没有可选灵印" : "没有可选奖励",
                        Description = string.IsNullOrWhiteSpace(offer.Message)
                            ? "可将本次奖励化为灵玉。" : offer.Message
                    }, null, actions);
                    return;
                }
                ShowProblem(offer.Phase == DungeonRunInteractionPhase.SilverPayment
                    ? "当前没有符合剩余要求的卡牌，可返回地图。" : offer.Message, title);
                return;
            }
            const int pageSize = 3;
            int lastPage = (selectable.Count - 1) / pageSize;
            page = Math.Max(0, Math.Min(lastPage, page));
            int offset = page * pageSize;
            var visible = new List<DungeonRunInteractionOption>();
            var items = new List<RewardFeedbackItem>();
            DungeonRunInteractionCatalog catalog = _getCatalog();
            if (offer.Phase == DungeonRunInteractionPhase.SilverPayment && _targetCards.Count == 0)
            {
                if (!_run.TryGetCurrentCards(catalog.Cards, out var paymentCards, out string paymentError))
                { ShowProblem(paymentError, title); return; }
                _targetCards.AddRange(paymentCards);
            }
            for (int i = offset; i < Math.Min(offset + pageSize, selectable.Count); i++)
            {
                visible.Add(selectable[i]);
                items.Add(ToChoiceItem(selectable[i], offer.Phase, catalog));
            }
            int currentPage = page;
            if (page > 0)
                actions.Add(new RewardUtilityAction(
                    RewardUtilityActionKind.Exclude, "上一页",
                    () => ChangePage(offer, currentPage - 1)));
            if (page < lastPage)
                actions.Add(new RewardUtilityAction(
                    RewardUtilityActionKind.Refresh, "下一页",
                    () => ChangePage(offer, currentPage + 1)));
            if (!IsPaidDoubleKey() && !IsCrystalReward())
                actions.Add(new RewardUtilityAction(
                    RewardUtilityActionKind.Abandon, "返回地图", Cancel));
            _choices ??= gameObject.AddComponent<RewardChoiceOverlay>();
            _choices.Show(EnsureCanvas(), title, items, actions,
                index =>
                {
                    if (index >= 0 && index < visible.Count)
                        Commit(visible[index].Id, index, closeChoices: true);
                }, RewardCardTooltipContext.FromRun(_run, catalog.Cards, _getCombatRules?.Invoke()));
            GlobalButtonAudioRuntime.BindHierarchyNow(_choices.Root);
        }

        private void ChangePage(DungeonRunInteractionOffer offer, int page)
        {
            if (!_active || _actionPending) return;
            try { ShowChoices(offer, page); }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private RewardFeedbackItem ToChoiceItem(
            DungeonRunInteractionOption option,
            DungeonRunInteractionPhase phase,
            DungeonRunInteractionCatalog catalog)
        {
            if (option.Seal != null)
                return RewardFeedbackItem.FromSeal(ResolveSeal(
                    option.Seal, option.Title, option.Description));
            CardConfig card = phase == DungeonRunInteractionPhase.SilverPayment
                ? _targetCards.FirstOrDefault(candidate => candidate.runtimeInstanceId == option.CardRuntimeInstanceId)
                : FindCard(catalog.Cards, option.CardContentId);
            if ((phase == DungeonRunInteractionPhase.RewardChoice || phase == DungeonRunInteractionPhase.SilverPayment) && card != null)
                return RewardFeedbackItem.FromCard(option.Id, card);
            return new RewardFeedbackItem
            {
                ReceiptId = option.Id,
                Kind = RewardFeedbackKind.Generic,
                DisplayName = option.Title,
                Description = option.Description,
                Artwork = card != null ? card.cardArt : null
            };
        }

        private void ShowSealTargets(DungeonRunInteractionOffer offer)
        {
            if (_assignment == null || offer.SelectedSeal == null)
            {
                ShowProblem("暂时无法打开钤印选择，请重试当前奖励。");
                return;
            }
            DungeonRunInteractionCatalog catalog = _getCatalog();
            if (!_run.TryGetCurrentCards(catalog.Cards, out var cards, out string error))
            {
                ShowProblem(error);
                return;
            }
            _targetCards.AddRange(cards);
            var targets = new List<CardConfig>();
            var choiceByInstance = new Dictionary<int, string>();
            foreach (DungeonRunInteractionOption option in offer.Options)
            {
                if (option.CardRuntimeInstanceId <= 0) continue;
                foreach (CardConfig card in cards)
                {
                    if (card.runtimeInstanceId != option.CardRuntimeInstanceId)
                        continue;
                    targets.Add(card);
                    choiceByInstance.Add(card.runtimeInstanceId, option.Id);
                    break;
                }
            }
            if (targets.Count == 0)
            {
                ShowUnavailableSealTargets(offer, offer.Message);
                return;
            }
            LingyinConfig seal = ResolveSeal(offer.SelectedSeal,
                description: offer.Options[0].Description);
            _assigning = true;
            if (!_assignment.TryBeginAssignment(
                    seal, targets,
                    instanceId =>
                    {
                        if (choiceByInstance.TryGetValue(instanceId, out string choiceId))
                            Commit(choiceId);
                        else
                            ShowProblem("这张卡牌已不在当前候选中，请重新选择。");
                    },
                    () => Commit("back"),
                    showRewardReveal: false,
                    out error))
            {
                ShowUnavailableSealTargets(offer, error);
                return;
            }
        }

        private void ShowUnavailableSealTargets(
            DungeonRunInteractionOffer offer, string message)
        {
            ClosePanels();
            var actions = new List<RewardUtilityAction>();
            foreach (DungeonRunInteractionOption option in offer.Options)
                if (option.Id == "cash-out")
                    actions.Add(new RewardUtilityAction(
                        RewardUtilityActionKind.CashOut, option.Title,
                        () => Commit("cash-out")));
            actions.Add(new RewardUtilityAction(
                RewardUtilityActionKind.Abandon, "重新选印", () => Commit("back")));
            _reveal ??= RewardFeedbackPresenter.Create(this);
            _reveal.ShowDecision(EnsureCanvas(), new RewardFeedbackItem
            {
                Kind = RewardFeedbackKind.Unavailable,
                Title = "暂不可用",
                DisplayName = "没有可钤印的卡牌",
                Description = string.IsNullOrWhiteSpace(message)
                    ? "需要有空印位的卡牌" : message
            }, null, actions);
        }

        private void Commit(
            string choiceId,
            int selectedIndex = -1,
            bool closeChoices = false)
        {
            if (!_active || _actionPending) return;
            _actionPending = true;
            int generation = _generation;
            try
            {
                if (!_run.Interactions.TryCommit(
                        _nodeId, choiceId, _getCatalog(), out var offer, out string error,
                        _run.CurrentFloor.FindNode(_nodeId)?.completionEffects?.FirstOrDefault()?.kind == LevelInteractionEffectKind.GemChest
                            ? _gemPersist : _crystalPersist ?? _persist))
                {
                    ShowProblem(error);
                    return;
                }
                Action show = () =>
                {
                    if (!_active || generation != _generation) return;
                    _actionPending = false;
                    Present(offer);
                };
                if (closeChoices && _choices != null &&
                    (selectedIndex >= 0
                        ? _choices.PlaySelectionAndClose(selectedIndex, show)
                        : _choices.PlayClose(show)))
                    return;
                show();
            }
            catch (Exception exception)
            {
                ShowProblem(exception.Message);
            }
        }

        /// <summary>调用方完成地图节点后播放结果；不再修改任何奖励状态。</summary>
        public void PlayCommitted(Action onClosed)
        {
            Action finish = () =>
            {
                CloseSilently();
                onClosed?.Invoke();
            };
            if (!_active || _completed == null || !_completed.Receipt.NewlyApplied)
            {
                finish();
                return;
            }
            try
            {
                if (_assigning)
                {
                    _assignment.PlayCommitted(finish);
                    return;
                }
                ClosePanels();
                LevelInteractionReceipt receipt = _completed.Receipt;
                RewardFeedbackItem item;
                if (receipt.Kind == LevelInteractionEffectKind.GrantCard ||
                    receipt.Kind == LevelInteractionEffectKind.RefineCard)
                    item = RewardFeedbackItem.FromCard(receipt.EffectId,
                        RestoreResultCard(receipt));
                else if ((receipt.Kind == LevelInteractionEffectKind.ChooseSeal || receipt.Kind == LevelInteractionEffectKind.GemChest) &&
                         string.IsNullOrWhiteSpace(receipt.TargetContentId))
                    {
                        item = RewardFeedbackItem.FromJade(receipt.EffectId,
                            receipt.Amount, "灵玉已经收入行囊。");
                        item.ValueBefore = receipt.ValueBefore;
                        item.ValueAfter = receipt.ValueAfter;
                    }
                else if (receipt.Kind == LevelInteractionEffectKind.GrantExperience)
                {
                    item = RewardFeedbackItem.FromInteractionReceipt(receipt);
                    item.Description = $"当前等级 {_run.CombatProgression.CurrentLevel}，本次升级所得已经收好。";
                }
                else
                {
                    item = RewardFeedbackItem.FromInteractionReceipt(receipt);
                    item.Title = _completed.Title;
                    if (!string.IsNullOrWhiteSpace(_completed.Message))
                        item.Description = _completed.Message;
                }
                ShowFinalReveal(item, finish);
            }
            catch (Exception exception)
            {
                Debug.LogError("[DungeonRunInteraction] 结果反馈失败：" + exception.Message);
                ClosePanels();
                ShowFinalReveal(new RewardFeedbackItem
                {
                    Kind = RewardFeedbackKind.Generic,
                    Title = "奖励已收好",
                    DisplayName = _completed.Title,
                    Description = "本次奖励已经入账，结果画面暂时无法显示。点击返回地图。"
                }, finish);
            }
        }

        private CardConfig RestoreResultCard(LevelInteractionReceipt receipt)
        {
            DungeonRunInteractionCatalog catalog = _getCatalog();
            if (!_run.TryGetCurrentCards(catalog.Cards, out var cards, out string error))
                throw new InvalidOperationException(error);
            foreach (CardConfig card in cards)
            {
                if (card.runtimeInstanceId == receipt.TargetRuntimeInstanceId)
                    _resultCard = card;
                else
                    Destroy(card);
            }
            if (_resultCard == null)
                throw new InvalidOperationException("找不到已入账的结果卡牌。");
            return _resultCard;
        }

        private void ShowFinalReveal(RewardFeedbackItem item, Action finish)
        {
            _reveal ??= RewardFeedbackPresenter.Create(this);
            _reveal.Show(EnsureCanvas(), item, finish);
        }

        private void ShowProblem(string message, string title = "暂不可用")
        {
            if (!_active) return;
            _actionPending = false;
            ClosePanels();
            _reveal ??= RewardFeedbackPresenter.Create(this);
            _reveal.Show(EnsureCanvas(), new RewardFeedbackItem
            {
                Kind = RewardFeedbackKind.Unavailable,
                Title = string.IsNullOrWhiteSpace(title) ? "暂不可用" : title,
                DisplayName = "暂不可用",
                Description = string.IsNullOrWhiteSpace(message)
                    ? (IsCrystalReward() ? "请重试当前升级奖励。" : "当前没有可执行的选项，请返回地图后再来。")
                    : message
            }, IsPaidDoubleKey() || IsCrystalReward() ? (Action)ResumeReward : Cancel);
        }

        private void Cancel()
        {
            if (!_active || _actionPending) return;
            if (IsPaidDoubleKey() || IsCrystalReward()) { ResumeReward(); return; }
            LevelInteractionEffect effect = _run?.CurrentFloor.FindNode(_nodeId)?.completionEffects?.FirstOrDefault();
            if (DungeonRunGemChests.SupportsDurableLeave(effect))
            {
                if (!_run.GemChests.TryCommit(_nodeId, "leave", _getCatalog(), out _, out string error, _gemPersist))
                { ShowProblem(error); return; }
            }
            Action cancelled = _onCancelled;
            CloseSilently();
            cancelled?.Invoke();
        }

        public void CloseSilently()
        {
            _generation++;
            _active = false;
            _actionPending = false;
            _nodeId = null;
            _onCommitted = null;
            _onCancelled = null;
            _crystalPersist = null;
            _completed = default;
            _payment?.CloseSilently();
            ClosePanels();
            _flowLease?.Dispose();
            _flowLease = null;
            if (_resultCard != null) Destroy(_resultCard);
            _resultCard = null;
            foreach (LingyinConfig seal in _temporarySeals)
                if (seal != null) Destroy(seal);
            _temporarySeals.Clear();
        }

        private void ClosePanels()
        {
            _choices?.Close();
            _reveal?.CloseSilently();
            if (_assigning)
                _assignment?.CloseSilently();
            _assigning = false;
            foreach (CardConfig card in _targetCards)
                if (card != null) Destroy(card);
            _targetCards.Clear();
        }

        private LingyinConfig ResolveSeal(
            CardSealSpec spec, string title = null, string description = null)
        {
            if (LingyinCatalog.TryGet(spec.contentId, out LingyinConfig authored))
                return authored;
            foreach (LingyinConfig cached in _temporarySeals)
                if (string.Equals(cached.contentId, spec.contentId, StringComparison.Ordinal))
                    return cached;
            var seal = ScriptableObject.CreateInstance<LingyinConfig>();
            seal.hideFlags = HideFlags.HideAndDontSave;
            seal.contentId = spec.contentId;
            seal.effectType = spec.effect;
            seal.effectValue = spec.value;
            seal.grade = LingyinGrade.Common;
            seal.material = LingyinMaterial.Copper;
            seal.displayName = title ?? (spec.effect switch
            {
                CardSealEffect.Draw => "引卷印",
                CardSealEffect.Armor => "罡气印",
                CardSealEffect.Amount => "数量印",
                _ => "灵印"
            });
            seal.sealCharacter = spec.effect switch
            {
                CardSealEffect.Draw => "引",
                CardSealEffect.Armor => "罡",
                CardSealEffect.Amount => "数",
                _ => "印"
            };
            seal.description = description ?? string.Empty;
            _temporarySeals.Add(seal);
            return seal;
        }

        private static CardConfig FindCard(IReadOnlyList<CardConfig> cards, string id)
        {
            if (cards == null || string.IsNullOrWhiteSpace(id)) return null;
            foreach (CardConfig card in cards)
                if (card != null && string.Equals(card.contentId, id, StringComparison.OrdinalIgnoreCase))
                    return card;
            return null;
        }

        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;
            if (FindObjectOfType<EventSystem>() == null)
            {
                var events = new GameObject("RunInteractionEventSystem",
                    typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
            var go = new GameObject("RunInteractionCanvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32080;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return _canvas;
        }

        private void OnDestroy() => CloseSilently();
    }

    /// <summary>把权威作者内容和现有奖励规则转换为 Dungeon 的只读事务输入。</summary>
    internal static class DungeonRunInteractionCatalogAdapter
    {
        public static DungeonRunInteractionCatalog Create(
            DungeonRunSession run,
            IReadOnlyList<CardConfig> cards,
            string volumeId,
            IDungeonRunInteractionWallet wallet,
            PlayerStats baseStats,
            IEnumerable<string> unlockedSealContentIds,
            MetaProgression account = null)
        {
            var recipes = new List<DungeonRunEvolutionRecipe>();
            if (!string.Equals(volumeId, "common", StringComparison.OrdinalIgnoreCase))
                AddRecipes("common", recipes);
            AddRecipes(volumeId, recipes);
            LevelUpRewardRuleResolution rewards = NaturalLevelUpRewardCatalog.ResolveGlobal(
                cards, volumeId, account, CardTestStatus.IsReady,
                runContext: new CardUnlockRunContext(run.RunId, () => run.RunProgression.CardUnlockFacts.Capture(),
                    run.LeaderCrawlerContentId));
            string scheduleId =
                run.CurrentFloor.Profile.levelUpOfferScheduleProfileId;
            ResolvedLevelUpOfferSchedule schedule = null;
            if (!string.IsNullOrWhiteSpace(scheduleId))
            {
                LevelUpOfferScheduleProfile scheduleProfile =
                    LevelUpOfferScheduleProfileCatalog.Get(scheduleId);
                if (scheduleProfile == null)
                {
                    throw new InvalidOperationException(
                        "当前关卡的固定升级 Offer 排程尚未准备好。");
                }
                schedule = LevelUpOfferScheduleAdapter.Resolve(
                    scheduleProfile,
                    cards,
                    CardTestStatus.IsReady,
                    canReceiveSeals: () => SealRewardEligibility.CanReceiveSeals(account));
            }
            return new DungeonRunInteractionCatalog
            {
                Cards = cards,
                Recipes = recipes,
                RewardCards = rewards.Cards,
                RewardConfig = rewards.Config,
                NaturalRewardPool = rewards.NaturalPool,
                CanReceiveSeals = SealRewardEligibility.CanReceiveSeals(account),
                IsEligible = CardTestStatus.CanUseInFormalPlay, IsEvolutionResultEligible = CardTestStatus.CanUseEvolutionResult,
                GrowthPercent = run.RunProgression.ProjectGrowth(baseStats?.mingWu ?? 0),
                LuckPercent = run.RunProgression.ProjectLuck(baseStats?.tianYun ?? 0),
                Wallet = wallet,
                GemChestOfferResolver = new DungeonRunGemChestOfferResolver(run, account, ContentCatalog.Current, allowSilverRunDeckPayment: true),
                SealRewardPools = CreateSealRewardPools(
                    unlockedSealContentIds),
                LevelRewardOfferResolver = schedule
            };
        }

        internal static IReadOnlyList<DungeonRunSealRewardPool>
            CreateSealRewardPools(IEnumerable<string> unlockedSealContentIds)
        {
            var unlocked = (unlockedSealContentIds ?? Array.Empty<string>())
                .ToArray();
            IReadOnlyList<CardSealSpec> candidates =
                LingyinCatalog.GetUnlockedCopperChestPool(unlocked);
            return new[]
            {
                new DungeonRunSealRewardPool
                {
                    ContentId = LingyinCatalog.UnlockedCopperChestPoolId,
                    Candidates = candidates
                },
                new DungeonRunSealRewardPool
                {
                    ContentId = LingyinCatalog.UnlockedSilverChestPoolId,
                    Candidates = candidates,
                    PreferredCandidateCount = 1,
                    PreferredCandidateContentIds =
                        LingyinCatalog.GetUnlockedSilverChestPreferredContentIds(
                            unlocked)
                },
                new DungeonRunSealRewardPool
                {
                    ContentId = LingyinCatalog.UnlockedHighTierChestPoolId,
                    Candidates = LingyinCatalog.GetUnlockedHighTierChestPool(
                        unlocked),
                    IsEvolutionResultEligible = CardTestStatus.CanUseEvolutionResult,
                    PreferredCandidateCount = 1,
                    PreferredCandidateContentIds =
                        LingyinCatalog.GetUnlockedHighTierChestPreferredContentIds(
                            unlocked)
                }
            };
        }

        private static void AddRecipes(string volumeId, List<DungeonRunEvolutionRecipe> recipes)
        {
            foreach (EvolutionData recipe in ContentCatalog.Current.GetVolume(volumeId).evolutions)
                recipes.Add(new DungeonRunEvolutionRecipe
                {
                    ContentId = recipe.contentId,
                    PrimaryCardContentId = recipe.primaryCardContentId,
                    SecondaryCardContentIds = recipe.secondaryCardContentIds,
                    ResultCardContentId = recipe.resultCardContentId
                });
        }
    }
}
