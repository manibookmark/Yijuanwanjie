namespace WanjieScroll.Effects
{
    /// <summary>
    /// Clock-only envelope for one stage boss's contact highlight. Repeated hits
    /// may raise an existing pulse to the critical cap, but never move its start,
    /// extend its tail or bypass the quiet interval. Owns no Unity object or clock.
    /// </summary>
    internal sealed class StageBossHitPulse
    {
        internal const double Duration = 0.16;
        internal const double MinimumStartInterval = 0.22;
        internal const double RiseDuration = 0.02;
        internal const float NormalPeak = 0.32f;
        internal const float CriticalPeak = 0.44f;

        private double _startedAt = double.NegativeInfinity;
        private float _peak;
        private bool _hasPulse;

        internal int Generation { get; private set; }

        /// <summary>
        /// True means a new pulse started and its contact anchor may be replaced.
        /// False includes a merged critical hit: the existing anchor/time remain.
        /// </summary>
        internal bool RegisterHit(double now, bool critical)
        {
            if (!IsValidTime(now) || now < _startedAt)
                return false;

            if (_hasPulse && now < _startedAt + Duration)
            {
                if (critical)
                    _peak = CriticalPeak;
                return false;
            }

            if (now < _startedAt + MinimumStartInterval)
                return false;

            _startedAt = now;
            _peak = critical ? CriticalPeak : NormalPeak;
            _hasPulse = true;
            unchecked { Generation++; }
            return true;
        }

        internal float Sample(double now)
        {
            if (!_hasPulse || !IsValidTime(now))
                return 0f;

            double age = now - _startedAt;
            if (age < 0.0 || now >= _startedAt + Duration)
                return 0f;
            if (age < RiseDuration)
                return _peak * (float)(age / RiseDuration);

            double remaining = 1.0 -
                (age - RiseDuration) / (Duration - RiseDuration);
            return _peak * (float)(remaining * remaining);
        }

        internal void Cancel()
        {
            _hasPulse = false;
            _peak = 0f;
            _startedAt = double.NegativeInfinity;
            unchecked { Generation++; }
        }

        private static bool IsValidTime(double now)
            => !double.IsNaN(now) && !double.IsInfinity(now) && now >= 0.0;
    }
}
