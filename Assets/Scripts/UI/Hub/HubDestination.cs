using System;

namespace WanjieScroll.UI.Hub
{
    /// <summary>Stable level-selection descriptor supplied by Bootstrap.</summary>
    [Serializable]
    public struct HubDestination
    {
        public HubDestination(
            string id,
            string title,
            string subtitle,
            bool available)
        {
            Id = id;
            Title = title;
            Subtitle = subtitle;
            Available = available;
        }

        public string Id;
        public string Title;
        public string Subtitle;
        public bool Available;
    }
}
