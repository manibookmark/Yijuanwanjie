using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Adapts explicit headless input and declared collision commands. Dungeon owns timers, physical
    /// membership and XP. This does not simulate a camera, trajectory or successful physical collision.</summary>
    internal static class AgentGameCrystals
    {
        internal static bool Handles(LevelNode node) => node?.completionEffects?.Count == 1 &&
            node.completionEffects[0]?.kind == LevelInteractionEffectKind.CrystalThrowing;
        internal static string Availability(DungeonRunSession run, LevelNode node, out string reason)
        {
            reason = null;
            if (run.Crystals.PendingNodeContentId == node.contentId) return null;
            if (run.SideEvents.TryGetCrystalSourceContext(node.contentId, node.completionEffects[0].effectId,
                out _, out reason)) return null;
            return "ConditionUnavailable";
        }
        internal static AgentGameOffer Observe(DungeonRunSession run, string nodeId)
        {
            Require(run.Crystals.TryInspect(nodeId, out var source, out var error), error);
            return new AgentGameOffer { kind = "Crystal", nodeContentId = nodeId, phase = source.Stage.ToString(),
                title = "晶体投牌", message = "投牌与命中是独立动作；命中声明仅适用于当前飞行实例。",
                crystal = new AgentGameCrystalState { eventElapsed = source.EventElapsed, exitElapsed = source.ExitElapsed,
                    eventDuration = source.EventDuration, exitDuration = source.ExitDuration,
                    remainingCards = source.RemainingCards, heldButtonCount = source.HeldButtonCount,
                    completedSourceFloors = source.CompletedSourceFloors, sourceFloorCount = source.SourceFloorCount,
                    actualExperience = source.ActualExperience, flyingCardInstanceIds = source.FlyingCardInstanceIds.ToArray() } };
        }
        internal static List<AgentGameCommand> Commands(DungeonRunSession run, string nodeId,
            DungeonRunInteractionCatalog catalog, Action persist, Action afterCommit)
        {
            Require(run.Crystals.TryInspect(nodeId, out var source, out var error), error);
            var commands = new List<AgentGameCommand>();
            void Add(string choice, string title, Action action, int id = 0)
                => commands.Add(new AgentGameCommand(new AgentGameAction { actionId = "Crystal/" + choice,
                    kind = "Crystal", nodeContentId = nodeId, choiceId = choice, runtimeInstanceId = id, title = title },
                    () => { action(); afterCommit(); }));
            void Frame(float delta, bool pressed = false, bool released = false, bool all = false)
                => Require(run.Crystals.TryAdvanceFrame(nodeId, delta, false, pressed, released, all, persist, out _, out var failure), failure);
            if (source.Stage == CrystalThrowingStage.Preparing)
                Add("start", "开始投牌", () => Require(run.Crystals.TryStart(nodeId, persist, out var failure), failure));
            else if (source.Stage == CrystalThrowingStage.Throwing)
            {
                if (source.HeldButtonCount == 0) Add("press", "按下投牌", () => Frame(0, pressed: true));
                else Add("release", "松开投牌", () => Frame(0, released: true));
                Add("wait", "推进一帧 0.31 秒", () => Frame(.31f));
                Add("throw-all-frame", "全部投牌模式推进一帧 0.31 秒", () => Frame(.31f, all: true));
                foreach (int physicalId in source.FlyingCardInstanceIds)
                    Add("hit/" + physicalId, "声明飞行实例 " + physicalId + " 命中晶体",
                        () => Require(run.Crystals.TryHit(nodeId, physicalId, catalog, persist, out _, out var failure), failure), physicalId);
            }
            else if (source.Stage == CrystalThrowingStage.Ending)
            {
                if (source.ExitElapsed < source.ExitDuration)
                    Add("wait-exit", "推进一帧 0.31 秒离场计时", () => Frame(.31f));
                else Add("finish", "完成清理并返回地图",
                    () => Require(run.Crystals.TryFinish(nodeId, persist, out _, out var failure), failure));
            }
            return commands;
        }
        private static void Require(bool success, string error)
        { if (!success) throw new InvalidOperationException(error); }
    }
    [Serializable] public sealed class AgentGameCrystalState
    {
        public float eventElapsed, exitElapsed, eventDuration, exitDuration;
        public int remainingCards, heldButtonCount, completedSourceFloors, sourceFloorCount;
        public long actualExperience;
        public int[] flyingCardInstanceIds = Array.Empty<int>();
    }
}
