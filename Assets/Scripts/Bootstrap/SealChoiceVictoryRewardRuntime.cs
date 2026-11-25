using System;
using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 胜利灵印表现协调器使用的窄事务接口。实现只做 Dungeon 两层会话的组合，
    /// 不拥有步骤、卡牌或经济状态。
    /// </summary>
    internal interface ISealChoiceVictoryRewardRuntime
    {
        bool TryGetPending(out LevelVictorySealRewardOffer offer);
        bool TryChooseSeal(
            string rewardContentId,
            string sealContentId,
            out string error);
        bool TryBeginAssignment(string rewardContentId, out string error);
        bool TryReturnToDisposition(
            string rewardContentId,
            out string error);
        bool TryReturnToSealChoice(
            string rewardContentId,
            out string error);
        bool TryAssign(
            string rewardContentId,
            int cardRuntimeInstanceId,
            CardSealSpec seal,
            out LevelVictorySealRewardReceipt receipt,
            out string error);
        bool TryCashOut(
            string rewardContentId,
            int baseAmount, IDungeonRunInteractionWallet wallet,
            out LevelVictorySealRewardReceipt receipt,
            out string error);
    }

    internal sealed class SealChoiceVictoryRewardRuntime :
        ISealChoiceVictoryRewardRuntime
    {
        private readonly LevelRunSession _floor;
        private readonly DungeonRunSession _run;
        private readonly Func<IReadOnlyList<CardConfig>> _getCardPool;
        private readonly Action _persist;

        public SealChoiceVictoryRewardRuntime(
            LevelRunSession floor,
            DungeonRunSession run,
            Func<IReadOnlyList<CardConfig>> getCardPool, Action persist)
        {
            _floor = floor ?? throw new ArgumentNullException(nameof(floor));
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _getCardPool = getCardPool ??
                throw new ArgumentNullException(nameof(getCardPool));
            _persist = persist ?? throw new ArgumentNullException(nameof(persist));
            if (_floor != _run.CurrentFloor)
                throw new ArgumentException("Victory reward runtime must bind the current Run floor.");
        }

        public bool TryGetPending(out LevelVictorySealRewardOffer offer)
            => _run.VictorySealRewards.TryGetPending(out offer);

        public bool TryChooseSeal(
            string rewardContentId,
            string sealContentId,
            out string error)
            => _run.VictorySealRewards.TryChooseSeal(
                rewardContentId,
                sealContentId,
                out error, _persist);

        public bool TryBeginAssignment(
            string rewardContentId,
            out string error)
            => _run.VictorySealRewards.TryBeginAssignment(
                rewardContentId, out error, _persist);

        public bool TryReturnToDisposition(
            string rewardContentId,
            out string error)
            => _run.VictorySealRewards.TryReturnToDisposition(
                rewardContentId, out error, _persist);

        public bool TryReturnToSealChoice(
            string rewardContentId,
            out string error)
            => _run.VictorySealRewards.TryReturnToSealChoice(
                rewardContentId, out error, _persist);

        public bool TryAssign(
            string rewardContentId,
            int cardRuntimeInstanceId,
            CardSealSpec seal,
            out LevelVictorySealRewardReceipt receipt,
            out string error)
            => _run.TryAssignVictorySealReward(
                rewardContentId,
                cardRuntimeInstanceId,
                seal,
                _getCardPool(),
                out receipt,
                out error,
                isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult, persist: _persist);

        public bool TryCashOut(
            string rewardContentId,
            int baseAmount, IDungeonRunInteractionWallet wallet,
            out LevelVictorySealRewardReceipt receipt,
            out string error)
            => _run.TryCashOutVictorySealReward(
                rewardContentId,
                baseAmount, wallet,
                out receipt,
                out error, _persist);
    }
}
