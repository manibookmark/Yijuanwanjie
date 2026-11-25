using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 可注入随机源。统一抽牌、洗牌、暴击、状态概率和奖励随机。
    /// 固定 seed 时结果可复现,不允许业务代码直接调用 UnityEngine.Random。
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>返回 [0, 1) 的浮点随机数。</summary>
        float Value01();

        /// <summary>返回 [minInclusive, maxExclusive) 的整数随机数。</summary>
        int Range(int minInclusive, int maxExclusive);

        /// <summary>返回 [minInclusive, maxInclusive) 的浮点随机数。</summary>
        float Range(float minInclusive, float maxInclusive);

        /// <summary>洗牌。使用 Fisher-Yates 算法。</summary>
        void Shuffle<T>(IList<T> list);
    }

    internal enum RecordedRandomKind
    {
        Value01,
        IntRange,
        FloatRange
    }

    internal readonly struct RecordedRandomDecision
    {
        public readonly RecordedRandomKind Kind;
        public readonly int IntMinimum;
        public readonly int IntMaximum;
        public readonly int IntResult;
        public readonly float FloatMinimum;
        public readonly float FloatMaximum;
        public readonly float FloatResult;

        private RecordedRandomDecision(
            RecordedRandomKind kind,
            int intMinimum,
            int intMaximum,
            int intResult,
            float floatMinimum,
            float floatMaximum,
            float floatResult)
        {
            Kind = kind;
            IntMinimum = intMinimum;
            IntMaximum = intMaximum;
            IntResult = intResult;
            FloatMinimum = floatMinimum;
            FloatMaximum = floatMaximum;
            FloatResult = floatResult;
        }

        public static RecordedRandomDecision Value(float result)
            => new RecordedRandomDecision(
                RecordedRandomKind.Value01,
                0,
                0,
                0,
                0f,
                1f,
                result);

        public static RecordedRandomDecision IntRange(
            int minimum,
            int maximum,
            int result)
            => new RecordedRandomDecision(
                RecordedRandomKind.IntRange,
                minimum,
                maximum,
                result,
                0f,
                0f,
                0f);

        public static RecordedRandomDecision FloatRange(
            float minimum,
            float maximum,
            float result)
            => new RecordedRandomDecision(
                RecordedRandomKind.FloatRange,
                0,
                0,
                0,
                minimum,
                maximum,
                result);
    }

    /// <summary>
    /// 一次攻击计划已经消费的随机决策。Hover 首次生成后冻结，Actual 只读重放；
    /// 它不是可复用随机源，也不允许静默越过计划边界继续取新随机数。
    /// </summary>
    internal sealed class RecordedRandomTape
    {
        private readonly ReadOnlyCollection<RecordedRandomDecision> _decisions;

        public RecordedRandomTape(IList<RecordedRandomDecision> decisions)
        {
            if (decisions == null)
                throw new ArgumentNullException(nameof(decisions));
            _decisions = new List<RecordedRandomDecision>(decisions)
                .AsReadOnly();
        }

        public IRandomSource CreateReplaySource()
            => new ReplayRandomSource(_decisions);
    }

    internal sealed class RecordingRandomSource : IRandomSource
    {
        private readonly IRandomSource _source;
        private readonly List<RecordedRandomDecision> _decisions =
            new List<RecordedRandomDecision>();

        public RecordingRandomSource(IRandomSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public float Value01()
        {
            float result = _source.Value01();
            _decisions.Add(RecordedRandomDecision.Value(result));
            return result;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            int result = _source.Range(minInclusive, maxExclusive);
            _decisions.Add(RecordedRandomDecision.IntRange(
                minInclusive,
                maxExclusive,
                result));
            return result;
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            float result = _source.Range(minInclusive, maxInclusive);
            _decisions.Add(RecordedRandomDecision.FloatRange(
                minInclusive,
                maxInclusive,
                result));
            return result;
        }

        public void Shuffle<T>(IList<T> list)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public RecordedRandomTape Freeze()
            => new RecordedRandomTape(_decisions);
    }

    internal sealed class ReplayRandomSource : IRandomSource
    {
        private readonly IReadOnlyList<RecordedRandomDecision> _decisions;
        private int _cursor;

        public ReplayRandomSource(
            IReadOnlyList<RecordedRandomDecision> decisions)
        {
            _decisions = decisions ??
                throw new ArgumentNullException(nameof(decisions));
        }

        public float Value01()
        {
            RecordedRandomDecision decision = Next(
                RecordedRandomKind.Value01);
            return decision.FloatResult;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            RecordedRandomDecision decision = Next(
                RecordedRandomKind.IntRange);
            if (decision.IntMinimum != minInclusive ||
                decision.IntMaximum != maxExclusive)
            {
                throw new InvalidOperationException(
                    "攻击随机计划的整数区间与 Hover 时不一致。");
            }
            return decision.IntResult;
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            RecordedRandomDecision decision = Next(
                RecordedRandomKind.FloatRange);
            if (!Mathf.Approximately(decision.FloatMinimum, minInclusive) ||
                !Mathf.Approximately(decision.FloatMaximum, maxInclusive))
            {
                throw new InvalidOperationException(
                    "攻击随机计划的浮点区间与 Hover 时不一致。");
            }
            return decision.FloatResult;
        }

        public void Shuffle<T>(IList<T> list)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private RecordedRandomDecision Next(RecordedRandomKind expectedKind)
        {
            if (_cursor >= _decisions.Count)
            {
                throw new InvalidOperationException(
                    "攻击实际结算消费了 Hover 计划之外的随机决策。");
            }
            RecordedRandomDecision decision = _decisions[_cursor++];
            if (decision.Kind != expectedKind)
            {
                throw new InvalidOperationException(
                    "攻击实际结算的随机决策顺序与 Hover 计划不一致。");
            }
            return decision;
        }
    }

    /// <summary>
    /// 默认随机源,使用 UnityEngine.Random。
    /// 生产环境使用;测试注入确定性实现。
    /// </summary>
    public sealed class DefaultRandomSource : IRandomSource
    {
        public static readonly IRandomSource Instance = new DefaultRandomSource();

        public float Value01() => UnityEngine.Random.value;

        public int Range(int minInclusive, int maxExclusive) =>
            UnityEngine.Random.Range(minInclusive, maxExclusive);

        public float Range(float minInclusive, float maxInclusive) =>
            UnityEngine.Random.Range(minInclusive, maxInclusive);

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    /// <summary>
    /// 确定性随机源,使用线性同余生成器(LCG)。
    /// 固定 seed 时结果可复现,用于 EditMode 测试。
    /// </summary>
    public sealed class SeededRandomSource : IRandomSource
    {
        private static readonly ulong Multiplier = 6364136223846793005UL;
        private static readonly ulong Increment = 1442695040888963407UL;
        private static readonly ulong Modulus = 1UL << 63;

        private ulong _state;

        /// <summary>Read-only deterministic cursor for trusted replay verification;
        /// inspecting it never advances the generator or predicts player-facing results.</summary>
        public ulong DeterministicState => _state;

        public SeededRandomSource(int seed)
        {
            _state = (ulong)seed + Increment;
            NextUInt();
        }

        public float Value01()
        {
            return (float)(NextUInt() / (double)Modulus);
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            return minInclusive + (int)(NextUInt() % (ulong)(maxExclusive - minInclusive));
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            return minInclusive + Value01() * (maxInclusive - minInclusive);
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private ulong NextUInt()
        {
            _state = unchecked(_state * Multiplier + Increment);
            return _state % Modulus;
        }
    }
}
