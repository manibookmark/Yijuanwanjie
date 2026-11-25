using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 场景级 UI 控制器
    /// 管理所有 UIView 的生命周期、ViewModel 注册、场景切换
    /// 作为场景中唯一的 UI 入口点
    /// </summary>
    public class UIController : MonoBehaviour
    {
        [Header("美术配置")]
        [Tooltip("全局美术资源配置")]
        public ArtConfig artConfig;

        private Dictionary<Type, ViewModelBase> _viewModels = new Dictionary<Type, ViewModelBase>();
        private List<UIView> _registeredViews = new List<UIView>();

        /// <summary>当前 ArtConfig（静态访问）</summary>
        public static ArtConfig ArtConfig { get; set; }

        private void Awake()
        {
            // 如果未在 Inspector 赋值，尝试运行时加载或创建
            if (artConfig == null)
            {
                artConfig = ScriptableObject.CreateInstance<ArtConfig>();
            }
            ArtConfig = artConfig;
        }

        /// <summary>
        /// 注册 ViewModel
        /// </summary>
        public void RegisterViewModel<T>(T viewModel) where T : ViewModelBase
        {
            _viewModels[typeof(T)] = viewModel;
        }

        /// <summary>
        /// 获取已注册的 ViewModel
        /// </summary>
        public T GetViewModel<T>() where T : ViewModelBase
        {
            if (_viewModels.TryGetValue(typeof(T), out var vm))
                return vm as T;
            return null;
        }

        /// <summary>
        /// 注册 UIView 并绑定对应 ViewModel
        /// </summary>
        public void RegisterView<T>(UIView view) where T : ViewModelBase
        {
            _registeredViews.Add(view);
            var vm = GetViewModel<T>();
            if (vm != null)
            {
                view.Bind(vm);
            }
        }

        /// <summary>
        /// 注销所有 ViewModel（场景切换时调用）
        /// </summary>
        public void ClearAll()
        {
            foreach (var view in _registeredViews)
            {
                view?.Unbind();
            }
            _registeredViews.Clear();

            foreach (var vm in _viewModels.Values)
            {
                vm?.Dispose();
            }
            _viewModels.Clear();
        }

        private void OnDestroy()
        {
            ClearAll();
        }
    }
}
