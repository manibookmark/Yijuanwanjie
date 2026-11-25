using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 日志分类。用于分类过滤和检索。
    /// </summary>
    public enum LogCategory
    {
        Combat,
        Card,
        Save,
        Config,
        Scene,
        UI,
        Audio,
        VFX,
        Bootstrap,
        Daozang,
        Dungeon,
        General
    }

    /// <summary>
    /// 分类日志系统。新代码统一使用此入口，旧代码可逐步迁移。
    /// </summary>
    public static class CombatLogger
    {
        /// <summary>Explicit opt-in for high-frequency damage, combo, hover and
        /// VFX diagnostics. Guard before building diagnostic strings or queries;
        /// ordinary warnings/errors and gameplay logging are unaffected.</summary>
        public static bool EnableVerboseDiagnostics { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetVerboseDiagnostics() => EnableVerboseDiagnostics = false;

        private static readonly string[] Prefixes =
        {
            "[Combat]", "[Card]", "[Save]", "[Config]", "[Scene]",
            "[UI]", "[Audio]", "[VFX]", "[Bootstrap]", "[Daozang]",
            "[Dungeon]", "[General]"
        };

        public static void Log(LogCategory cat, string message)
        {
            Debug.Log($"{GetPrefix(cat)} {message}");
        }

        public static void LogWarning(LogCategory cat, string message)
        {
            Debug.LogWarning($"{GetPrefix(cat)} {message}");
        }

        public static void LogError(LogCategory cat, string message)
        {
            Debug.LogError($"{GetPrefix(cat)} {message}");
        }

        public static void Log(LogCategory cat, CombatState state, string message)
        {
            string turn = state != null ? $"[T{state.CurrentTurn}]" : "";
            Debug.Log($"{GetPrefix(cat)}{turn} {message}");
        }

        private static string GetPrefix(LogCategory cat)
        {
            int idx = (int)cat;
            return idx >= 0 && idx < Prefixes.Length ? Prefixes[idx] : "[General]";
        }
    }
}
