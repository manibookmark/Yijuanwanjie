using System;
using System.Globalization;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns only the versioned wire meaning of the four existing Run counters.
    /// Core owns their mutable state and effective combat projection. Modern
    /// saves preserve earned values exactly; legacy saves retain their original
    /// normalization rather than inventing previously truncated source awards.
    /// </summary>
    internal static class DungeonRunProgressionSaveCodec
    {
        private static readonly string[] Fields =
            { "runLuckBonus", "runGreedBonus", "runJadeEarned", "runGrowthBonus" };

        internal static RunProgressionSnapshot Read(DungeonRunSaveData data, string payload)
        {
            if (data == null) throw Invalid("Run data is missing.");
            if (data.schemaVersion < 36)
                return new RunProgressionSnapshot(Math.Max(0, data.runLuckBonus),
                    Math.Max(0, data.runGreedBonus), Math.Max(0, data.runJadeEarned),
                    Math.Max(0, Math.Min(RunProgressionState.DefaultGrowthCap, data.runGrowthBonus)));

            if (string.IsNullOrWhiteSpace(payload)) throw Invalid("Run wire is missing.");
            // The existing closed field scanner rejects duplicate and escaped
            // root names before DTO defaults can hide them. This codec owns the
            // four scalars, not a second copy of the entire Run envelope.
            var names = DungeonRunCrystalEvents.ReadFieldNames(payload);
            foreach (string field in Fields)
            {
                if (!names.Contains(field)) throw Invalid("Required counter is missing: " + field);
                foreach (string actual in names)
                    if (actual.Equals(field, StringComparison.OrdinalIgnoreCase) && actual != field)
                        throw Invalid("Counter alias is not canonical: " + actual);
            }
            int luck = ReadCounter(payload, Fields[0]);
            int greed = ReadCounter(payload, Fields[1]);
            int jade = ReadCounter(payload, Fields[2]);
            int growth = ReadCounter(payload, Fields[3]);
            if (luck != data.runLuckBonus || greed != data.runGreedBonus ||
                jade != data.runJadeEarned || growth != data.runGrowthBonus)
                throw Invalid("Parsed counters differ from their explicit wire.");
            return new RunProgressionSnapshot(luck, greed, jade, growth);
        }

        private static int ReadCounter(string raw, string field)
        {
            if (!JsonObjectShape.TryGetField(raw, field, out string value) ||
                !int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number) ||
                number < 0 || value != number.ToString(CultureInfo.InvariantCulture))
                throw Invalid("Counter must be an explicit canonical nonnegative int: " + field);
            return number;
        }

        private static InvalidOperationException Invalid(string message)
            => new InvalidOperationException("Run progression wire: " + message);
    }
}
