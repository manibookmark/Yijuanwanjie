using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    public enum RunCardConversionSelectionKind
    {
        Card,
        CashOut
    }

    public readonly struct RunCardConversionSelection
    {
        public RunCardConversionSelectionKind Kind { get; }
        public int CardRuntimeInstanceId { get; }
        public bool IsCashOut => Kind == RunCardConversionSelectionKind.CashOut;

        private RunCardConversionSelection(
            RunCardConversionSelectionKind kind,
            int cardRuntimeInstanceId)
        {
            Kind = kind;
            CardRuntimeInstanceId = Math.Max(0, cardRuntimeInstanceId);
        }

        public static RunCardConversionSelection Card(int runtimeInstanceId)
            => new RunCardConversionSelection(
                RunCardConversionSelectionKind.Card,
                runtimeInstanceId);

        public static RunCardConversionSelection CashOut()
            => new RunCardConversionSelection(
                RunCardConversionSelectionKind.CashOut,
                0);
    }

    /// <summary>
    /// 归元选择的纯表现协调器。它只把玩家选择回传给地图流程；卡牌移除、
    /// Run 修正、灵玉与回执均由处理器和 Dungeon 事务提交。
    /// </summary>
    public sealed class RunCardConversionCoordinator : MonoBehaviour
    {
        private RunCardConversionInteractionEffectHandler _handler;
        private Canvas _canvas;
        private RunCardConversionOverlay _overlay;
        private string _activeEffectId;
        private RunCardConversionSelection _pendingSelection;
        private bool _submissionPending;
        private bool _presentationPending;
        private bool _manaConversion;
        private int _generation;
        // These are detached preview instances, not the Run's saved cards.
        // Their lifetime follows this presentation, including retry and cancel.
        private readonly List<CardConfig> _candidateCards = new List<CardConfig>();

        public bool IsActive => _overlay != null && _overlay.IsShown;
        public bool IsManaConversion => _manaConversion;

        public void Bind(RunCardConversionInteractionEffectHandler handler)
        {
            _handler = handler ??
                throw new ArgumentNullException(nameof(handler));
        }

        public bool TryBegin(
            LevelInteractionEffect effect,
            Vector2 mapTargetScreenPosition,
            Action<RunCardConversionSelection> onSelected,
            Action onCancelled,
            out string error)
        {
            error = null;
            if (_handler == null)
            {
                error = "归元选择协调器尚未绑定处理器。";
                return false;
            }
            if (!_handler.TryGetCandidates(
                    effect,
                    out RunCardConversionRuleProfile profile,
                    out List<CardConfig> candidates,
                    out error))
                return false;

            CloseSilently();
            _candidateCards.AddRange(candidates);
            int generation = ++_generation;
            _activeEffectId = effect.effectId;
            _submissionPending = false;
            bool manaConversion = profile.RequiresManaPickup;
            _manaConversion = manaConversion;
            var options = new List<RunCardConversionOptionViewData>();
            for (int i = 0; i < candidates.Count; i++)
            {
                CardConfig card = candidates[i];
                if (!profile.TryResolveRule(card, out RunCardConversionRule rule))
                    continue;
                options.Add(CreateCardChoice(card, rule, _handler.PreviewConversionAmount(rule), manaConversion));
            }

            _overlay ??= gameObject.AddComponent<RunCardConversionOverlay>();
            _overlay.Show(
                EnsureCanvas(),
                options,
                _handler.PreviewCashOut(effect),
                mapTargetScreenPosition,
                intent =>
                {
                    if (generation != _generation || _submissionPending)
                        return;
                    _pendingSelection = intent.Kind ==
                        RunCardConversionOverlayIntentKind.CashOut
                            ? RunCardConversionSelection.CashOut()
                            : RunCardConversionSelection.Card(
                                intent.CardRuntimeInstanceId);
                    _submissionPending = true;
                    onSelected?.Invoke(_pendingSelection);
                },
                () =>
                {
                    if (generation != _generation)
                        return;
                    ReleaseCandidateCards();
                    ResetActiveSubmission();
                    onCancelled?.Invoke();
                },
                new RunCardConversionPresentation(
                    manaConversion ? "选择一张牌化为灵力" : "选择一张牌归元",
                    manaConversion ? "聚灵" : "归元",
                    profile.AllowsCashOut, profile.AllowsLeave, manaConversion));
            GlobalButtonAudioRuntime.BindHierarchyNow(_overlay.Root);
            return true;
        }

        public bool ShowCommitFailure(
            LevelInteractionEffect effect,
            string error)
        {
            if (!_submissionPending || _overlay == null)
                return false;
            if (!_pendingSelection.IsCashOut)
            {
                if (!_handler.TryGetCandidates(
                        effect, out _, out List<CardConfig> candidates, out _))
                    return false;
                bool stillLegal;
                try
                {
                    stillLegal = candidates.Exists(card => card != null &&
                        card.runtimeInstanceId == _pendingSelection.CardRuntimeInstanceId);
                }
                finally
                {
                    foreach (CardConfig candidate in candidates)
                        if (candidate != null) Destroy(candidate);
                }
                if (!stillLegal)
                    return false;
            }
            if (!_overlay.ShowCommitFailure(error))
                return false;
            _submissionPending = false;
            _pendingSelection = default;
            return true;
        }

        public bool PlayCommitted(
            LevelInteractionReceipt receipt,
            Action onClosed)
        {
            if (!_submissionPending || _presentationPending ||
                _overlay == null ||
                receipt.Kind != LevelInteractionEffectKind.ConvertCard ||
                !receipt.NewlyApplied ||
                !string.Equals(
                    receipt.EffectId,
                    _activeEffectId,
                    StringComparison.OrdinalIgnoreCase) ||
                receipt.TargetRuntimeInstanceId !=
                    _pendingSelection.CardRuntimeInstanceId)
            {
                return false;
            }

            _presentationPending = true;
            bool completed = false;
            Action finish = () =>
            {
                if (completed)
                    return;
                completed = true;
                ReleaseCandidateCards();
                ResetActiveSubmission();
                onClosed?.Invoke();
            };
            if (_overlay.PlayCommitted(receipt.Amount, finish))
                return true;
            finish();
            return false;
        }

        public void CloseSilently()
        {
            _generation++;
            _overlay?.CloseSilently();
            ReleaseCandidateCards();
            ResetActiveSubmission();
        }

        private void ReleaseCandidateCards()
        {
            foreach (CardConfig candidate in _candidateCards)
                if (candidate != null) Destroy(candidate);
            _candidateCards.Clear();
        }

        private static RunCardConversionOptionViewData CreateCardChoice(
            CardConfig card,
            RunCardConversionRule rule,
            int amount,
            bool manaRepair)
        {
            string gain = rule.outcome switch
            {
                RunCardConversionOutcome.BaseArmorPerTurn =>
                    $"本次探索每回合基础护甲 +{amount}",
                RunCardConversionOutcome.Might =>
                    $"本次探索锋锐 +{amount}%",
                RunCardConversionOutcome.BaseMana =>
                    $"本次探索每回合基础灵力 +{amount}",
                RunCardConversionOutcome.Area =>
                    $"本次探索裂空 +{amount}%",
                RunCardConversionOutcome.Luck =>
                    $"本次探索天运 +{amount}%",
                _ => "未知归元结果"
            };
            int sealCount = card.seals?.Count ?? 0;
            string warning = sealCount > 0
                ? $"此牌附有 {sealCount} 枚灵印，移除后会随牌一并消耗。"
                : string.Empty;
            Color accent = rule.outcome ==
                    RunCardConversionOutcome.BaseArmorPerTurn
                        ? new Color(0.48f, 0.78f, 0.82f, 1f)
                        : manaRepair ? SpiritEnergyColorPalette.ManaFull
                            : new Color(0.58f, 0.92f, 0.72f, 1f);
            return new RunCardConversionOptionViewData(
                card,
                "从本次探索移除此牌",
                gain,
                warning,
                accent);
        }

        private void ResetActiveSubmission()
        {
            _activeEffectId = null;
            _pendingSelection = default;
            _submissionPending = false;
            _presentationPending = false;
            _manaConversion = false;
        }

        private Canvas EnsureCanvas()
        {
            if (_canvas != null)
                return _canvas;
            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystem = new GameObject(
                    "RunCardConversionEventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(transform, false);
            }
            var canvasObject = new GameObject(
                "RunCardConversionCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32100;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return _canvas;
        }

        private void OnDestroy()
        {
            CloseSilently();
            _handler = null;
        }
    }
}
