using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// Owns the resident's body hit area and tooltip lifetime. Reads the live
    /// resident contract without deciding card eligibility or changing combat.
    /// The caller owns actor visibility; this view owns only its tooltip.
    /// </summary>
    public sealed class CrawlerResidentHoverView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ICanvasRaycastFilter
    {
        private readonly List<RaycastResult> _raycasts = new List<RaycastResult>();
        private ColorTriggerSystem.ActiveCrawler _crawler;
        private Rect _bodyBounds;
        private RectTransform _actorRect;
        private RawImage _actor;
        private Canvas _tooltipCanvas;
        private CardTooltipView _tooltip;
        private EventSystem _eventSystem;
        private PointerEventData _pointer;
        private bool _available;
        private bool _pointerInside;
        private string _displayedText;

        public void Bind(ColorTriggerSystem.ActiveCrawler crawler,
            Rect normalizedBodyBounds, Canvas tooltipCanvas)
        {
            if (!ReferenceEquals(_crawler, crawler))
                Hide();
            if (_tooltipCanvas != tooltipCanvas)
                ReleaseTooltip();
            _crawler = crawler;
            _tooltipCanvas = tooltipCanvas;
            _actorRect = transform as RectTransform;
            _actor = GetComponent<RawImage>();
            _bodyBounds = Rect.MinMaxRect(
                Mathf.Clamp01(normalizedBodyBounds.xMin),
                Mathf.Clamp01(normalizedBodyBounds.yMin),
                Mathf.Clamp01(normalizedBodyBounds.xMax),
                Mathf.Clamp01(normalizedBodyBounds.yMax));
        }

        public void SetAvailable(bool available)
        {
            _available = available;
            if (!available)
            {
                _pointerInside = false;
                Hide();
            }
        }

        public void Clear()
        {
            _available = false;
            _pointerInside = false;
            _crawler = null;
            ReleaseTooltip();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _pointerInside = true;
            RefreshTooltip(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pointerInside = false;
            Hide();
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!CanHover() || _actorRect == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _actorRect, screenPoint, eventCamera, out Vector2 local))
                return false;

            Rect rect = _actorRect.rect;
            if (rect.width <= 0f || rect.height <= 0f)
                return false;
            Vector2 normalized = new Vector2(
                (local.x - rect.xMin) / rect.width,
                (local.y - rect.yMin) / rect.height);
            return _bodyBounds.Contains(normalized);
        }

        private bool CanHover()
        {
            return isActiveAndEnabled && _available && _crawler?.config != null &&
                   !_crawler.IsExpired && _tooltipCanvas != null &&
                   _tooltipCanvas.isActiveAndEnabled && _actor != null &&
                   _actor.isActiveAndEnabled && _actor.texture != null &&
                   _actor.color.a > 0.01f;
        }

        private void LateUpdate()
        {
            if (!_pointerInside)
                return;
            EventSystem current = EventSystem.current;
            if (current == null)
            {
                Hide();
                return;
            }
            if (_pointer == null || _eventSystem != current)
            {
                _eventSystem = current;
                _pointer = new PointerEventData(current);
            }
            _pointer.Reset();
            _pointer.position = Input.mousePosition;
            RefreshTooltip(_pointer);
        }

        private void RefreshTooltip(PointerEventData pointer)
        {
            if (!CanHover() || !IsTopmostBody(pointer))
            {
                Hide();
                return;
            }

            string text = FormatTooltip(_crawler);
            if (_tooltip == null)
            {
                var root = new GameObject("CrawlerResidentTooltip", typeof(RectTransform));
                root.transform.SetParent(_tooltipCanvas.transform, false);
                _tooltip = root.AddComponent<CardTooltipView>();
            }
            if (_displayedText == text)
                return;
            _tooltip.ShowText(text);
            _displayedText = text;
        }

        private bool IsTopmostBody(PointerEventData pointer)
        {
            if (pointer == null || EventSystem.current == null)
                return false;
            _raycasts.Clear();
            EventSystem.current.RaycastAll(pointer, _raycasts);
            // Match TopmostHoverGate's world-hover blockers, but permit this
            // actor itself. GraphicRaycaster already respects blocksRaycasts;
            // non-interactable modal surfaces must still block the tooltip.
            for (int i = 0; i < _raycasts.Count; i++)
            {
                RaycastResult result = _raycasts[i];
                if (!(result.module is GraphicRaycaster))
                    continue;
                GameObject hit = result.gameObject;
                if (hit == null || !hit.activeInHierarchy)
                    continue;
                Graphic graphic = hit.GetComponent<Graphic>();
                if (graphic == null || !graphic.isActiveAndEnabled || !graphic.raycastTarget)
                    continue;
                return hit.transform == transform || hit.transform.IsChildOf(transform);
            }
            return false;
        }

        private static string FormatTooltip(ColorTriggerSystem.ActiveCrawler crawler)
        {
            CrawlerConfig config = crawler.config;
            var effects = new List<string>();
            if (config.triggerEffects != null)
            {
                foreach (CardEffectSpec effect in config.triggerEffects)
                {
                    if (effect == null)
                        continue;
                    string text = null;
                    if (effect.effectType == EffectType.ModifyStat && effect.amount != 0)
                    {
                        text = effect.statType == StatType.NingShen
                            ? FormatDurationGain(effect.amount)
                            : PowerUpGlyphConfig.FormatDelta(effect.statType, effect.amount);
                        if (effect.statType == StatType.ShouPai)
                            text += $"（{PowerUpGlyphConfig.GetStatDescription(effect.statType)}）";
                    }
                    else if (effect.effectType == EffectType.DrawCards && effect.count > 0)
                    {
                        text = $"立即抽{effect.count}张牌（可受引卷加成）";
                    }
                    if (text == null)
                        continue;
                    if (effect.comboScalable)
                        text += "，受叠势加成";
                    effects.Add(text);
                }
            }
            if ((config.triggerFollowUp == CrawlerTriggerFollowUp.GainCrawlerDuration ||
                 config.triggerFollowUp == CrawlerTriggerFollowUp.IncreaseOtherCrawlersDuration) &&
                config.triggerFollowUpAmount > 0)
            {
                effects.Add("先消耗凝神，再" + FormatDurationGain(config.triggerFollowUpAmount));
            }
            string result = effects.Count > 0 ? string.Join("；", effects) : "暂无已配置的效果";
            return $"<b>{config.crawlerName}</b>\n" +
                   $"触发：打出{ColorName(crawler.EffectiveTriggerColor)}牌或太极牌\n" +
                   $"效果：{result}\n" +
                   $"凝神：剩余{crawler.remainingTriggers}；每次消耗1，耗尽离场";
        }

        private static string FormatDurationGain(int amount)
        {
            return $"给仍在场英灵凝神+{amount}；无人时留给下次召唤";
        }

        private static string ColorName(CardColor color)
        {
            return color switch
            {
                CardColor.Huo => "火",
                CardColor.Tu => "土",
                CardColor.Jin => "金",
                CardColor.Shui => "水",
                CardColor.Mu => "木",
                CardColor.Yin => "阴",
                CardColor.Yang => "阳",
                _ => color.ToString()
            };
        }

        private void Hide()
        {
            _tooltip?.Hide();
            _displayedText = null;
        }

        private void ReleaseTooltip()
        {
            Hide();
            if (_tooltip != null)
                Destroy(_tooltip.gameObject);
            _tooltip = null;
        }

        private void OnDisable()
        {
            _pointerInside = false;
            ReleaseTooltip();
        }

        private void OnDestroy()
        {
            ReleaseTooltip();
        }
    }
}
