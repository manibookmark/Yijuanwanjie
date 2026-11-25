using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Immutable consumer contract for the one Crystal lifecycle. No wire DTO or mutable owner state escapes.</summary>
    public sealed class CrystalRunOffer
    {
        public string NodeContentId { get; }
        public CrystalThrowingStage Stage { get; }
        public float EventElapsed { get; }
        public float AutoThrowElapsed { get; }
        public float ExitElapsed { get; }
        public float EventDuration { get; }
        public float ExitDuration { get; }
        public int HeldButtonCount { get; }
        public int RemainingCards { get; }
        public int CompletedSourceFloors { get; }
        public int SourceFloorCount { get; }
        /// <summary>Accepted physical hits; zero-XP hits still count. Never derive speed from XP.</summary>
        public int AcceptedHitCount { get; }
        public long ActualExperience { get; }
        public IReadOnlyList<int> FrozenRosterInstanceIds { get; }
        public IReadOnlyList<int> FlyingCardInstanceIds { get; }
        internal CrystalRunOffer(string nodeId, CrystalThrowingSnapshot snapshot, float exitElapsed,
            int completedSourceFloors, int sourceFloorCount, long actualExperience)
        {
            NodeContentId = nodeId; Stage = snapshot.Stage; EventElapsed = snapshot.EventElapsed;
            AutoThrowElapsed = snapshot.AutoThrowElapsed; ExitElapsed = exitElapsed;
            EventDuration = snapshot.Source.Timing.EventTime; ExitDuration = snapshot.Source.Timing.ExitTime;
            HeldButtonCount = snapshot.HeldButtonCount; AcceptedHitCount = snapshot.HitCardInstanceIds.Count;
            RemainingCards = Stage == CrystalThrowingStage.Returned ? 0 : snapshot.DrawPileTopFirst.Count - snapshot.ThrownCount;
            CompletedSourceFloors = completedSourceFloors; SourceFloorCount = sourceFloorCount; ActualExperience = actualExperience;
            FrozenRosterInstanceIds = Array.AsReadOnly(snapshot.DrawPileTopFirst.ToArray());
            FlyingCardInstanceIds = Array.AsReadOnly(Stage == CrystalThrowingStage.Throwing
                ? snapshot.DrawPileTopFirst.Take(snapshot.ThrownCount).Except(snapshot.HitCardInstanceIds).ToArray() : Array.Empty<int>());
        }
    }
}
