using WanjieScroll.Content;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Owns the checkpoint artifact boundary: quiescence checks, production
    /// carry-over validation, restore verification, and canonical payload hash.
    /// </summary>
    internal static class BalanceCheckpointArtifactCapture
    {
        internal static void CaptureRequestedEntries(
            BalanceCheckpointRecipe recipe,
            DungeonRunSession dungeon,
            ICollection<BalanceCheckpointArtifact> artifacts,
            ISet<string> capturedFloorIds)
        {
            for (int i = 0; i < recipe.Outputs.Count; i++)
            {
                BalanceCheckpointOutput output = recipe.Outputs[i];
                if (!string.Equals(
                        output.FloorContentId,
                        dungeon.CurrentFloorContentId,
                        StringComparison.Ordinal) ||
                    !capturedFloorIds.Add(output.FloorContentId))
                {
                    continue;
                }

                BalanceEntrySessionFactory.RequireQuiescent(
                    dungeon,
                    output.CheckpointFile);
                BalanceEntrySessionFactory.RequireProductionCarryOver(
                    dungeon,
                    dungeon.CurrentFloor.Profile,
                    output.CheckpointFile);
                string payload = dungeon.Serialize();
                ValidateSerializedEntry(
                    payload,
                    output,
                    dungeon.CurrentFloor.SnapshotId);
                artifacts.Add(new BalanceCheckpointArtifact(
                    output.FloorContentId,
                    dungeon.CurrentFloor.SnapshotId,
                    output.CheckpointFile,
                    payload,
                    ComputeSha256(payload)));
            }
        }

        internal static void RequireEveryOutputReached(
            BalanceCheckpointRecipe recipe,
            IReadOnlyCollection<BalanceCheckpointArtifact> artifacts,
            ISet<string> capturedFloorIds)
        {
            if (artifacts.Count == recipe.Outputs.Count) return;

            var missing = new List<string>();
            for (int i = 0; i < recipe.Outputs.Count; i++)
            {
                if (!capturedFloorIds.Contains(
                        recipe.Outputs[i].FloorContentId))
                {
                    missing.Add(recipe.Outputs[i].FloorContentId);
                }
            }
            throw new BalanceCheckpointException(
                "Recipe did not reach every requested output floor: " +
                string.Join(", ", missing) + ".");
        }

        private static void ValidateSerializedEntry(
            string payload,
            BalanceCheckpointOutput output,
            string expectedSnapshotId)
        {
            if (!DungeonRunSession.TryRestore(
                    payload,
                    LevelRunProfileCatalog.Get,
                    new SeededInstanceIdGenerator(1),
                    "checkpoint.validation",
                    out DungeonRunSession restored,
                    out string restoreError,
                    commonEncounterSources: id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id)))
            {
                throw new BalanceCheckpointException(
                    $"Generated checkpoint {output.CheckpointFile} cannot be " +
                    "restored: " + restoreError);
            }
            if (!string.Equals(
                    restored.CurrentFloorContentId,
                    output.FloorContentId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    restored.CurrentFloor.SnapshotId,
                    expectedSnapshotId,
                    StringComparison.Ordinal))
            {
                throw new BalanceCheckpointException(
                    $"Generated checkpoint {output.CheckpointFile} restored " +
                    "to an unexpected floor or snapshot.");
            }
            BalanceEntrySessionFactory.RequireQuiescent(
                restored,
                output.CheckpointFile);
            BalanceEntrySessionFactory.RequireProductionCarryOver(
                restored,
                restored.CurrentFloor.Profile,
                output.CheckpointFile);
        }

        private static string ComputeSha256(string text)
        {
            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(
                new UTF8Encoding(false).GetBytes(text ?? string.Empty));
            var result = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
            {
                result.Append(digest[i].ToString(
                    "x2",
                    CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }
    }
}
