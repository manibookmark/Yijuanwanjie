using System;
using UnityEngine;

namespace WanjieScroll.Core
{
    public enum CardOfferingAdmissionState
    {
        Unknown = 0,
        Allowed = 1,
        ExcludedByFlag = 2,
        ExcludedFcc = 3
    }

    /// <summary>
    /// Exact source admission evidence, independent of card lifetime, publication
    /// and rewards. Unknown is closed. Private serialized fields survive Unity
    /// definition cloning; consumers receive no setters or mutable collection.
    /// </summary>
    [Serializable]
    public sealed class CardOfferingAdmission
    {
        [SerializeField] private CardOfferingAdmissionState _state;
        [SerializeField] private string _sourceConfigId;
        [SerializeField] private string _sourceClass;
        [SerializeField] private int _rawAllowInOfferingTable = -1;

        public static CardOfferingAdmission Unknown { get; } = new CardOfferingAdmission(
            CardOfferingAdmissionState.Unknown);
        public CardOfferingAdmissionState State => _state;
        public string SourceConfigId => _sourceConfigId ?? string.Empty;
        public string SourceClass => _sourceClass ?? string.Empty;
        public int RawAllowInOfferingTable => _rawAllowInOfferingTable;
        public bool IsAllowed => State == CardOfferingAdmissionState.Allowed &&
            Validate() == null;

        public CardOfferingAdmission(CardOfferingAdmissionState state,
            string sourceConfigId = null, string sourceClass = null,
            int rawAllowInOfferingTable = -1)
        {
            _state = state;
            _sourceConfigId = sourceConfigId ?? string.Empty;
            _sourceClass = sourceClass ?? string.Empty;
            _rawAllowInOfferingTable = rawAllowInOfferingTable;
            string error = Validate();
            if (error != null) throw new ArgumentException(error);
        }

        public string Validate()
        {
            if (!Enum.IsDefined(typeof(CardOfferingAdmissionState), State))
                return "Unknown offering admission state.";
            if (State == CardOfferingAdmissionState.Unknown)
                return SourceConfigId.Length == 0 && SourceClass.Length == 0 &&
                    RawAllowInOfferingTable == -1 ? null :
                    "Unknown admission cannot claim source identity or a source flag.";
            if (string.IsNullOrWhiteSpace(SourceConfigId) ||
                string.IsNullOrWhiteSpace(SourceClass))
                return "Known offering admission requires exact source identity and class.";
            foreach (char ch in SourceConfigId + SourceClass)
                if (char.IsWhiteSpace(ch) || char.IsControl(ch))
                    return "Offering source identity cannot contain whitespace or control characters.";
            if (RawAllowInOfferingTable != 0 && RawAllowInOfferingTable != 1)
                return "Offering source flag must be the captured integer 0 or 1.";
            bool fcc = string.Equals(SourceClass, "FccConfig", StringComparison.Ordinal);
            if (State == CardOfferingAdmissionState.ExcludedFcc)
                return fcc ? null : "ExcludedFcc requires the source FccConfig class.";
            if (fcc) return "FccConfig cannot use flag-only admission.";
            if (!string.Equals(SourceClass, "CardConfig", StringComparison.Ordinal))
                return "Known flag-only admission requires the verified source CardConfig class.";
            if (State == CardOfferingAdmissionState.Allowed && RawAllowInOfferingTable != 1)
                return "Allowed admission requires source flag 1.";
            if (State == CardOfferingAdmissionState.ExcludedByFlag && RawAllowInOfferingTable != 0)
                return "ExcludedByFlag requires source flag 0.";
            return null;
        }
    }
}
