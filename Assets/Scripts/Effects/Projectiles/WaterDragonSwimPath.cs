using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Immutable swimming geometry through one object's recorded Direct contacts.
    /// It owns neither targets, damage nor clocks. The flight owner alone applies
    /// the sampled pose to the dragon Transform.
    /// </summary>
    internal sealed class WaterDragonSwimPath
    {
        private const float FrontDepth = 1f / 6f;
        private const float CycleSeconds = 0.82f;
        private const float MouthToTailLength = 1.49f;
        // Transparent spell overlays the five actors of its current row;
        // nearer rows can still occlude a dragon visiting a deeper target.
        private const int RowOverlayOffset = 6;
        private readonly BoardVisualFrame _frame;
        private readonly int _instance;
        private readonly float _birth, _end, _width, _scale, _lengthFraction;
        private readonly Vector3 _rowAxis, _depthSpan;
        private readonly Node[] _nodes;
        private readonly bool _hasContacts;

        internal readonly struct Pose
        {
            internal readonly Vector3 Position;
            internal readonly Vector3 Scale;
            internal readonly int SortingOrder;
            internal Pose(Vector3 position, Vector3 scale, int sortingOrder)
            { Position = position; Scale = scale; SortingOrder = sortingOrder; }
        }

        private readonly struct Node
        {
            internal readonly float Time, Depth, Facing;
            internal readonly Vector3 Position;
            internal Node(float time, Vector3 position, float depth, float facing)
            { Time = time; Position = position; Depth = depth; Facing = facing; }
        }

        internal WaterDragonSwimPath(ProjectileTrace trace, BoardVisualFrame frame,
            float sizeMultiplier, Func<ProjectileTracePoint, Vector3?> findRecordedAnchor)
        {
            _frame = frame;
            _instance = trace.AttackInstanceIndex;
            _birth = trace.BirthTime;
            _end = trace.EndTime;
            _width = Vector3.Distance(frame.Project(0f, FrontDepth), frame.Project(1f, FrontDepth));
            _lengthFraction = 0.12f * Mathf.Clamp(sizeMultiplier, 0.1f, 3f);
            _scale = _width * _lengthFraction / MouthToTailLength;
            _rowAxis = (frame.Project(1f, FrontDepth) - frame.Project(0f, FrontDepth)).normalized;
            _depthSpan = frame.Project(0.5f, 1f) - frame.Project(0.5f, 0f);
            var nodes = new List<Node>();
            float entryX = 0.32f + (_instance % 5) * 0.09f;
            float facing = (_instance & 1) == 0 ? 1f : -1f;
            nodes.Add(new Node(_birth, frame.Project(entryX, FrontDepth), FrontDepth, facing));
            foreach (ProjectileTracePoint point in trace.Points)
            {
                if (point.Kind != ProjectileTracePointKind.Hit &&
                    point.Kind != ProjectileTracePointKind.InitialFeedback) continue;
                _hasContacts = true;
                Vector3 position = findRecordedAnchor(point) ?? frame.Project(point.X, point.Y);
                // Same-time contacts cannot have distinct travel intervals. The
                // last recorded contact sets the endpoint; all receipts remain.
                if (nodes[nodes.Count - 1].Time == point.Time) nodes.RemoveAt(nodes.Count - 1);
                Node previous = nodes.Count > 0 ? nodes[nodes.Count - 1] :
                    new Node(_birth, position, point.Y, facing);
                float dx = Vector3.Dot(position - previous.Position, _rowAxis);
                facing = Mathf.Abs(dx) > 0.001f ? Mathf.Sign(dx) : previous.Facing;
                nodes.Add(new Node(point.Time, position, point.Y, facing));
            }
            if (nodes.Count > 1 && nodes[0].Time == _birth)
            {
                Node entry = nodes[0];
                nodes[0] = new Node(entry.Time, entry.Position, entry.Depth, nodes[1].Facing);
            }
            _nodes = nodes.ToArray();
        }

        internal Pose Sample(float absoluteTime)
        {
            float time = Mathf.Clamp(absoluteTime, _birth, _end);
            if (!_hasContacts) return SampleFreeSwim(time - _birth);
            int fromIndex = FindLastNodeAtOrBefore(time);
            Node from = _nodes[fromIndex];
            if (fromIndex == _nodes.Length - 1) return SampleAfterContact(from, time);
            Node to = _nodes[fromIndex + 1];
            float progress = Mathf.Clamp01((time - from.Time) / (to.Time - from.Time));
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            float envelope = Mathf.Sin(Mathf.PI * progress);
            Vector3 position = Vector3.Lerp(from.Position, to.Position, eased);
            float depth = Mathf.Lerp(from.Depth, to.Depth, eased);
            float facing = Mathf.Lerp(from.Facing, to.Facing, eased);
            if ((to.Position - from.Position).sqrMagnitude < 0.01f)
            {
                // Repeated hits on one boss retain a small returning swim.
                position += _rowAxis * (from.Facing * _width * 0.06f * Mathf.Sin(progress * 2f * Mathf.PI));
                facing = from.Facing * Mathf.Cos(progress * 2f * Mathf.PI);
            }
            else
            {
                position += _depthSpan * (0.015f * envelope * Mathf.Sin(progress * 2f * Mathf.PI));
            }
            return MakePose(position, depth, facing);
        }

        internal Pose AtContact(float time, Vector3 position, float depth)
        {
            Pose pose = Sample(time);
            return new Pose(position, pose.Scale,
                _frame.SortingOrderAtDepth(depth) + RowOverlayOffset);
        }

        private int FindLastNodeAtOrBefore(float time)
        {
            int low = 0, high = _nodes.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                if (_nodes[middle].Time <= time) low = middle + 1;
                else high = middle - 1;
            }
            return Mathf.Max(0, high);
        }

        private Pose SampleAfterContact(Node last, float time)
        {
            float phase = (time - last.Time) * (2f * Mathf.PI / CycleSeconds);
            Vector3 position = last.Position + _rowAxis *
                (last.Facing * _width * 0.06f * Mathf.Sin(phase));
            return MakePose(position, last.Depth, last.Facing * Mathf.Cos(phase));
        }

        private Pose SampleFreeSwim(float age)
        {
            float phase = age * (2f * Mathf.PI / CycleSeconds) + (_instance % 7) * 0.64f;
            float depth = FrontDepth + ((_instance % 5) - 2) * 0.008f + Mathf.Sin(phase * 2f) * 0.012f;
            float swing = Mathf.Min(0.30f, 0.46f - _lengthFraction * 0.5f);
            float centerX = 0.5f - swing * Mathf.Cos(phase);
            float sine = Mathf.Sin(phase);
            float facing = sine / Mathf.Sqrt(sine * sine + 0.015f);
            Vector3 mouth = _frame.Project(centerX, depth) +
                _rowAxis * (facing * _scale * MouthToTailLength * 0.5f);
            return MakePose(mouth, depth, facing);
        }

        private Pose MakePose(Vector3 position, float depth, float facing)
            => new Pose(position, new Vector3(facing * _scale, _scale, _scale),
                _frame.SortingOrderAtDepth(depth) + RowOverlayOffset);
    }
}
