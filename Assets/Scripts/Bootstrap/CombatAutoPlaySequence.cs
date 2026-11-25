using System.Collections;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Adapts a single Core play-all session to the existing hand presentation.
    /// Owns the running request, readiness wait and stale-coroutine cancellation;
    /// policy/payment remain in Core and actual play remains in the manual path.
    /// </summary>
    internal sealed class CombatAutoPlaySequence
    {
        private const float MaximumReadyWaitSeconds = 5f;
        private readonly CombatSceneController _host;
        private CombatAutoPlaySession _session;

        public CombatAutoPlaySequence(CombatSceneController host) => _host = host;
        public bool IsActive => _session?.IsActive == true;

        public IEnumerator Begin()
        {
            Cancel();
            var session = new CombatAutoPlaySession(_host.combat);
            _session = session;
            return Run(session);
        }

        public void Cancel()
        {
            _session?.Cancel();
            _session = null;
        }

        private bool IsCurrent(CombatAutoPlaySession session) =>
            ReferenceEquals(_session, session) && session.IsActive &&
            _host != null && _host.isActiveAndEnabled && _host.IsBound &&
            ReferenceEquals(_host.combat, session.Source);

        private IEnumerator Run(CombatAutoPlaySession session)
        {
            try
            {
                while (IsCurrent(session))
                {
                    // Rewards and input gates pause the same request. They do
                    // not spend readiness time or select a reward for the player.
                    while (IsCurrent(session) && !_host.CanAcceptAutoPlay)
                        yield return null;
                    if (!IsCurrent(session)) yield break;
                    CardConfig card = session.SelectNext();
                    if (card == null) yield break;

                    float wait = 0f;
                    while (IsCurrent(session) && session.Source.State.Deck.HasInHand(card) &&
                        (!_host.CanAcceptAutoPlay || !_host.IsCardReadyForAutoPlay(card)))
                    {
                        if (_host.CanAcceptAutoPlay)
                        {
                            wait += Time.unscaledDeltaTime;
                            if (wait >= MaximumReadyWaitSeconds)
                            {
                                Debug.LogWarning("[PlayAll] 手牌未就绪，已停止自动出牌，可手动继续。");
                                yield break;
                            }
                        }
                        yield return null;
                    }
                    if (!IsCurrent(session)) yield break;
                    bool success = session.Source.State.Deck.HasInHand(card) &&
                        _host.TryAutoPlayCard(card);
                    session.RecordAttempt(card, success);
                    // Let hand updates and progression gates settle. Attack
                    // presentation may overlap, as with ordinary manual play.
                    yield return null;
                }
            }
            finally
            {
                session.Cancel();
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                    if (_host != null) _host.RefreshAutoPlayButton();
                }
            }
        }
    }
}
