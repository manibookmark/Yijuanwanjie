using System;
using System.Collections.Generic;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns the versioned balance routes for G1F1. These stable node IDs are
    /// simulation inputs only; production map availability is still validated by
    /// LevelRunSession for every requested encounter.
    /// </summary>
    [Obsolete(
        "Compatibility catalog for legacy tests. " +
        "tools/simulation/suites/g1f1.json is the authoritative balance input.")]
    public static class ShuangchalingF1SimulationRoutes
    {
        public const int Revision = 1;
        public const string FloorResourcePath = "Levels/shuangchaling-g1f1";

        public const string E01 = "node.xiyou.shuangchaling.f01.e01";
        public const string E02 = "node.xiyou.shuangchaling.f01.e02";
        public const string E03 = "node.xiyou.shuangchaling.f01.e03";
        public const string E04 = "node.xiyou.shuangchaling.f01.e04";
        public const string E05 = "node.xiyou.shuangchaling.f01.e05";
        public const string E06 = "node.xiyou.shuangchaling.f01.e06";
        public const string E07 = "node.xiyou.shuangchaling.f01.e07";
        public const string B01 = "node.xiyou.shuangchaling.f01.b01";

        private static readonly Dictionary<string, StageSimulationRoute> Routes =
            BuildRoutes();

        public static StageSimulationRoute Get(string routeId)
        {
            if (routeId != null && Routes.TryGetValue(
                    routeId, out StageSimulationRoute route))
                return route;
            throw new ArgumentException($"Unknown G1F1 simulation route: {routeId}");
        }

        public static IReadOnlyCollection<StageSimulationRoute> GetAll() =>
            Routes.Values;

        private static Dictionary<string, StageSimulationRoute> BuildRoutes()
        {
            var result = new Dictionary<string, StageSimulationRoute>(
                StringComparer.Ordinal)
            {
                ["R_B"] = Route("R_B", B01),
                ["R_U_B"] = Route("R_U_B", E01, E03, B01),
                ["R_M_B"] = Route("R_M_B", E02, E05, B01),
                ["R_L_B"] = Route("R_L_B", E04, E06, E07, B01),
                ["R_UM_B"] = Route("R_UM_B", E01, E03, E02, E05, B01),
                ["R_UL_B"] = Route("R_UL_B", E01, E03, E04, E06, E07, B01),
                ["R_ML_B"] = Route("R_ML_B", E02, E05, E04, E06, E07, B01),
                ["R_ALL_01"] = Route("R_ALL_01", E01, E03, E02, E05, E04, E06, E07, B01),
                ["R_ALL_02"] = Route("R_ALL_02", E01, E03, E04, E06, E07, E02, E05, B01),
                ["R_ALL_03"] = Route("R_ALL_03", E02, E05, E01, E03, E04, E06, E07, B01),
                ["R_ALL_04"] = Route("R_ALL_04", E02, E05, E04, E06, E07, E01, E03, B01),
                ["R_ALL_05"] = Route("R_ALL_05", E04, E06, E07, E01, E03, E02, E05, B01),
                ["R_ALL_06"] = Route("R_ALL_06", E04, E06, E07, E02, E05, E01, E03, B01),
                ["R_B_ALL"] = Route("R_B_ALL", B01, E01, E03, E02, E05, E04, E06, E07)
            };
            return result;
        }

        private static StageSimulationRoute Route(
            string routeId,
            params string[] encounters)
            => new StageSimulationRoute(routeId, Revision, encounters);
    }
}
