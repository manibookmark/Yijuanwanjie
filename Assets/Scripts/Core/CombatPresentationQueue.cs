using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 表现项接口。一个表现项代表一个需要按序播放的视觉动作（如投射物飞行、命中特效、死亡动画）。
    /// </summary>
    public interface ICombatPresentation
    {
        /// <summary>表现项名称，用于日志和调试。</summary>
        string Name { get; }
    }

    /// <summary>
    /// 异步表现项。需要等待完成回调，队列才继续下一项。
    /// </summary>
    public interface IAsyncCombatPresentation : ICombatPresentation
    {
        /// <summary>开始执行。完成后必须调用 <paramref name="onComplete"/>。</summary>
        void Execute(Action onComplete);
    }

    /// <summary>
    /// 同步表现项。立即执行，不需要等待。
    /// </summary>
    public interface ISyncCombatPresentation : ICombatPresentation
    {
        /// <summary>执行表现项。</summary>
        void Execute();
    }

    /// <summary>
    /// 表现队列。按事件顺序依次播放表现项，动画被跳过/加速时不影响战斗数据。
    /// <para>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>领域层先完成结算，表现层随后展示结果。</item>
    /// <item>异步表现项（如投射物飞行）完成后才继续下一项。</item>
    /// <item>同步表现项（如飘字、闪白）立即执行后继续。</item>
    /// <item>队列可以被跳过（SkipAll），不影响最终战斗数据。</item>
    /// <item>当前为纯架构骨架，CombatVisualBridge 后续逐步迁移使用。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class CombatPresentationQueue
    {
        private readonly Queue<ICombatPresentation> _queue = new Queue<ICombatPresentation>();
        private bool _isProcessing = false;
        private IAsyncCombatPresentation _currentAsync = null;
        private long _executionId;

        /// <summary>队列中剩余项数。</summary>
        public int Count => _queue.Count;

        /// <summary>是否正在处理中。</summary>
        public bool IsProcessing => _isProcessing;

        /// <summary>队列变为非空时触发（首次入队）。</summary>
        public event Action OnQueueStarted;

        /// <summary>队列全部完成时触发。</summary>
        public event Action OnQueueCompleted;

        private readonly MonoBehaviour _host;
        private readonly bool _useHostCoroutine;

        /// <param name="host">可选的 MonoBehaviour 宿主，用于启动协程。为 null 时只支持同步表现项。</param>
        public CombatPresentationQueue(MonoBehaviour host = null)
        {
            _host = host;
            _useHostCoroutine = host != null;
        }

        /// <summary>入队一个表现项。</summary>
        public void Enqueue(ICombatPresentation item)
        {
            if (item == null) return;
            bool wasEmpty = _queue.Count == 0 && !_isProcessing;
            _queue.Enqueue(item);

            if (wasEmpty)
                OnQueueStarted?.Invoke();

            if (!_isProcessing && _queue.Count > 0)
                ProcessNext();
        }

        /// <summary>入队多个表现项。</summary>
        public void EnqueueRange(IEnumerable<ICombatPresentation> items)
        {
            if (items == null) return;
            bool wasEmpty = _queue.Count == 0 && !_isProcessing;
            bool anyAdded = false;

            foreach (var item in items)
            {
                if (item == null) continue;
                _queue.Enqueue(item);
                anyAdded = true;
            }

            if (anyAdded && wasEmpty)
                OnQueueStarted?.Invoke();

            if (anyAdded && !_isProcessing && _queue.Count > 0)
                ProcessNext();
        }

        /// <summary>跳过所有剩余表现项（不影响战斗数据）。</summary>
        public void SkipAll()
        {
            Clear();
            OnQueueCompleted?.Invoke();
        }

        /// <summary>清空队列（不触发完成事件）。</summary>
        public void Clear()
        {
            // Invalidate before releasing state: an old completion must never
            // advance a newly enqueued sequence, even if it uses the same item.
            _executionId++;
            _queue.Clear();
            _currentAsync = null;
            _isProcessing = false;
        }

        private void ProcessNext()
        {
            if (_queue.Count == 0)
            {
                _isProcessing = false;
                OnQueueCompleted?.Invoke();
                return;
            }

            _isProcessing = true;
            long executionId = ++_executionId;
            var item = _queue.Dequeue();

            if (item is IAsyncCombatPresentation asyncItem)
            {
                _currentAsync = asyncItem;
                try
                {
                    asyncItem.Execute(() => OnAsyncComplete(executionId, asyncItem));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PresentationQueue] Async '{item.Name}' threw: {ex.Message}");
                    OnAsyncComplete(executionId, asyncItem);
                }
            }
            else if (item is ISyncCombatPresentation syncItem)
            {
                try
                {
                    syncItem.Execute();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PresentationQueue] Sync '{item.Name}' threw: {ex.Message}");
                }
                if (_isProcessing && executionId == _executionId)
                    ProcessNext();
            }
            else
            {
                Debug.LogWarning($"[PresentationQueue] Unknown presentation type: {item.GetType().Name}");
                if (_isProcessing && executionId == _executionId)
                    ProcessNext();
            }
        }

        private void OnAsyncComplete(long executionId, IAsyncCombatPresentation item)
        {
            if (!_isProcessing || executionId != _executionId ||
                !ReferenceEquals(_currentAsync, item))
                return;
            _currentAsync = null;
            ProcessNext();
        }
    }

    // ──────────────────────────────────────────────
    //  内置表现项
    // ──────────────────────────────────────────────

    /// <summary>同步延迟表现项（等待指定秒数）。</summary>
    public sealed class DelayPresentation : IAsyncCombatPresentation
    {
        public string Name => $"Delay({ _seconds:F2}s)";
        private readonly float _seconds;
        private readonly MonoBehaviour _host;

        public DelayPresentation(float seconds, MonoBehaviour host)
        {
            _seconds = seconds;
            _host = host;
        }

        public void Execute(Action onComplete)
        {
            if (_host == null || _seconds <= 0f)
            {
                onComplete?.Invoke();
                return;
            }
            _host.StartCoroutine(DelayCoroutine(onComplete));
        }

        private IEnumerator DelayCoroutine(Action onComplete)
        {
            yield return new UnityEngine.WaitForSeconds(_seconds);
            onComplete?.Invoke();
        }
    }

    /// <summary>同步动作表现项（立即执行一个 Action）。</summary>
    public sealed class ActionPresentation : ISyncCombatPresentation
    {
        public string Name { get; }
        private readonly Action _action;

        public ActionPresentation(string name, Action action)
        {
            Name = name;
            _action = action;
        }

        public void Execute()
        {
            _action?.Invoke();
        }
    }
}
