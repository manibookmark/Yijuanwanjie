using System;
using System.Collections.Generic;

namespace WanjieScroll.Bootstrap
{
    /// <summary>跨流程共享的稳定目的地 ID。</summary>
    public static class FlowDestinationIds
    {
        public const string MetaHub = "destination.meta.hub";
    }

    /// <summary>
    /// 稳定目的地注册和路由。本轮默认不注册 MetaHub。
    /// 没有路由时返回 false 和明确错误，不加载任何替代场景。
    /// </summary>
    public sealed class FlowDestinationRouter
    {
        private readonly Dictionary<string, Func<bool>> _handlers =
            new Dictionary<string, Func<bool>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>注册一个目的地处理器。</summary>
        public void Register(string destinationId, Func<bool> handler)
        {
            if (string.IsNullOrWhiteSpace(destinationId))
                throw new ArgumentException("destinationId 不能为空。", nameof(destinationId));
            _handlers[destinationId] = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>注销目的地。</summary>
        public bool Unregister(string destinationId)
        {
            return _handlers.Remove(destinationId);
        }

        /// <summary>目的地是否可路由。</summary>
        public bool CanRoute(string destinationId)
        {
            return !string.IsNullOrWhiteSpace(destinationId) &&
                   _handlers.ContainsKey(destinationId);
        }

        /// <summary>尝试路由。成功返回 true，失败返回 false 和原因。</summary>
        public bool TryRoute(string destinationId, out string error)
        {
            if (string.IsNullOrWhiteSpace(destinationId))
            {
                error = "destinationId 不能为空。";
                return false;
            }

            if (!_handlers.TryGetValue(destinationId, out var handler))
            {
                error = $"常驻页面尚未注册 {destinationId}。";
                return false;
            }

            bool success = handler();
            error = success ? null : $"路由到 {destinationId} 失败。";
            return success;
        }
    }
}
