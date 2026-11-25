using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 场景间临时数据传递通道。场景切换时写入参数，目标场景 Awake/Start 时读取。
    /// <para>数据在目标场景读取后自动清除，避免残留。不用于持久化（持久化用 GameSaveService）。</para>
    /// </summary>
    public static class SceneContext
    {
        private static readonly Dictionary<string, object> Data = new Dictionary<string, object>();

        /// <summary>写入一个值。</summary>
        public static void Set<T>(string key, T value)
        {
            Data[key] = value;
        }

        /// <summary>读取一个值，不存在则返回默认值。</summary>
        public static T Get<T>(string key, T defaultValue = default)
        {
            return Data.TryGetValue(key, out var value) && value is T typed
                ? typed
                : defaultValue;
        }

        /// <summary>尝试读取一个值。</summary>
        public static bool TryGet<T>(string key, out T value)
        {
            if (Data.TryGetValue(key, out var obj) && obj is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>检查是否包含指定 key。</summary>
        public static bool HasKey(string key) => Data.ContainsKey(key);

        /// <summary>移除指定 key。</summary>
        public static void Remove(string key)
        {
            Data.Remove(key);
        }

        /// <summary>清空所有数据。</summary>
        public static void Clear()
        {
            Data.Clear();
        }
    }

    /// <summary>
    /// Fluent API 构建场景上下文参数。
    /// <example>
    /// <code>
    /// SceneLoader.GoTo(SceneNames.Combat, new SceneContextBuilder()
    ///     .Set("volumeId", "xiyou")
    ///     .Set("dungeonIndex", 2));
    /// </code>
    /// </example>
    /// </summary>
    public sealed class SceneContextBuilder
    {
        private readonly List<Action> _actions = new List<Action>();

        public SceneContextBuilder Set<T>(string key, T value)
        {
            _actions.Add(() => SceneContext.Set(key, value));
            return this;
        }

        /// <summary>将所有写入操作应用到 SceneContext。</summary>
        internal void Apply()
        {
            foreach (var action in _actions)
                action();
        }
    }
}
