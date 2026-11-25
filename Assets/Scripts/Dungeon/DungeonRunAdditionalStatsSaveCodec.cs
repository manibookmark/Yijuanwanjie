using System;
using System.Globalization;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns the closed additional-stat/revival Run wire and its legacy admission.
    /// Core alone owns mutable modifiers, caps and revival consumption. This
    /// boundary validates raw presence before Unity can synthesize zero DTOs,
    /// then validates the earned/consumed proof against the immutable entry.
    /// It never reads live accounts or reconstructs consumed lives from combat.
    /// </summary>
    internal static class DungeonRunAdditionalStatsSaveCodec
    {
        private static readonly string[] OwnerFields =
            { "hasAdditionalStatState", "additionalStats", "hasRevivalReserve", "revivalReserve" };

        internal readonly struct State
        {
            internal RunAdditionalStatSnapshot Additional { get; }
            internal RunRevivalSnapshot Revival { get; }
            internal State(RunAdditionalStatSnapshot additional, RunRevivalSnapshot revival)
            { Additional = additional; Revival = revival; }
        }

        internal static State Read(DungeonRunSaveData data, string payload,
            DungeonRunPlayerBaselineSaveData baseline)
        {
            if (data == null) throw Invalid("Run data is missing.");
            var names = DungeonRunCrystalEvents.ReadFieldNames(payload);
            foreach (string field in OwnerFields)
                foreach (string actual in names)
                    if (actual.Equals(field, StringComparison.OrdinalIgnoreCase) && actual != field)
                        throw Invalid("Owner field alias is not canonical: " + actual);

            if (data.schemaVersion < 35)
            {
                foreach (string field in OwnerFields)
                    if (names.Contains(field)) throw Invalid("Legacy Run contains future owner field: " + field);
                var migrated = new RunRevivalReserve();
                // Prior Run versions had no revival consumption. Only a genuine
                // recorded entry can admit its percent; absent entry stays unknown.
                if (baseline != null) migrated.InitializeOnce(Math.Max(0, baseline.hunHun));
                return new State(default, migrated.Capture());
            }

            RequireFlag(payload, "hasAdditionalStatState", data.hasAdditionalStatState);
            RequireFlag(payload, "hasRevivalReserve", data.hasRevivalReserve);
            string stats = RequireBody(payload, "additionalStats");
            string revival = RequireBody(payload, "revivalReserve");
            RequireFields(stats, "amountBonus", "magnetBonus", "curseBonus");
            RequireFields(revival, "initialized", "capacityPercent", "initialPercent",
                "awardedPercent", "spentLives", "remainingPercent");

            var additional = new RunAdditionalStatSnapshot(ReadInt(stats, "amountBonus"),
                ReadInt(stats, "magnetBonus"), ReadInt(stats, "curseBonus"));
            var reserve = new RunRevivalSnapshot(ReadBool(revival, "initialized"),
                ReadInt(revival, "capacityPercent"), ReadInt(revival, "initialPercent"),
                ReadInt(revival, "awardedPercent"), ReadInt(revival, "spentLives"),
                ReadInt(revival, "remainingPercent"));
            if (reserve.Initialized != (baseline != null))
                throw Invalid("Revival admission differs from the recorded player entry.");
            if (baseline != null && reserve.InitialPercent != Math.Min(reserve.CapacityPercent, Math.Max(0, baseline.hunHun)))
                throw Invalid("Revival initial percent differs from the immutable entry.");
            // Validate values against Unity's DTO as well as the raw wire: wrong
            // numeric types must not be accepted as a synthesized default zero.
            if (data.additionalStats == null || data.revivalReserve == null ||
                !data.additionalStats.Matches(additional) || !data.revivalReserve.Matches(reserve))
                throw Invalid("Parsed owner values differ from their explicit wire.");
            return new State(additional, reserve);
        }

        private static void RequireFlag(string raw, string name, bool parsed)
        {
            if (!parsed || !JsonObjectShape.TryGetField(raw, name, out string value) || value != "true")
                throw Invalid("Explicit true owner presence is required: " + name);
        }
        private static string RequireBody(string raw, string name)
        {
            if (!JsonObjectShape.TryGetField(raw, name, out string value) || value == "null")
                throw Invalid("Explicit owner body is required: " + name);
            return value;
        }
        private static void RequireFields(string raw, params string[] required)
        {
            var names = DungeonRunCrystalEvents.ReadFieldNames(raw);
            if (names.Count != required.Length) throw Invalid("Owner body is not a closed object.");
            foreach (string name in required)
                if (!names.Contains(name)) throw Invalid("Owner field is missing or unknown: " + name);
        }
        private static int ReadInt(string raw, string field)
        {
            if (!JsonObjectShape.TryGetField(raw, field, out string value) ||
                !int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number) ||
                value != number.ToString(CultureInfo.InvariantCulture))
                throw Invalid("Owner integer is not canonical: " + field);
            return number;
        }
        private static bool ReadBool(string raw, string field)
        {
            if (!JsonObjectShape.TryGetField(raw, field, out string value) || (value != "true" && value != "false"))
                throw Invalid("Owner boolean is not explicit: " + field);
            return value == "true";
        }
        private static InvalidOperationException Invalid(string text)
            => new InvalidOperationException("Run additional-stat wire: " + text);
    }

    [Serializable]
    internal sealed class DungeonRunAdditionalStatsSaveData
    {
        public int amountBonus;
        public int magnetBonus;
        public int curseBonus;
        internal static DungeonRunAdditionalStatsSaveData Capture(RunAdditionalStatModifiers owner)
        {
            var state = owner.Capture();
            return new DungeonRunAdditionalStatsSaveData
            { amountBonus = state.AmountBonus, magnetBonus = state.MagnetBonus, curseBonus = state.CurseBonus };
        }
        internal bool Matches(RunAdditionalStatSnapshot snapshot)
            => amountBonus == snapshot.AmountBonus && magnetBonus == snapshot.MagnetBonus && curseBonus == snapshot.CurseBonus;
    }

    [Serializable]
    internal sealed class DungeonRunRevivalReserveSaveData
    {
        public bool initialized;
        public int capacityPercent;
        public int initialPercent;
        public int awardedPercent;
        public int spentLives;
        public int remainingPercent;
        internal static DungeonRunRevivalReserveSaveData Capture(RunRevivalReserve owner)
        {
            var state = owner.Capture();
            return new DungeonRunRevivalReserveSaveData
            { initialized = state.Initialized, capacityPercent = state.CapacityPercent, initialPercent = state.InitialPercent,
                awardedPercent = state.AwardedPercent, spentLives = state.SpentLives, remainingPercent = state.RemainingPercent };
        }
        internal bool Matches(RunRevivalSnapshot state)
            => initialized == state.Initialized && capacityPercent == state.CapacityPercent && initialPercent == state.InitialPercent &&
                awardedPercent == state.AwardedPercent && spentLives == state.SpentLives && remainingPercent == state.RemainingPercent;
    }
}
