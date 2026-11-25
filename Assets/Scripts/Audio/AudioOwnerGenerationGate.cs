using System.Collections.Generic;

namespace WanjieScroll.Audio
{
    /// <summary>Rejects duplicate or late native-audio requests per owner.
    /// Keep tombstones after Stop: an older Play cannot resurrect a cancelled voice.</summary>
    public sealed class AudioOwnerGenerationGate
    {
        private readonly Dictionary<long, long> _latest = new Dictionary<long, long>();

        public bool TryAdmit(long owner, long generation)
        {
            if (owner <= 0 || generation <= 0 ||
                (_latest.TryGetValue(owner, out long previous) && generation <= previous))
                return false;
            _latest[owner] = generation;
            return true;
        }

        public void Clear() => _latest.Clear();
    }
}
