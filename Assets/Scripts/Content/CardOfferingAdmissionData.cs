using System;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>Optional author evidence. Absence is Unknown, never an inferred Allow.</summary>
    [Serializable]
    public sealed class CardOfferingAdmissionData
    {
        public string state = "Unknown";
        public string sourceConfigId;
        public string sourceClass;
        public int rawAllowInOfferingTable = -1;

        public CardOfferingAdmission Compile()
        {
            if (!Enum.TryParse(state, false, out CardOfferingAdmissionState value) ||
                !Enum.IsDefined(typeof(CardOfferingAdmissionState), value) ||
                !string.Equals(state, value.ToString(), StringComparison.Ordinal))
                throw new ArgumentException("Offering admission requires an explicit named state.");
            return new CardOfferingAdmission(value, sourceConfigId, sourceClass,
                rawAllowInOfferingTable);
        }

        public static CardOfferingAdmission CompileOrUnknown(CardOfferingAdmissionData data)
            => data == null ? CardOfferingAdmission.Unknown : data.Compile();
    }
}
