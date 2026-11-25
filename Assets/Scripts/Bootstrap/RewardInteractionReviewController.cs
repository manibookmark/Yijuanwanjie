#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 编辑器审核页中交互节点完整表现的唯一状态所有者。每个类型从同一个入口
    /// 开始，并按生产顺序串联获得窗、后续选择面板和成果演出；审核过程只构造
    /// 展示快照和模拟成功回执，不修改 Run、内容资产或存档。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class RewardInteractionReviewController : MonoBehaviour
    {
        private const string DoubleDamageSealId =
            "seal.xiyou.double-damage.common.v1";
        private const int CashOutAmount = 200;

        private static readonly LevelInteractionEffectKind[] ScenarioKinds =
        {
            LevelInteractionEffectKind.AddBaseMana,
            LevelInteractionEffectKind.GrantJade,
            LevelInteractionEffectKind.GrantRelic,
            LevelInteractionEffectKind.GrantSeal,
            LevelInteractionEffectKind.HealCurrentHealth,
            LevelInteractionEffectKind.ConvertCard
        };

        private static readonly string[] ScenarioLabels =
        {
            "灵力节点",
            "灵玉节点",
            "遗珍节点",
            "灵印节点",
            "疗伤节点",
            "归元碑"
        };

        private static readonly string[] ScenarioRoutes =
        {
            "获得灵力 → 结束",
            "获得灵玉 → 结束",
            "获得遗珍 → 结束",
            "灵印去向 → 钤印选卡 / 化玉 → 钤印成果",
            "恢复生命 → 结束",
            "归元选择 → 归元成果"
        };

        private static readonly string[] RarityOverrideLabels =
        {
            "按配置",
            "凡品",
            "良品",
            "上品",
            "珍品",
            "仙品"
        };

        private Canvas _canvas;
        private Func<IReadOnlyList<CardConfig>> _cardProvider;
        private Action _onStateChanged;
        private RewardRevealReviewController _reveal;
        private SealAssignmentOverlay _sealAssignment;
        private RunCardConversionOverlay _conversion;
        private Coroutine _flowRoutine;
        private Coroutine _raritySequenceRoutine;
        private int _selectedIndex;
        private int _rarityOverrideIndex;
        private int _generation;
        private string _stageLabel = "未开始";

        public string RarityOverrideLabel =>
            RarityOverrideLabels[_rarityOverrideIndex];
        public string StageLabel => _stageLabel;
        public string SelectedRouteLabel => ScenarioRoutes[_selectedIndex];

        public void Initialize(
            Canvas canvas,
            Func<IReadOnlyList<CardConfig>> cardProvider,
            Action onStateChanged)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _cardProvider = cardProvider ??
                            throw new ArgumentNullException(nameof(cardProvider));
            _onStateChanged = onStateChanged;
            _reveal ??= gameObject.GetComponent<
                RewardRevealReviewController>() ??
                gameObject.AddComponent<RewardRevealReviewController>();
            _reveal.Initialize(_canvas);
            _sealAssignment ??=
                gameObject.GetComponent<SealAssignmentOverlay>() ??
                gameObject.AddComponent<SealAssignmentOverlay>();
            _conversion ??=
                gameObject.GetComponent<RunCardConversionOverlay>() ??
                gameObject.AddComponent<RunCardConversionOverlay>();
        }

        public static List<string> GetScenarioLabels()
            => new List<string>(ScenarioLabels);

        public void SelectScenario(int index)
        {
            StopRaritySequence();
            _selectedIndex = Mathf.Clamp(index, 0, ScenarioKinds.Length - 1);
            NotifyStateChanged();
            Replay();
        }

        public void CycleRarityOverride(int delta)
        {
            StopRaritySequence();
            _rarityOverrideIndex = Wrap(
                _rarityOverrideIndex + delta,
                RarityOverrideLabels.Length);
            NotifyStateChanged();
            Replay();
        }

        public void Replay()
        {
            StopRaritySequence();
            int generation = ++_generation;
            StopActiveViews();
            SetStage("准备播放");
            _flowRoutine = StartCoroutine(BeginNextFrame(generation));
        }

        public void PlayRaritySequence()
        {
            StopRaritySequence();
            int generation = ++_generation;
            StopActiveViews();
            LevelInteractionEffectKind kind = ScenarioKinds[_selectedIndex];
            if (kind == LevelInteractionEffectKind.ConvertCard)
            {
                SetStage("归元碑没有获得品阶，请选择奖励节点");
                return;
            }

            _raritySequenceRoutine = StartCoroutine(
                PlayRaritySequenceRoutine(generation, kind));
        }

        private IEnumerator PlayRaritySequenceRoutine(
            int generation,
            LevelInteractionEffectKind kind)
        {
            const float PreviewDuration = 2.05f;
            for (int rarityIndex = 1;
                 rarityIndex < RarityOverrideLabels.Length;
                 rarityIndex++)
            {
                if (generation != _generation)
                    yield break;

                StopActiveViews();
                _rarityOverrideIndex = rarityIndex;
                RewardFeedbackItem item = BuildRarityPreview(kind);
                if (item == null)
                {
                    _raritySequenceRoutine = null;
                    SetStage("当前节点没有可用的品阶样本");
                    yield break;
                }

                ApplyRarityOverride(item);
                SetStage(
                    $"五档连播 · {RarityOverrideLabels[rarityIndex]} " +
                    $"({rarityIndex}/{RarityOverrideLabels.Length - 1})");
                _reveal.Show(item, null);
                yield return new WaitForSecondsRealtime(PreviewDuration);
            }

            if (generation != _generation)
                yield break;
            _reveal.Close();
            _raritySequenceRoutine = null;
            SetStage("五档连播完成 · 当前仙品");
        }

        private RewardFeedbackItem BuildRarityPreview(
            LevelInteractionEffectKind kind)
        {
            if (kind != LevelInteractionEffectKind.GrantSeal)
                return BuildDirectAcquisition(kind);

            if (!LingyinCatalog.TryGet(
                    DoubleDamageSealId, out LingyinConfig seal) ||
                seal == null)
            {
                return null;
            }
            return RewardFeedbackItem.FromSeal(seal);
        }

        private IEnumerator BeginNextFrame(int generation)
        {
            yield return null;
            _flowRoutine = null;
            if (generation != _generation || _canvas == null)
                yield break;

            switch (ScenarioKinds[_selectedIndex])
            {
                case LevelInteractionEffectKind.GrantSeal:
                    BeginSealFlow(generation);
                    break;
                case LevelInteractionEffectKind.ConvertCard:
                    BeginConversionFlow(generation);
                    break;
                default:
                    BeginDirectAcquisition(generation);
                    break;
            }
        }

        private void BeginDirectAcquisition(int generation)
        {
            RewardFeedbackItem item = BuildDirectAcquisition(
                ScenarioKinds[_selectedIndex]);
            if (item == null)
            {
                Complete(generation, "缺少获得样本");
                return;
            }
            ApplyRarityOverride(item);
            SetStage("获得反馈");
            _reveal.Show(item, () => Complete(generation, "播放完成"));
        }

        private void BeginSealFlow(int generation)
        {
            if (!LingyinCatalog.TryGet(
                    DoubleDamageSealId, out LingyinConfig seal) ||
                seal == null)
            {
                Debug.LogWarning(
                    "[RewardReview] 找不到正式双伤印内容：" +
                    DoubleDamageSealId);
                Complete(generation, "缺少双伤印内容");
                return;
            }

            ShowSealDisposition(generation, seal);
        }

        private void ShowSealDisposition(
            int generation,
            LingyinConfig seal)
        {
            if (generation != _generation)
                return;
            List<CardConfig> candidates = BuildSealCandidates(seal);
            RewardFeedbackItem item = RewardFeedbackItem.FromSeal(seal);
            ApplyRarityOverride(item);
            SetStage(candidates.Count > 0
                ? "灵印去向"
                : "灵印去向 · 无合法宿主");
            Action pickUp = candidates.Count > 0
                ? (Action)(() => QueueContinuation(
                    generation,
                    () => ShowSealAssignment(
                        generation,
                        seal,
                        candidates)))
                : null;
            _reveal.ShowDecision(
                item,
                pickUp,
                actions: new[]
                {
                    new RewardUtilityAction(
                        RewardUtilityActionKind.CashOut,
                        $"化为灵玉 · +{CashOutAmount}",
                        () => Complete(generation, "模拟化玉完成"))
                });
        }

        private void ShowSealAssignment(
            int generation,
            LingyinConfig seal,
            IReadOnlyList<CardConfig> candidates)
        {
            if (generation != _generation)
                return;
            if (candidates == null || candidates.Count == 0)
            {
                Debug.LogWarning(
                    "[RewardReview] 当前审核卡牌中没有双伤印合法宿主。");
                ShowSealDisposition(generation, seal);
                return;
            }

            var viewModel = new SealAssignmentViewModel(seal, candidates);
            SetStage("钤印选卡");
            _sealAssignment.Show(
                _canvas,
                viewModel,
                _ => QueueSimulatedSealCommit(generation),
                () => QueueContinuation(
                    generation,
                    () => ShowSealDisposition(generation, seal)));
            GlobalButtonAudioRuntime.BindHierarchyNow(_sealAssignment.Root);
        }

        private List<CardConfig> BuildSealCandidates(LingyinConfig seal)
        {
            var result = new List<CardConfig>();
            IReadOnlyList<CardConfig> cards = _cardProvider();
            CardSealSpec spec = seal.ToCardSealSpec();
            for (int i = 0; cards != null && i < cards.Count; i++)
            {
                CardConfig card = cards[i];
                if (CardSealRules.CanAssign(card, spec))
                    result.Add(card);
            }
            return result;
        }

        private void QueueSimulatedSealCommit(int generation)
        {
            if (generation != _generation)
                return;
            if (_flowRoutine != null)
                StopCoroutine(_flowRoutine);
            _flowRoutine = StartCoroutine(SimulateSealCommit(generation));
        }

        private IEnumerator SimulateSealCommit(int generation)
        {
            SetStage("模拟权威提交");
            yield return new WaitForSecondsRealtime(0.35f);
            _flowRoutine = null;
            if (generation != _generation)
                yield break;
            SetStage("钤印成果");
            _sealAssignment.PlayCommitted(
                onPlaybackStarted: null,
                onClosed: () => Complete(generation, "播放完成"));
        }

        private void BeginConversionFlow(int generation)
        {
            IReadOnlyList<RunCardConversionOptionViewData> options =
                BuildConversionOptions();
            SetStage("归元选择");
            _conversion.Show(
                _canvas,
                options,
                CashOutAmount,
                new Vector2(Screen.width * 0.84f, Screen.height * 0.52f),
                intent => QueueSimulatedConversionCommit(generation, intent),
                () => Complete(generation, "已取消归元"));
            GlobalButtonAudioRuntime.BindHierarchyNow(_conversion.Root);
        }

        private IReadOnlyList<RunCardConversionOptionViewData>
            BuildConversionOptions()
        {
            var options = new List<RunCardConversionOptionViewData>();
            IReadOnlyList<CardConfig> cards = _cardProvider();
            for (int i = 0; cards != null && i < cards.Count; i++)
            {
                CardConfig card = cards[i];
                if (card == null)
                    continue;
                options.Add(new RunCardConversionOptionViewData(
                    card,
                    string.Empty,
                    i % 2 == 0
                        ? "本次探索基础护甲 +1"
                        : "本次探索锋锐 +5%",
                    card.seals != null && card.seals.Count > 0
                        ? "此牌附有灵印，归元后将随牌离开。"
                        : string.Empty,
                    i % 2 == 0
                        ? new Color(0.48f, 0.78f, 0.82f, 1f)
                        : new Color(0.58f, 0.92f, 0.72f, 1f)));
            }
            return options;
        }

        private void QueueSimulatedConversionCommit(
            int generation,
            RunCardConversionOverlayIntent intent)
        {
            if (generation != _generation)
                return;
            if (_flowRoutine != null)
                StopCoroutine(_flowRoutine);
            _flowRoutine = StartCoroutine(
                SimulateConversionCommit(generation, intent));
        }

        private IEnumerator SimulateConversionCommit(
            int generation,
            RunCardConversionOverlayIntent intent)
        {
            SetStage("模拟权威提交");
            yield return new WaitForSecondsRealtime(0.35f);
            _flowRoutine = null;
            if (generation != _generation)
                yield break;
            SetStage("归元成果");
            int amount = intent.Kind ==
                RunCardConversionOverlayIntentKind.CashOut
                    ? CashOutAmount
                    : 1;
            if (!_conversion.PlayCommitted(
                    amount,
                    () => Complete(generation, "播放完成")))
            {
                Complete(generation, "无法播放归元成果");
            }
        }

        private RewardFeedbackItem BuildDirectAcquisition(
            LevelInteractionEffectKind kind)
        {
            LevelInteractionReceipt receipt = kind switch
            {
                LevelInteractionEffectKind.AddBaseMana => CreateReceipt(
                    kind, "review.interaction.base-mana",
                    "reward.reveal.base-mana.v1", 1, 3, 4, "基础灵力",
                    "本次探索的基础灵力永久提高 1。",
                    RewardRarity.Uncommon),
                LevelInteractionEffectKind.GrantJade => CreateReceipt(
                    kind, "review.interaction.jade",
                    RewardArtworkCatalog.JadePresentationId, 6, 0, 6,
                    "灵玉", "凝结灵气已经化作灵玉收入行囊。",
                    RewardRarity.Uncommon),
                LevelInteractionEffectKind.GrantRelic => CreateReceipt(
                    kind, "review.interaction.relic.yuyin",
                    RewardArtworkCatalog.YuyinPresentationId, 1, 0, 1,
                    "玉印", "取得玉印，解锁即时钤印。",
                    RewardRarity.Rare, "relic.xiyou.yuyin.v1"),
                LevelInteractionEffectKind.HealCurrentHealth => CreateReceipt(
                    kind, "review.interaction.heal", string.Empty,
                    10, 20, 30, "休整疗伤", "恢复当前生命 10 点。",
                    RewardRarity.Uncommon),
                _ => default
            };
            if (string.IsNullOrWhiteSpace(receipt.EffectId))
                return null;
            return RewardFeedbackItem.FromInteractionReceipt(receipt);
        }

        private static LevelInteractionReceipt CreateReceipt(
            LevelInteractionEffectKind kind,
            string effectId,
            string presentationId,
            int amount,
            int valueBefore,
            int valueAfter,
            string summary,
            string detail,
            RewardRarity rarity,
            string targetContentId = "")
        {
            return new LevelInteractionReceipt(
                effectId,
                kind,
                targetContentId,
                presentationId,
                amount,
                valueBefore,
                valueAfter,
                newlyApplied: true,
                summaryText: summary,
                detailText: detail,
                rarity: rarity);
        }

        private void ApplyRarityOverride(RewardFeedbackItem item)
        {
            if (item != null && _rarityOverrideIndex > 0)
                item.Rarity = (RewardRarity)(_rarityOverrideIndex - 1);
        }

        private void QueueContinuation(int generation, Action continuation)
        {
            if (generation != _generation)
                return;
            if (_flowRoutine != null)
                StopCoroutine(_flowRoutine);
            _flowRoutine = StartCoroutine(
                ContinueNextFrame(generation, continuation));
        }

        private IEnumerator ContinueNextFrame(
            int generation,
            Action continuation)
        {
            yield return null;
            _flowRoutine = null;
            if (generation == _generation)
                continuation?.Invoke();
        }

        private void Complete(int generation, string stage)
        {
            if (generation != _generation)
                return;
            SetStage(stage);
        }

        private void SetStage(string stage)
        {
            _stageLabel = stage ?? string.Empty;
            NotifyStateChanged();
        }

        private void NotifyStateChanged()
        {
            _onStateChanged?.Invoke();
        }

        private void StopActiveViews()
        {
            if (_flowRoutine != null)
            {
                StopCoroutine(_flowRoutine);
                _flowRoutine = null;
            }
            _reveal?.Close();
            _sealAssignment?.CloseSilently();
            _conversion?.CloseSilently();
        }

        private void StopRaritySequence()
        {
            if (_raritySequenceRoutine == null)
                return;
            StopCoroutine(_raritySequenceRoutine);
            _raritySequenceRoutine = null;
        }

        public void Close()
        {
            StopRaritySequence();
            _generation++;
            StopActiveViews();
            SetStage("已关闭");
        }

        private static int Wrap(int value, int count)
        {
            int wrapped = value % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        private void OnDestroy()
        {
            _cardProvider = null;
            _onStateChanged = null;
            Close();
        }
    }
}
#endif
