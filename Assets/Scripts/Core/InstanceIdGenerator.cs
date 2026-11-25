using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 实例 ID 生成接口。可注入以支持固定 seed 复现。
    /// </summary>
    public interface IInstanceIdGenerator
    {
        int NextId();
    }

    /// <summary>
    /// 默认实例 ID 生成器，从 1 开始递增。
    /// </summary>
    public sealed class DefaultInstanceIdGenerator : IInstanceIdGenerator
    {
        public static readonly IInstanceIdGenerator Instance = new DefaultInstanceIdGenerator();

        private int _next;

        public DefaultInstanceIdGenerator(int start = 1)
        {
            if (start <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(start),
                    "实例 ID 起点必须大于 0。");
            _next = start;
        }

        public int NextId()
        {
            if (_next <= 0)
                throw new InvalidOperationException(
                    "实例 ID 空间已经耗尽，禁止整数回绕后复用身份。");
            int result = _next;
            _next = _next == int.MaxValue ? 0 : _next + 1;
            return result;
        }
    }

    /// <summary>
    /// 确定性实例 ID 生成器，固定起始值，用于测试。
    /// </summary>
    public sealed class SeededInstanceIdGenerator : IInstanceIdGenerator
    {
        private int _next;

        /// <summary>Read-only allocation cursor for deterministic replay verification.
        /// Zero means exhausted; reading it does not allocate an identity.</summary>
        public int NextInstanceId => _next;

        public SeededInstanceIdGenerator(int seed)
        {
            if (seed <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(seed),
                    "实例 ID seed 必须大于 0。");
            _next = seed;
        }

        public int NextId()
        {
            if (_next <= 0)
                throw new InvalidOperationException(
                    "实例 ID 空间已经耗尽，禁止整数回绕后复用身份。");
            int result = _next;
            _next = _next == int.MaxValue ? 0 : _next + 1;
            return result;
        }
    }
}
