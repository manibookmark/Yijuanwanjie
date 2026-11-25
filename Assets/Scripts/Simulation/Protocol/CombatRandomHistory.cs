using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Protocol
{
    /// <summary>One typed random result from the actual past, never a future seed.</summary>
    [Serializable]
    public sealed class CombatRandomDecision
    {
        public string kind;
        public int minimum, maximum, integer;
        public float lower, upper, value;
    }

    /// <summary>
    /// Owns encounter-wide recording and strict replay. Replaying must consume
    /// the complete past before explicitly switching to an independent future.
    /// Core's attack-projection tape has a different, single-attack lifetime.
    /// </summary>
    internal sealed class CombatRandomHistory : IRandomSource
    {
        private readonly IRandomSource _actual;
        private readonly List<CombatRandomDecision> _past;
        private IRandomSource _future;
        private int _position;
        internal int Count => _past.Count;
        internal int Position => _position;

        internal CombatRandomHistory(IRandomSource actual)
        {
            _actual = actual ?? throw new ArgumentNullException(nameof(actual));
            _past = new List<CombatRandomDecision>();
        }

        private CombatRandomHistory(List<CombatRandomDecision> past)
        {
            _past = new List<CombatRandomDecision>(past);
        }

        internal CombatRandomHistory Replay() => new CombatRandomHistory(_past);

        internal static CombatRandomHistory FromRecorded(IReadOnlyList<CombatRandomDecision> past)
        {
            if (past == null) throw new ArgumentNullException(nameof(past));
            return new CombatRandomHistory(new List<CombatRandomDecision>(past));
        }

        internal void BeginFuture(int seed)
        {
            if (_actual != null || _position != _past.Count)
                throw new InvalidOperationException("Replay did not consume the exact random history.");
            _future = new SeededRandomSource(seed);
        }

        internal IReadOnlyList<CombatRandomDecision> Export()
        {
            var copy = new List<CombatRandomDecision>(_past.Count);
            foreach (CombatRandomDecision d in _past)
                copy.Add(new CombatRandomDecision { kind = d.kind, minimum = d.minimum,
                    maximum = d.maximum, integer = d.integer, lower = d.lower,
                    upper = d.upper, value = d.value });
            return copy.AsReadOnly();
        }

        private CombatRandomDecision Next(string kind, int min = 0, int max = 0,
            float lower = 0, float upper = 0)
        {
            if (_position >= _past.Count)
                throw new InvalidOperationException("Replay requested randomness beyond the recorded past.");
            CombatRandomDecision d = _past[_position++];
            if (d.kind != kind || d.minimum != min || d.maximum != max ||
                d.lower != lower || d.upper != upper)
                throw new InvalidOperationException("Random replay request differs from the recorded past.");
            return d;
        }

        public float Value01()
        {
            if (_future != null) return _future.Value01();
            if (_actual == null) return Next("value").value;
            float value = _actual.Value01();
            _past.Add(new CombatRandomDecision { kind = "value", value = value });
            return value;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (_future != null) return _future.Range(minInclusive, maxExclusive);
            if (_actual == null) return Next("int", minInclusive, maxExclusive).integer;
            int value = _actual.Range(minInclusive, maxExclusive);
            _past.Add(new CombatRandomDecision { kind = "int", minimum = minInclusive,
                maximum = maxExclusive, integer = value });
            return value;
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            if (_future != null) return _future.Range(minInclusive, maxInclusive);
            if (_actual == null) return Next("float", lower: minInclusive, upper: maxInclusive).value;
            float value = _actual.Range(minInclusive, maxInclusive);
            _past.Add(new CombatRandomDecision { kind = "float", lower = minInclusive,
                upper = maxInclusive, value = value });
            return value;
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
