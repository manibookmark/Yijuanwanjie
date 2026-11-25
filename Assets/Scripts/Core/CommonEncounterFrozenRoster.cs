using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// An immutable complete logical source matrix. Ordered actors are derived
    /// from its cells by the generator's second-axis-descending traversal;
    /// repeated species remain distinct occurrences. No runtime actor IDs,
    /// mutable EnemyStats, random stream or physical geometry are stored here.
    /// </summary>
    public sealed class CommonEncounterFrozenRoster
    {
        public string TemplateContentId { get; }
        public int FirstAxisLength { get; }
        public int SecondAxisLength { get; }
        public int AlgorithmVersion { get; }
        public string GenerationFingerprint { get; }
        public string RuntimeContentFingerprint { get; }
        public IReadOnlyList<string> Cells { get; }
        public IReadOnlyList<string> EnemyContentIds { get; }
        public IReadOnlyList<CommonEncounterRosterEntry> SourceSlots { get; }

        public CommonEncounterFrozenRoster(string templateContentId, int firstAxisLength,
            int secondAxisLength, IReadOnlyList<string> cells, int algorithmVersion,
            string generationFingerprint, string runtimeContentFingerprint)
        {
            CommonEncounterGenerator.RequireId(templateContentId, nameof(templateContentId));
            CommonEncounterSourceContract.RequireFingerprint(generationFingerprint, nameof(generationFingerprint));
            CommonEncounterSourceContract.RequireFingerprint(runtimeContentFingerprint, nameof(runtimeContentFingerprint));
            if (algorithmVersion != CommonEncounterGenerator.AlgorithmVersion)
                throw new ArgumentException("Unsupported common encounter generator version.", nameof(algorithmVersion));
            if (firstAxisLength < 1 || secondAxisLength < 1 ||
                (long)firstAxisLength * secondAxisLength > int.MaxValue || cells == null ||
                cells.Count != (long)firstAxisLength * secondAxisLength)
                throw new ArgumentException("Frozen matrix dimensions and cells do not match.", nameof(cells));
            var copied = new string[cells.Count];
            for (int i = 0; i < copied.Length; i++)
            {
                if (cells[i] != null) CommonEncounterGenerator.RequireId(cells[i], nameof(cells));
                copied[i] = cells[i];
            }
            var ids = new List<string>();
            var slots = new List<CommonEncounterRosterEntry>();
            for (int second = secondAxisLength - 1; second >= 0; second--)
                for (int first = 0; first < firstAxisLength; first++)
                {
                    string id = copied[first * secondAxisLength + second];
                    if (id == null) continue;
                    ids.Add(id); slots.Add(new CommonEncounterRosterEntry(id, first, second));
                }
            if (ids.Count == 0)
                throw new ArgumentException("A formal frozen encounter cannot have an empty roster.", nameof(cells));
            TemplateContentId = templateContentId;
            FirstAxisLength = firstAxisLength; SecondAxisLength = secondAxisLength;
            AlgorithmVersion = algorithmVersion;
            GenerationFingerprint = generationFingerprint;
            RuntimeContentFingerprint = runtimeContentFingerprint;
            Cells = Array.AsReadOnly(copied);
            EnemyContentIds = Array.AsReadOnly(ids.ToArray());
            SourceSlots = Array.AsReadOnly(slots.ToArray());
        }

        public static CommonEncounterFrozenRoster FromGenerated(CommonEncounterResult result,
            CommonEncounterSourceContract contract)
        {
            if (result == null || result.Status != CommonEncounterGenerationStatus.Generated)
                throw new ArgumentException("Generation did not produce a legal template.", nameof(result));
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            contract.RequireCurrent();
            return new CommonEncounterFrozenRoster(result.TemplateContentId, result.FirstAxisLength,
                result.SecondAxisLength, result.Cells, CommonEncounterGenerator.AlgorithmVersion,
                contract.GenerationFingerprint, contract.RuntimeContentFingerprint);
        }
    }
}
