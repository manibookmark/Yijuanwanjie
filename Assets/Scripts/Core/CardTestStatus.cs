using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WanjieScroll.Core
{
    public static class CardTestStatus
    {
        private const string ResourcePath = "card_test_status";

        [System.Serializable]
        public class CardTestEntry
        {
            public string contentId;
            public string name;
            public bool uiConfirmed;
            public bool functionConfirmed;
        }

        [System.Serializable]
        private class CardTestCollection
        {
            public List<CardTestEntry> cards = new List<CardTestEntry>();
        }

        private static Dictionary<string, CardTestEntry> _byContentId;
        private static bool _loaded;

        public static bool IsUiConfirmed(string contentId)
        {
            EnsureLoaded();
            return contentId != null && _byContentId.TryGetValue(contentId, out var e) && e.uiConfirmed;
        }

        public static bool IsFunctionConfirmed(string contentId)
        {
            EnsureLoaded();
            return contentId != null && _byContentId.TryGetValue(contentId, out var e) && e.functionConfirmed;
        }

        public static bool IsReady(string contentId)
        {
            EnsureLoaded();
            return contentId != null &&
                _byContentId.TryGetValue(contentId, out var entry) &&
                CardReleasePolicy.IsComplete(
                    entry.uiConfirmed,
                    entry.functionConfirmed);
        }

        public static bool IsTracked(string contentId)
        {
            EnsureLoaded();
            return contentId != null && _byContentId.ContainsKey(contentId);
        }

        public static bool ShouldAppearInTest(string contentId)
        {
            EnsureLoaded();
            if (contentId == null ||
                !_byContentId.TryGetValue(contentId, out var entry))
                return CardReleasePolicy.ShouldAppearInTest(false, false, false);
            return CardReleasePolicy.ShouldAppearInTest(
                true,
                entry.uiConfirmed,
                entry.functionConfirmed);
        }

        public static bool CanUseInFormalPlay(string contentId)
        {
            if (!DemoReleasePolicy.AllowsCard(contentId)) return false;
            EnsureLoaded();
            if (contentId == null ||
                !_byContentId.TryGetValue(contentId, out var entry))
                return CardReleasePolicy.CanUseInFormalPlay(false, false, false);
            return CardReleasePolicy.CanUseInFormalPlay(
                true,
                entry.uiConfirmed,
                entry.functionConfirmed);
        }

        public static bool CanUseInTestBattle(string contentId)
        {
            if (!DemoReleasePolicy.AllowsCard(contentId)) return false;
            EnsureLoaded();
            if (contentId == null ||
                !_byContentId.TryGetValue(contentId, out var entry))
                return CardReleasePolicy.CanUseInTestBattle(
                    false,
                    false,
                    false);
            return CardReleasePolicy.CanUseInTestBattle(
                true,
                entry.uiConfirmed,
                entry.functionConfirmed);
        }

        public static bool CanUseEvolutionResult(string contentId)
        {
            if (!DemoReleasePolicy.AllowsCard(contentId)) return false;
            EnsureLoaded();
            return contentId != null &&
                _byContentId.TryGetValue(contentId, out var entry) &&
                CardReleasePolicy.CanUseEvolutionResult(
                    true, entry.uiConfirmed, entry.functionConfirmed);
        }

        public static void SetUiConfirmed(string contentId, string cardName, bool value)
        {
            UpdateConfirmation(contentId, cardName, value, null);
        }

        public static void SetFunctionConfirmed(string contentId, string cardName, bool value)
        {
            UpdateConfirmation(contentId, cardName, null, value);
        }

        public static void SetCompleted(
            string contentId,
            string cardName,
            bool value = true)
        {
            UpdateConfirmation(contentId, cardName, value, value);
        }

        public static List<string> GetPendingFunctionTest()
        {
            EnsureLoaded();
            var result = new List<string>();
            foreach (var entry in _byContentId.Values)
                if (entry.uiConfirmed && !entry.functionConfirmed) result.Add(entry.contentId);
            return result;
        }

        public static List<string> GetIncompleteCards()
        {
            EnsureLoaded();
            var result = new List<string>();
            foreach (var entry in _byContentId.Values)
            {
                if (CardReleasePolicy.ShouldAppearInTest(
                        true,
                        entry.uiConfirmed,
                        entry.functionConfirmed))
                    result.Add(entry.contentId);
            }
            return result;
        }

        public static void RegisterCard(string contentId, string cardName)
        {
            EnsureLoaded();
            if (!_byContentId.ContainsKey(contentId))
                UpdateConfirmation(contentId, cardName, null, null);
        }
        public static List<CardTestEntry> GetAll() { EnsureLoaded(); return new List<CardTestEntry>(_byContentId.Values); }
        public static void Reload() { _loaded = false; EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            var byContentId = new Dictionary<string, CardTestEntry>(System.StringComparer.OrdinalIgnoreCase);
            string json = null;
#if UNITY_EDITOR
            // Human acceptance updates the authoring file without a global
            // import/refresh in Play. Read its latest bytes after a reload;
            // a previously loaded TextAsset can still refer to the old file.
            string path = JsonPath;
            if (File.Exists(path)) json = File.ReadAllText(path);
#endif
            if (json == null)
            {
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (asset != null) json = asset.text;
            }
            if (json != null)
            {
                var coll = JsonUtility.FromJson<CardTestCollection>(json);
                if (coll?.cards != null)
                    foreach (var entry in coll.cards)
                        if (!string.IsNullOrWhiteSpace(entry.contentId))
                            byContentId[entry.contentId] = entry;
            }
            _byContentId = byContentId;
            _loaded = true;
        }

        private static string JsonPath => Path.Combine(Application.dataPath,
            "Resources", "card_test_status.json");

        private static void UpdateConfirmation(string contentId, string cardName,
            bool? ui, bool? function)
        {
            EnsureLoaded();
            _byContentId.TryGetValue(contentId, out CardTestEntry previous);
            var entry = new CardTestEntry
            {
                contentId = contentId,
                name = previous != null ? previous.name : cardName,
                uiConfirmed = ui ?? (previous?.uiConfirmed ?? false),
                functionConfirmed = function ?? (previous?.functionConfirmed ?? false)
            };
            var next = new Dictionary<string, CardTestEntry>(_byContentId,
                System.StringComparer.OrdinalIgnoreCase);
            next[contentId] = entry;
            // A failed disk write must leave the authoritative in-memory
            // acceptance unchanged so the same human action can be retried.
            Save(next);
            _byContentId = next;
        }

        private static void Save(Dictionary<string, CardTestEntry> entries)
        {
            var coll = new CardTestCollection { cards = new List<CardTestEntry>() };
            foreach (var entry in entries.Values) coll.cards.Add(entry);
            // Unity may retain a mapped read view of this TextAsset. A complete
            // sibling file replaces the directory entry instead of truncating
            // the mapped file, and keeps the existing asset meta/GUID intact.
            string path = JsonPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AtomicJsonFile.Write(path, JsonUtility.ToJson(coll, true));
            Debug.Log($"[CardTestStatus] Saved {coll.cards.Count} entries");
        }
    }
}
