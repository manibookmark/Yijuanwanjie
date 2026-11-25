using System;

namespace WanjieScroll.Dungeon
{
    public delegate bool DungeonRunNavigationPrepareHandler(out string error);

    /// <summary>
    /// Owns the checkpoint between one concluded Run and its prepared successor.
    /// Save or navigation retries reuse that exact successor instead of preparing
    /// another Run. Admission and persistence remain caller-owned operations.
    /// </summary>
    public sealed class DungeonRunResultPreparation
    {
        private readonly DungeonRunSession _source;
        private DungeonRunSession _prepared;
        private DungeonRunResultAction _action;
        private bool _persisted;

        public DungeonRunResultPreparation(DungeonRunSession source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public bool TryPrepare(DungeonRunResultAction action,
            DungeonRunNavigationPrepareHandler command, Action persistPrepared,
            out string error)
        {
            error = null;
            if (action != DungeonRunResultAction.ContinueDungeon &&
                action != DungeonRunResultAction.RetryDungeon)
            {
                error = "本次结算不支持此操作。";
                return false;
            }

            if (_prepared != null)
            {
                if (action != _action)
                {
                    error = "本次结算不支持此操作。";
                    return false;
                }
                if (!ReferenceEquals(DungeonRunSessionStore.Current, _prepared))
                {
                    error = "当前探索已改变，请返回神域后重新进入。";
                    return false;
                }
            }
            else
            {
                if (!ReferenceEquals(DungeonRunSessionStore.Current, _source))
                {
                    error = "当前探索已改变，请返回神域后重新进入。";
                    return false;
                }
                if (!_source.TryGetConclusion(out DungeonRunResultSnapshot result) ||
                    (action == DungeonRunResultAction.ContinueDungeon &&
                     result.Status != DungeonRunResultStatus.Victory) ||
                    (action == DungeonRunResultAction.RetryDungeon &&
                     result.Status != DungeonRunResultStatus.Defeat))
                {
                    error = "本次结算不支持此操作。";
                    return false;
                }
                if (command == null)
                {
                    error = "探索尚未准备完成，请重试。";
                    return false;
                }
                if (!command(out error))
                    return false;

                DungeonRunSession current = DungeonRunSessionStore.Current;
                if (current == null || ReferenceEquals(current, _source))
                {
                    error = "探索尚未准备完成，请重试。";
                    return false;
                }
                _prepared = current;
                _action = action;
                // A null callback explicitly means command committed its own
                // required save before adoption. It is not an optional-save flag.
                _persisted = persistPrepared == null;
            }

            if (_persisted)
            {
                error = null;
                return true;
            }
            if (persistPrepared == null)
            {
                // A previously failed external save cannot be waived on retry.
                error = "探索尚未准备完成，请重试。";
                return false;
            }

            // Keep the adopted checkpoint when saving throws. A subsequent
            // attempt must retry only this save, without invoking command again.
            persistPrepared();
            if (!ReferenceEquals(DungeonRunSessionStore.Current, _prepared))
            {
                error = "当前探索已改变，请返回神域后重新进入。";
                return false;
            }
            _persisted = true;
            error = null;
            return true;
        }
    }
}
