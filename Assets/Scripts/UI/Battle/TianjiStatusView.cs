using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Meta;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// Projects all recognized active rules; owns only text, subscriptions and visual feedback.
    /// Refreshing the projection never equips or changes a Run rule. The cycle ring reads only growth.
    /// </summary>
    public sealed class TianjiStatusView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerClickHandler
    {
        [SerializeField] private Text _name, _detail, _feedback;
        [SerializeField] private GameObject _tooltip;
        [SerializeField] private Image _seal;
        private CombatController _combat;
        private readonly List<ArcanaConfig> _configs = new List<ArcanaConfig>();
        private float _pulseUntil;
        private float _cyclePulseUntil;
        private Color _sealColor;
        private RunCardPlayGrowth _growth;
        private TianjiCycleRing _cycleRing;
        private bool _presentationInitialized;
        private float _minimumDetailHeight, _tooltipPadding;

        public static TianjiStatusView Create(Transform parent, CombatRuleSet rules,
            CombatController combat = null, bool map = false, RunCardPlayGrowth growth = null)
        {
            var prefab = Resources.Load<GameObject>(map ? "UI/Battle/TianjiMapStatus" : "UI/Battle/TianjiStatus");
            if (prefab == null)
            {
                Debug.LogError("Missing Tianji status prefab.");
                return null;
            }
            GameObject instance = Instantiate(prefab, parent, false);
            var view = instance.GetComponentInChildren<TianjiStatusView>(true);
            view.Bind(rules, combat, growth);
            return view;
        }

        private void Bind(CombatRuleSet rules, CombatController combat, RunCardPlayGrowth growth)
        {
            Unsubscribe();
            _combat = combat;
            RefreshRules(rules, growth);
            if (_combat != null)
            {
                _combat.OnRuleTriggered += OnRuleTriggered;
                _combat.OnCardPlayGrowthCommitted += OnCardPlayGrowthCommitted;
            }
        }

        /// <summary>Reprojects current rules after a map reward without rebinding combat subscriptions.</summary>
        public void RefreshRules(CombatRuleSet rules, RunCardPlayGrowth growth = null)
        {
            if (!_presentationInitialized)
            {
                _sealColor = _seal.color;
                _minimumDetailHeight = _detail.rectTransform.rect.height;
                _tooltipPadding = ((RectTransform)_tooltip.transform).rect.height - _minimumDetailHeight;
                _name.resizeTextMaxSize = _name.fontSize;
                _name.resizeTextMinSize = 14;
                _name.resizeTextForBestFit = true;
                _presentationInitialized = true;
            }
            _growth = growth ?? _combat?.RunProgression.CardPlayGrowth;
            _configs.Clear();
            ArcanaConfig[] configs = Resources.LoadAll<ArcanaConfig>("Content/Arcanas");
            Array.Sort(configs, (a, b) => string.Compare(a?.contentId, b?.contentId, StringComparison.Ordinal));
            var effects = new HashSet<ArcanaEffect>();
            foreach (ArcanaConfig config in configs)
            {
                if (config == null || string.IsNullOrWhiteSpace(config.contentId) ||
                    !IsEnabled(config.effect, rules) || !effects.Add(config.effect)) continue;
                _configs.Add(config);
            }
            _tooltip.SetActive(false);
            _pulseUntil = _cyclePulseUntil = 0;
            _seal.color = _sealColor;
            _feedback.text = string.Empty;
            SyncCycleRing();
            RefreshProjection();
            gameObject.SetActive(_configs.Count > 0);
        }

        private bool IsEnabled(ArcanaEffect effect, CombatRuleSet rules)
        {
            switch (effect)
            {
                case ArcanaEffect.ExperimentalMedicine: return rules?.experimentalMedicineEnabled == true;
                case ArcanaEffect.ShieldBash: return rules?.shieldBashEnabled == true;
                case ArcanaEffect.YourShieldMyLiege: return rules?.preservePlayerArmorBetweenTurns == true;
                case ArcanaEffect.BaishuZhoutian: return _growth?.Enabled == true;
                default: return false;
            }
        }

        private ArcanaConfig FindConfig(ArcanaEffect effect)
        {
            foreach (ArcanaConfig config in _configs)
                if (config.effect == effect) return config;
            return null;
        }

        private void SyncCycleRing()
        {
            if (_cycleRing == null) _cycleRing = _seal.GetComponentInChildren<TianjiCycleRing>(true);
            if (_cycleRing == null && _growth?.Enabled == true)
            {
                var ring = new GameObject("CycleProgress", typeof(RectTransform), typeof(TianjiCycleRing));
                ring.transform.SetParent(_seal.transform, false);
                var rect = (RectTransform)ring.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(-5, -5); rect.offsetMax = new Vector2(5, 5);
                _cycleRing = ring.GetComponent<TianjiCycleRing>();
                _cycleRing.raycastTarget = false;
            }
            if (_cycleRing != null) _cycleRing.gameObject.SetActive(_growth?.Enabled == true);
        }

        private void RefreshProjection()
        {
            if (!_presentationInitialized) return;
            var title = new StringBuilder();
            var detail = new StringBuilder("已生效天机");
            foreach (ArcanaConfig config in _configs)
            {
                if (title.Length > 0) title.Append(" · ");
                title.Append(config.arcanaName);
                detail.Append("\n\n").Append(config.arcanaName).Append('\n').Append(config.description);
                if (config.effect == ArcanaEffect.BaishuZhoutian && _growth?.Enabled == true)
                    detail.Append($"\n周天进度 {_growth.ProgressToNextCycle}/{RunCardPlayGrowth.RealPlaysPerCycle}" +
                        $" · 累计出牌 {_growth.TotalRealPlays}\n本天机基础灵力 +{_growth.BaseManaBonus}");
            }
            _name.text = title.ToString();
            _detail.text = _configs.Count > 0
                ? detail.Append("\n\n本局规则 · 行程结束前不可更换").ToString() : string.Empty;
            float height = Mathf.Max(_minimumDetailHeight, _detail.preferredHeight);
            _detail.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            ((RectTransform)_tooltip.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                height + _tooltipPadding);
            RefreshCycleProgress();
            if (_pulseUntil <= Time.unscaledTime) _feedback.text = GrowthFeedback();
        }

        private void RefreshCycleProgress()
        {
            if (_cycleRing == null || _growth?.Enabled != true) return;
            _cycleRing.ShowProgress(_cyclePulseUntil > Time.unscaledTime ? 1 :
                (float)_growth.ProgressToNextCycle / RunCardPlayGrowth.RealPlaysPerCycle);
        }

        private string GrowthFeedback() => _growth?.Enabled == true
            ? $"{_growth.ProgressToNextCycle}/{RunCardPlayGrowth.RealPlaysPerCycle} · 灵力 +{_growth.BaseManaBonus}"
            : string.Empty;

        private void OnCardPlayGrowthCommitted(RunCardPlayGrowthReceipt receipt)
        {
            if (_growth?.Enabled != true || !gameObject.activeInHierarchy) return;
            if (!receipt.IsRestore && receipt.BaseManaDelta > 0)
            {
                ArcanaConfig config = FindConfig(ArcanaEffect.BaishuZhoutian);
                if (config != null) _feedback.text = $"{config.arcanaName} · 基础灵力 +{receipt.BaseManaDelta}";
                _pulseUntil = _cyclePulseUntil = Time.unscaledTime + 1.2f;
            }
            RefreshProjection();
        }

        private void OnRuleTriggered(CombatRuleTriggeredEvent result)
        {
            if (!gameObject.activeInHierarchy) return;
            ArcanaEffect effect;
            string contribution;
            switch (result.Kind)
            {
                case CombatRuleTriggerKind.ExperimentalMedicine:
                    effect = ArcanaEffect.ExperimentalMedicine; contribution = " · 治疗 +"; break;
                case CombatRuleTriggerKind.ShieldBash:
                    effect = ArcanaEffect.ShieldBash; contribution = " · 伤害 "; break;
                default: return;
            }
            ArcanaConfig config = FindConfig(effect);
            if (config == null) return;
            _feedback.text = config.arcanaName + contribution + result.Amount;
            _pulseUntil = Time.unscaledTime + 1.2f;
        }

        private void Update()
        {
            float remaining = _pulseUntil - Time.unscaledTime;
            _seal.color = Color.Lerp(_sealColor, new Color(.97f, .68f, .34f, 1), Mathf.Clamp01(remaining / .7f));
            if (_cyclePulseUntil > 0 && _cyclePulseUntil <= Time.unscaledTime)
            {
                _cyclePulseUntil = 0;
                RefreshCycleProgress();
            }
            if (remaining <= 0 && _pulseUntil > 0)
            {
                _pulseUntil = 0;
                RefreshProjection();
            }
            if (remaining <= 0) _feedback.text = GrowthFeedback();
        }

        public void OnPointerEnter(PointerEventData data)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(data, gameObject)) return;
            _tooltip.SetActive(true);
        }
        public void OnPointerExit(PointerEventData data) => _tooltip.SetActive(false);
        public void OnPointerClick(PointerEventData data) => _tooltip.SetActive(!_tooltip.activeSelf);
        private void OnEnable() => RefreshProjection();
        private void OnDisable()
        {
            if (_tooltip != null) _tooltip.SetActive(false);
            _pulseUntil = _cyclePulseUntil = 0;
        }
        private void OnDestroy() => Unsubscribe();
        private void Unsubscribe()
        {
            if (_combat != null)
            {
                _combat.OnRuleTriggered -= OnRuleTriggered;
                _combat.OnCardPlayGrowthCommitted -= OnCardPlayGrowthCommitted;
            }
        }
    }
}
