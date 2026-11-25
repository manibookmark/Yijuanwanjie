using System;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// Frozen presentation policy supplied by composition. It describes labels
    /// and available buttons only; Dungeon validates each submitted disposition.
    /// </summary>
    public sealed class RunCardConversionPresentation
    {
        public static RunCardConversionPresentation Guiyuan { get; } =
            new RunCardConversionPresentation("选择一张牌归元", "归元", true, true);

        public string Title { get; }
        public string ActionLabel { get; }
        public bool AllowsCashOut { get; }
        public bool AllowsLeave { get; }
        public bool IsManaConversion { get; }

        public RunCardConversionPresentation(
            string title, string actionLabel, bool allowsCashOut, bool allowsLeave,
            bool isManaConversion = false)
        {
            Title = title ?? string.Empty;
            ActionLabel = actionLabel ?? string.Empty;
            AllowsCashOut = allowsCashOut;
            AllowsLeave = allowsLeave;
            IsManaConversion = isManaConversion;
        }
    }

    public enum RunCardConversionOverlayIntentKind
    {
        ConvertCard,
        CashOut
    }

    public readonly struct RunCardConversionOverlayIntent
    {
        public RunCardConversionOverlayIntentKind Kind { get; }
        public int CardRuntimeInstanceId { get; }

        private RunCardConversionOverlayIntent(
            RunCardConversionOverlayIntentKind kind,
            int cardRuntimeInstanceId)
        {
            Kind = kind;
            CardRuntimeInstanceId = Mathf.Max(0, cardRuntimeInstanceId);
        }

        public static RunCardConversionOverlayIntent ConvertCard(
            int runtimeInstanceId)
        {
            return new RunCardConversionOverlayIntent(
                RunCardConversionOverlayIntentKind.ConvertCard,
                runtimeInstanceId);
        }

        public static RunCardConversionOverlayIntent CashOut()
        {
            return new RunCardConversionOverlayIntent(
                RunCardConversionOverlayIntentKind.CashOut,
                0);
        }
    }

    /// <summary>
    /// UI-only snapshot of one legal Guiyuan target. Dungeon remains the sole
    /// owner of rule validation and card removal; this type only carries the
    /// already-resolved text and artwork needed by the overlay.
    /// </summary>
    public sealed class RunCardConversionOptionViewData
    {
        public CardConfig Card { get; }
        public int RuntimeInstanceId => Card?.runtimeInstanceId ?? 0;
        public string LossText { get; }
        public string GainText { get; }
        public string WarningText { get; }
        public Color Accent { get; }

        public RunCardConversionOptionViewData(
            CardConfig card,
            string lossText,
            string gainText,
            string warningText,
            Color accent)
        {
            Card = card ?? throw new ArgumentNullException(nameof(card));
            LossText = lossText ?? string.Empty;
            GainText = gainText ?? string.Empty;
            WarningText = warningText ?? string.Empty;
            Accent = accent;
        }
    }
}
