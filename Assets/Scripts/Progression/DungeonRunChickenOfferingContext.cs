using System;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>Resolves live, explicit source admission and event eligibility for one Run.</summary>
    // Historical name retained for callers; one source admission resolver serves
    // both typed offerings. Neither interface owns a counter or permanent gate.
    public sealed class DungeonRunChickenOfferingContext : IChickenOfferingContext, IManaOfferingContext
    {
        private readonly DungeonRunSession _run;
        private readonly MetaProgression _account;
        private readonly Func<ContentCatalog> _catalog;

        public DungeonRunChickenOfferingContext(DungeonRunSession run, MetaProgression account,
            Func<ContentCatalog> catalog = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _catalog = catalog ?? (() => ContentCatalog.Current);
        }

        public ChickenOfferingEligibility QueryEligibility()
            => new ChickenOfferingEligibility(ChickenOfferingContracts.SourceEventId,
                _account.EventUnlocks.IsChickenShopEligible());

        ManaOfferingEligibility IManaOfferingContext.QueryEligibility()
            => _account.EventUnlocks.QueryManaOfferingEligibility();

        public bool ExperimentalMedicineEnabled =>
            RunCombatRules.Create(_account, _run.CurrentFloor, _run).experimentalMedicineEnabled;

        public CardOfferingAdmission ResolveAdmission(string cardContentId)
        {
            if (string.IsNullOrWhiteSpace(cardContentId))
                throw new ArgumentException("Offering requires a stable card content ID.");
            ContentCatalog catalog = _catalog() ?? throw new InvalidOperationException("Offering content catalog is missing.");
            CardData found = null;
            foreach (VolumeContent volume in catalog.Volumes)
                foreach (CardData card in volume.cards)
                    if (string.Equals(card.contentId, cardContentId, StringComparison.Ordinal))
                    {
                        if (found != null) throw new InvalidOperationException("Offering card ID is ambiguous.");
                        found = card;
                    }
            if (found == null) throw new InvalidOperationException("Offering card content is missing: " + cardContentId);
            return CardOfferingAdmissionData.CompileOrUnknown(found.offeringAdmission);
        }
    }
}
