using System;
using System.Text.RegularExpressions;

namespace WanjieScroll.Content
{
    /// <summary>Optional author evidence for a native card, independent of Offering permission.
    /// Missing evidence remains unknown. ContentCatalog copies and validates it once.</summary>
    [Serializable]
    public sealed class CardSourceIdentityData
    {
        public string sourceAlias;
        public string sourceFile;
        public int sourcePathId;
        public string sourceConfigId;
        public string sourceClass;
        public string sourceCaptureSha256;
        public string cardTypeSourceFile;
        public int cardTypeFileIndex = -1;
        public int cardTypePathId;
        public string cardTypeAssetId;

        /// <summary>Unity may materialize an absent inline reference as an empty shell.
        /// Both representations have no evidence; any partial positive claim is validated.</summary>
        public static bool IsUnknown(CardSourceIdentityData data) => data == null ||
            (string.IsNullOrEmpty(data.sourceAlias) && string.IsNullOrEmpty(data.sourceFile) &&
             data.sourcePathId == 0 && string.IsNullOrEmpty(data.sourceConfigId) &&
             string.IsNullOrEmpty(data.sourceClass) && string.IsNullOrEmpty(data.sourceCaptureSha256) &&
             string.IsNullOrEmpty(data.cardTypeSourceFile) && data.cardTypeFileIndex >= -1 &&
             data.cardTypeFileIndex <= 0 && data.cardTypePathId == 0 && string.IsNullOrEmpty(data.cardTypeAssetId));
    }

    /// <summary>Immutable source identity and native base type. Owns only imported evidence;
    /// it neither authorizes offerings nor decides payment, installed types or Run state.</summary>
    public sealed class CardSourceIdentity
    {
        public const string GlobalFile = "Vampire Crawlers_Data/globalgamemanagers.assets";
        public const string CardsFile = "Vampire Crawlers_Data/sharedassets0.assets";
        public string SourceAlias { get; }
        public string SourceFile { get; }
        public int SourcePathId { get; }
        public string SourceConfigId { get; }
        public string SourceClass { get; }
        public string SourceCaptureSha256 { get; }
        public string CardTypeSourceFile { get; }
        public int CardTypeFileIndex { get; }
        public int CardTypePathId { get; }
        public string CardTypeAssetId { get; }
        public bool IsFcc => SourceClass == "FccConfig";

        private CardSourceIdentity(CardSourceIdentityData data)
        {
            SourceAlias = data.sourceAlias;
            SourceFile = data.sourceFile;
            SourcePathId = data.sourcePathId;
            SourceConfigId = data.sourceConfigId;
            SourceClass = data.sourceClass;
            SourceCaptureSha256 = data.sourceCaptureSha256;
            CardTypeSourceFile = data.cardTypeSourceFile;
            CardTypeFileIndex = data.cardTypeFileIndex;
            CardTypePathId = data.cardTypePathId;
            CardTypeAssetId = data.cardTypeAssetId;
        }

        public static CardSourceIdentity Compile(CardSourceIdentityData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Require(data.sourceAlias == "vc-local-full-copy", "Unknown native card source alias.");
            Require(KnownFile(data.sourceFile) && KnownFile(data.cardTypeSourceFile),
                "Native card evidence requires captured relative source files.");
            Require(data.sourcePathId > 0 && data.cardTypePathId > 0, "Native object PathIDs must be positive.");
            Require(data.sourceClass == "CardConfig" || data.sourceClass == "FccConfig", "Unknown native card source class.");
            Require(Token(data.sourceConfigId), "Native card AssetId must be an exact nonempty token.");
            Require(Token(data.cardTypeAssetId) && data.cardTypeAssetId.StartsWith("CardType_", StringComparison.Ordinal),
                "Native base type requires its captured AssetId.");
            Require(data.sourceCaptureSha256 != null && Regex.IsMatch(data.sourceCaptureSha256, "\\A[0-9a-f]{64}\\z"),
                "Native card evidence requires a lower-case SHA-256 capture binding.");
            int expectedIndex = data.sourceFile == data.cardTypeSourceFile ? 0 : 1;
            Require(data.cardTypeFileIndex == expectedIndex && (expectedIndex == 0 ||
                (data.sourceFile == CardsFile && data.cardTypeSourceFile == GlobalFile)),
                "Native base-type PPtr does not resolve through the captured file relationship.");
            // Captured base-type objects. The payment rules independently choose their three-object pool.
            int typePath = data.cardTypeAssetId == "CardType_Attack" ? 7550 :
                data.cardTypeAssetId == "CardType_Character" ? 7551 :
                data.cardTypeAssetId == "CardType_Defense" ? 7552 :
                data.cardTypeAssetId == "CardType_Mana" ? 7553 :
                data.cardTypeAssetId == "CardType_Special" ? 7554 :
                data.cardTypeAssetId == "CardType_Void" ? 7555 :
                data.cardTypeAssetId == "CardType_Debuff" ? 18276 : 0;
            Require(typePath > 0 && data.cardTypePathId == typePath &&
                data.cardTypeSourceFile == (typePath == 18276 ? CardsFile : GlobalFile),
                "Native type AssetId contradicts its captured object reference.");
            return new CardSourceIdentity(data);
        }

        private static bool KnownFile(string value) => value == GlobalFile || value == CardsFile;
        private static bool Token(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (char ch in value) if (char.IsWhiteSpace(ch) || char.IsControl(ch)) return false;
            return true;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new ArgumentException(message); }
    }
}
