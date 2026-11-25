using System;
using System.Collections.Generic;

namespace WanjieScroll.UI
{
    /// <summary>
    /// ViewModel 基类：纯 C# 可观察模式，不依赖 UnityEngine，支持 headless 测试
    /// 提供 Set<T> 属性变更通知 + 集合变更通知
    /// </summary>
    public abstract class ViewModelBase
    {
        /// <summary>属性变更事件，参数=属性名</summary>
        public event Action<string> OnPropertyChanged;

        /// <summary>集合变更事件，参数=集合名</summary>
        public event Action<string> OnCollectionChanged;

        /// <summary>日志事件（headless 测试用）</summary>
        public event Action<string> OnLog;

        private bool _isDirty = true;

        /// <summary>数据是否已变更（需刷新视图）</summary>
        public bool IsDirty => _isDirty;

        /// <summary>
        /// 设置属性值，若变更则触发通知
        /// </summary>
        protected bool Set<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            _isDirty = true;
            OnPropertyChanged?.Invoke(propertyName);
            return true;
        }

        /// <summary>
        /// 通知集合变更
        /// </summary>
        protected void NotifyCollectionChanged(string collectionName)
        {
            _isDirty = true;
            OnCollectionChanged?.Invoke(collectionName);
        }

        /// <summary>
        /// 触发属性变更通知（子类调用）
        /// </summary>
        protected void RaisePropertyChanged(string propertyName)
        {
            _isDirty = true;
            OnPropertyChanged?.Invoke(propertyName);
        }

        /// <summary>
        /// 标记已刷新（视图渲染后调用）
        /// </summary>
        public void MarkClean()
        {
            _isDirty = false;
        }

        /// <summary>
        /// 强制刷新所有属性
        /// </summary>
        public virtual void RefreshAll()
        {
            _isDirty = true;
            OnPropertyChanged?.Invoke("*");
        }

        /// <summary>
        /// 清理：取消所有订阅
        /// </summary>
        public virtual void Dispose()
        {
            OnPropertyChanged = null;
            OnCollectionChanged = null;
            OnLog = null;
        }

        protected void Log(string msg)
        {
            OnLog?.Invoke(msg);
        }
    }

    /// <summary>
    /// 可观察属性（简化版，用于 ViewModel 中的简单属性绑定）
    /// </summary>
    public class ObservableProperty<T>
    {
        public event Action<T> OnValueChanged;
        private T _value;

        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value))
                    return;
                _value = value;
                OnValueChanged?.Invoke(_value);
            }
        }

        public ObservableProperty(T initialValue = default)
        {
            _value = initialValue;
        }
    }

    /// <summary>
    /// 可观察集合（简化版，用于手牌/牌库等列表绑定）
    /// </summary>
    public class ObservableList<T> : List<T>
    {
        public event Action OnListChanged;

        public new void Add(T item)
        {
            base.Add(item);
            OnListChanged?.Invoke();
        }

        public new void Remove(T item)
        {
            base.Remove(item);
            OnListChanged?.Invoke();
        }

        public new void Clear()
        {
            base.Clear();
            OnListChanged?.Invoke();
        }

        public new void Sort(Comparison<T> comparison)
        {
            base.Sort(comparison);
            OnListChanged?.Invoke();
        }

        public void NotifyChanged()
        {
            OnListChanged?.Invoke();
        }
    }
}
