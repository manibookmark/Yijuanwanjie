using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Immutable damage display for one proposed play. Its total is exactly the
    /// enemy rows' applied armor + health loss. Overkill, future ticks and Erase
    /// defeat markers never manufacture damage. With no targets an attack is 0;
    /// support-only cards have HasDamage=false instead of a misleading damage label.
    /// </summary>
    public sealed class CardImmediateDamageDisplaySummary
    {
        internal CardImmediateDamageDisplaySummary(IReadOnlyList<EnemyDamagePreview> previews,
            bool hasDamage, bool isEstimate)
        {
            var rows = new List<EnemyDamagePreview>(previews ?? Array.Empty<EnemyDamagePreview>());
            DamagePreviews = rows.AsReadOnly();
            HasDamage = hasDamage;
            IsEstimate = isEstimate;
            foreach (EnemyDamagePreview preview in rows)
            {
                TotalDamage = checked(TotalDamage + preview.AppliedDamage);
                IsEstimate |= preview.IsEstimate;
                HasDamage |= preview.RequestedDamage > 0;
            }
        }

        public int TotalDamage { get; }
        public bool IsEstimate { get; }
        public bool HasDamage { get; }
        public IReadOnlyList<EnemyDamagePreview> DamagePreviews { get; }
    }
}
