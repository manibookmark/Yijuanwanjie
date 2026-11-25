using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Versioned, value-only JSON boundary. The flat request grammar rejects
    /// unknown/duplicate fields instead of allowing JsonUtility to silently ignore them.</summary>
    public static class AgentGameProtocol
    {
        public const string Version = "wanjie-scroll-playtest.v1";
        public const string G1TestCaseId = "g1.post-tutorial.no-upgrades.v1";
        public const string G2VcReferenceTestCaseId =
            "g2.g1-cleared.vc-reference-pilot.v1";
        public const string G3ProductionTestCaseId =
            "g3.g2-cleared.production-start.v1";
        public const string G3HistoricalDaozangTestCaseId =
            "g3.g2-cleared.historical-daozang.v1";
        internal const int MaximumRequestCharacters = 32768;
        private const int MaximumReceiptEnvelopeCharacters = MaximumRequestCharacters * 6 + 512;

        public const string G4ProductionTestCaseId =
            "g4.g3-cleared.production-start.v1";
        public const string CampaignTestCaseId = "campaign.fresh-account.production.v1";
        public const string G4CampaignTestCaseId = "campaign.g3-cleared.g4-production.v1";
        public const string G4HistoricalCampaignTestCaseId = "campaign.g3-cleared.historical-daozang.g4-production.v1";

        internal static bool IsCampaignTestCase(string testCaseId)
            => testCaseId == CampaignTestCaseId || testCaseId == G4CampaignTestCaseId ||
               testCaseId == G4HistoricalCampaignTestCaseId;

        internal static bool IsSupportedTestCase(string testCaseId)
            => testCaseId == G1TestCaseId ||
               testCaseId == G2VcReferenceTestCaseId ||
               testCaseId == G3ProductionTestCaseId ||
               testCaseId == G3HistoricalDaozangTestCaseId ||
               testCaseId == G4ProductionTestCaseId || IsCampaignTestCase(testCaseId);

        internal static AgentGameRequest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumReceiptEnvelopeCharacters)
                throw new FormatException("A bounded JSON object is required.");
            int cursor = 0;
            Skip(json, ref cursor);
            Require(json, ref cursor, '{');
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var numeric = new HashSet<string>(StringComparer.Ordinal);
            Skip(json, ref cursor);
            if (cursor < json.Length && json[cursor] != '}')
            {
                while (true)
                {
                    string key = ReadString(json, ref cursor);
                    Skip(json, ref cursor); Require(json, ref cursor, ':'); Skip(json, ref cursor);
                    bool isNumber = key == "seed" || key == "expectedRevision";
                    string value;
                    if (isNumber)
                    {
                        int start = cursor;
                        if (cursor < json.Length && json[cursor] == '-') cursor++;
                        int digits = cursor;
                        while (cursor < json.Length && json[cursor] >= '0' && json[cursor] <= '9') cursor++;
                        if (cursor == digits || cursor - digits > 1 && json[digits] == '0')
                            throw new FormatException("Expected a JSON integer.");
                        value = json.Substring(start, cursor - start);
                        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                            throw new FormatException("Integer is outside the supported range.");
                        numeric.Add(key);
                    }
                    else value = ReadString(json, ref cursor);
                    if (fields.ContainsKey(key)) throw new FormatException("Duplicate request field: " + key);
                    fields.Add(key, value);
                    Skip(json, ref cursor);
                    if (cursor < json.Length && json[cursor] == ',') { cursor++; Skip(json, ref cursor); continue; }
                    break;
                }
            }
            Require(json, ref cursor, '}'); Skip(json, ref cursor);
            if (cursor != json.Length) throw new FormatException("Unexpected trailing JSON.");
            var allowed = new HashSet<string>(new[] { "protocolVersion", "operation", "requestId", "sessionId",
                "testCaseId", "seed", "actorId", "agentVersion", "promptVersion", "expectedRevision", "actionId", "decisionSummary", "originalRequestJson" });
            foreach (string key in fields.Keys)
                if (!allowed.Contains(key)) throw new FormatException("Unknown request field: " + key);
            string Get(string key) => fields.TryGetValue(key, out string value) ? value : null;
            string version = Get("protocolVersion"), operation = Get("operation");
            if (version != Version) throw new FormatException("Unsupported protocolVersion.");
            if (operation != "receipt" && json.Length > MaximumRequestCharacters)
                throw new FormatException("Mutation or observation request exceeds its bounded size.");
            if (operation != "describe" && operation != "create" && operation != "observe" &&
                operation != "act" && operation != "trace" && operation != "close" && operation != "receipt")
                throw new FormatException("Unsupported operation.");
            var required = new List<string> { "protocolVersion", "operation" };
            if (operation == "create") required.AddRange(new[] { "requestId", "testCaseId", "seed", "actorId", "agentVersion", "promptVersion" });
            else if (operation == "receipt") required.Add("originalRequestJson");
            else if (operation != "describe") required.Add("sessionId");
            if (operation == "act" || operation == "close") required.AddRange(new[] { "requestId", "expectedRevision" });
            if (operation == "act") required.Add("actionId");
            foreach (string key in required)
                if (!fields.ContainsKey(key) || string.IsNullOrWhiteSpace(fields[key]))
                    throw new FormatException("Missing required field: " + key);
            foreach (var pair in fields)
                if (pair.Value.Length > (pair.Key == "originalRequestJson" ? MaximumRequestCharacters : pair.Key == "decisionSummary" ? 2000 : 256))
                    throw new FormatException("Request field is too long: " + pair.Key);
            return new AgentGameRequest {
                protocolVersion = version, operation = operation, requestId = Get("requestId"), sessionId = Get("sessionId"),
                testCaseId = Get("testCaseId"), actorId = Get("actorId"), agentVersion = Get("agentVersion"),
                promptVersion = Get("promptVersion"), actionId = Get("actionId"), decisionSummary = Get("decisionSummary"),
                originalRequestJson = Get("originalRequestJson"),
                seed = numeric.Contains("seed") ? int.Parse(Get("seed"), CultureInfo.InvariantCulture) : 0,
                expectedRevision = numeric.Contains("expectedRevision") ? int.Parse(Get("expectedRevision"), CultureInfo.InvariantCulture) : -1
            };
        }

        private static void Skip(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\r' || s[i] == '\n' || s[i] == '\t')) i++; }
        private static void Require(string s, ref int i, char expected)
        { if (i >= s.Length || s[i++] != expected) throw new FormatException("Malformed request JSON."); }
        private static string ReadString(string s, ref int i)
        {
            Require(s, ref i, '"'); var text = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return text.ToString();
                if (c < 32) throw new FormatException("Unescaped control character.");
                if (c != '\\') { text.Append(c); continue; }
                if (i >= s.Length) break;
                c = s[i++];
                switch (c)
                {
                    case '"': case '\\': case '/': text.Append(c); break;
                    case 'b': text.Append('\b'); break; case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break; case 'r': text.Append('\r'); break; case 't': text.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length || !ushort.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                            throw new FormatException("Invalid Unicode escape.");
                        text.Append((char)code); i += 4; break;
                    default: throw new FormatException("Invalid JSON escape.");
                }
            }
            throw new FormatException("Unterminated JSON string.");
        }
    }

    [Serializable] internal sealed class AgentGameRequest
    {
        public string protocolVersion, operation, requestId, sessionId, testCaseId, actorId, agentVersion, promptVersion, actionId, decisionSummary, originalRequestJson;
        public int seed, expectedRevision;
    }

    [Serializable] public sealed class AgentGameError { public string code, message; }
    [Serializable] public sealed class AgentGameResponse
    {
        public string protocolVersion = AgentGameProtocol.Version;
        public bool success;
        public AgentGameError error;
        public string sessionId;
        public string contentFingerprint;
        public int revision;
        public string phase;
        public AgentGameObservation observation;
        public List<AgentGameAction> legalActions = new List<AgentGameAction>();
        public AgentGameTraceDocument trace;
        public AgentGameDescription description;
    }
    [Serializable] public sealed class AgentGameDescription
    {
        public string hostKind;
        public string buildId;
        public string engineVersion, buildIdentityKind;
        public bool persistenceEnabled;
        public int recoveredMutations;
        public string[] recoveredSessionIds = Array.Empty<string>();
        public string[] operations = { "describe", "create", "observe", "act", "trace", "close", "receipt" };
        public string[] testCaseIds = {
            AgentGameProtocol.G1TestCaseId,
            AgentGameProtocol.G2VcReferenceTestCaseId,
            AgentGameProtocol.G3ProductionTestCaseId,
            AgentGameProtocol.G3HistoricalDaozangTestCaseId,
            AgentGameProtocol.G4ProductionTestCaseId, AgentGameProtocol.CampaignTestCaseId,
            AgentGameProtocol.G4CampaignTestCaseId, AgentGameProtocol.G4HistoricalCampaignTestCaseId };
        public string startingAccount = "Declared per test case; no user save is imported";
        public string[] limitations = {
            "Campaign starts a zero-progress account in the real tutorial, retains one account across formal Hub entries, and exposes Hall, Daozang and permanent registry observations. Single-level test cases remain independent slices.",
            "The G4 campaign slice explicitly starts at Hub with the G3Cleared permanent-unlock blueprint and Zhubajie, zero currency/upgrades/equipped Arcana, and no G4/Shawujing grant. It is not a naturally played G1-G3 account. Enter G4F1 legally; later progress, settlement, Hub and replay use the same account.",
            "The historical G4 campaign slice is separately declared: G3Cleared blueprint, Wukong, historical income7909, formal purchases4000, balance3909 and ShieldBash. Only that budget, purchased upgrades and equipped Arcana are reconstructed from the frozen recipe; no old Run, other historical statistics, G4 clearance or Shawujing unlock is imported.",
            "Headless formal rules; not a live desktop/UI session.",
            "RNG-free damage previews may be estimates; isEstimate=true means damage/estimatedWillKill are advisory and willKill is never guaranteed.",
            "G2 uses the declared G1-cleared account plus authored VC reference loadout; it is not claimed equivalent to a natural-growth first entry.",
            "G3 uses the G2Cleared permanent-unlock blueprint with no upgrades/equipped arcana, Bailongma and the formal RunStart entry. Prior G2 temporary build is not imported; later G3 floors inherit this attempt's actual play.",
            "G4 uses the G3Cleared permanent-unlock blueprint, Zhubajie and the formal RunStart entry. No upgrades, equipped arcana, prior G3 temporary build or Shawujing unlock is imported; later G4 floors inherit actual play.",
            "G3 historical-daozang projects recorded independent G1/G2 earnings into a declared account recipe and buys upgrades through formal Meta rules. It is not a continuous G1-to-G2 save; no locked tier is opened.",
            "G2 F3 I03/C01/C02 and F4 I01 use typed production contracts; no substitute rewards are invented.",
            "Actions and public checkpoints are replayable in-process; production full-run replay remains to be validated.",
            "No automatic policy, timeout turn, map unlocking, or full-clear rule is added." };
        public int maximumLiveSessions = 4, maximumActionsPerSession = AgentGameSession.MaximumActions;
        public int maximumActionsPerCampaign = AgentGameCampaignSession.MaximumActions;
    }
    [Serializable] public sealed class AgentGameAction
    {
        public string actionId, kind, nodeContentId, choiceId, contentId, title, description;
        public int runtimeInstanceId, secondaryRuntimeInstanceId, rewardIndex = -1;
    }
    internal sealed class AgentGameCommand
    {
        internal readonly AgentGameAction View;
        internal readonly Action Execute;
        internal AgentGameCommand(AgentGameAction view, Action execute) { View = view; Execute = execute; }
    }
}
