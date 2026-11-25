using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns one accepted map reload from its deferred preparation to transition
    /// completion. Tokens protect cleanup from older coroutines; a failure
    /// releases ownership before notifying its caller, which may immediately retry.
    /// Scene loading, preparation work and presentation remain with the host.
    /// </summary>
    public sealed class MapReloadRequestLifecycle
    {
        public sealed class Request
        {
            public string RequestId { get; }
            internal Request() { RequestId = Guid.NewGuid().ToString("N"); }
        }

        private enum Phase { Scheduled, Preparing, Started }
        private Request _current;
        private Phase _phase;
        private Action<string> _onFailed;

        public bool IsBusy => _current != null;
        public string TransitionRequestId => _phase == Phase.Started ? _current?.RequestId : null;

        /// <summary>A synchronous rejection never owns or invokes the supplied callback.</summary>
        public bool TrySchedule(bool transitionIsIdle, Action<string> onFailed,
            out Request request, out string error)
        {
            request = null;
            if (IsBusy || !transitionIsIdle)
            {
                error = "已有场景切换正在进行。";
                return false;
            }
            request = new Request();
            _current = request;
            _phase = Phase.Scheduled;
            _onFailed = onFailed;
            error = null;
            return true;
        }

        public bool IsCurrent(Request request)
            => request != null && ReferenceEquals(_current, request);

        /// <summary>
        /// Rechecks the deferred handoff before running preparation. A rejected
        /// or throwing preparation leaves the token owned until the host has
        /// released its preparation lease and calls TryFail.
        /// </summary>
        public bool TryPrepareTransition(Request request, bool transitionIsIdle,
            Action prepare, out string error)
        {
            if (!IsCurrent(request) || _phase != Phase.Scheduled)
            { error = "地图切换请求已失效。"; return false; }
            if (!transitionIsIdle)
            { error = "已有其它场景切换，地图切换未完成。"; return false; }
            _phase = Phase.Preparing;
            try { prepare?.Invoke(); }
            catch (Exception exception)
            { error = FailureMessage(exception.Message); return false; }
            if (!IsCurrent(request))
            { error = "地图切换请求已失效。"; return false; }
            _phase = Phase.Started;
            error = null;
            return true;
        }

        /// <summary>Only a started map request can consume a transition result.</summary>
        public bool TryComplete(SceneTransitionResult result)
        {
            if (_phase != Phase.Started || _current == null ||
                !string.Equals(_current.RequestId, result.RequestId, StringComparison.Ordinal) ||
                !string.Equals(result.DestinationScene, SceneNames.LevelMap, StringComparison.Ordinal))
                return false;
            return Finish(_current, result.IsSuccess ? null : FailureMessage(result.Error));
        }

        public bool TryFail(Request request, string error)
            => Finish(request, FailureMessage(error));

        /// <summary>Silent cleanup for a rejected host setup or abandoned, unstarted coroutine.</summary>
        public bool TryDiscard(Request request)
            => _phase != Phase.Started && Finish(request, null);

        private bool Finish(Request request, string error)
        {
            if (!IsCurrent(request)) return false;
            Action<string> notify = error == null ? null : _onFailed;
            _current = null;
            _phase = Phase.Scheduled;
            _onFailed = null;
            notify?.Invoke(error);
            return true;
        }

        private static string FailureMessage(string error)
            => string.IsNullOrWhiteSpace(error) ? "地图切换失败，请重试。" : error;
    }
}
