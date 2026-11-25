using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 租约式流程屏障。支持多个并行阻塞者，每个 Hold 返回一个 IDisposable 租约，
    /// 释放后自动移除。Cleared 事件只在最后一个租约释放（1→0）时发布一次。
    /// <para>stableReasonId 仅用于诊断，不作为唯一键——同一类窗口可同时打开多个。</para>
    /// </summary>
    public sealed class FlowBarrier
    {
        private readonly List<Lease> _activeLeases = new List<Lease>();
        private int _nextToken;

        /// <summary>没有活跃租约时返回 true。</summary>
        public bool IsClear => _activeLeases.Count == 0;

        /// <summary>当前活跃租约数量。</summary>
        public int HoldCount => _activeLeases.Count;

        /// <summary>最后一个租约释放时触发。不在 2→1 等中间状态触发。</summary>
        public event Action Cleared;

        /// <summary>
        /// 获取一个租约。返回的 IDisposable 释放后移除该租约。
        /// 重复 Dispose 安全（幂等）。
        /// </summary>
        /// <param name="stableReasonId">诊断用稳定标识，不能为空白。</param>
        public IDisposable Hold(string stableReasonId)
        {
            if (string.IsNullOrWhiteSpace(stableReasonId))
                throw new ArgumentException(
                    "stableReasonId 不能为空白。", nameof(stableReasonId));

            int token = ++_nextToken;
            var lease = new Lease(this, token, stableReasonId);
            _activeLeases.Add(lease);
            return lease;
        }

        /// <summary>返回当前活跃租约的原因 ID 列表副本。</summary>
        public IReadOnlyList<string> GetActiveReasonIds()
        {
            var copy = new List<string>(_activeLeases.Count);
            for (int i = 0; i < _activeLeases.Count; i++)
                copy.Add(_activeLeases[i].ReasonId);
            return copy;
        }

        private void Release(int token)
        {
            int index = -1;
            for (int i = 0; i < _activeLeases.Count; i++)
            {
                if (_activeLeases[i].Token == token)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
                return; // already disposed — idempotent

            _activeLeases.RemoveAt(index);

            if (_activeLeases.Count == 0)
                Cleared?.Invoke();
        }

        private sealed class Lease : IDisposable
        {
            private readonly FlowBarrier _owner;
            private readonly int _token;
            private bool _disposed;

            public int Token => _token;
            public string ReasonId { get; }

            internal Lease(FlowBarrier owner, int token, string reasonId)
            {
                _owner = owner;
                _token = token;
                ReasonId = reasonId;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _owner.Release(_token);
            }
        }
    }
}
