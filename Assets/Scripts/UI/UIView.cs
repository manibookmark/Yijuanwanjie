using UnityEngine;

namespace WanjieScroll.UI
{
    /// <summary>
    /// UI 视图基类（MonoBehaviour）
    /// 所有 UI 面板/组件继承此类，通过 Bind 绑定 ViewModel
    /// </summary>
    public abstract class UIView : MonoBehaviour
    {
        protected ViewModelBase ViewModel { get; private set; }
        protected bool IsBound => ViewModel != null;

        /// <summary>
        /// 绑定 ViewModel，订阅属性变更
        /// </summary>
        public virtual void Bind(ViewModelBase viewModel)
        {
            if (ViewModel != null)
                Unbind();

            ViewModel = viewModel;
            ViewModel.OnPropertyChanged += OnViewModelChanged;
            ViewModel.OnCollectionChanged += OnViewModelCollectionChanged;

            // 首次绑定立即刷新
            OnViewModelChanged("*");
        }

        /// <summary>
        /// 解绑 ViewModel
        /// </summary>
        public virtual void Unbind()
        {
            if (ViewModel == null) return;

            ViewModel.OnPropertyChanged -= OnViewModelChanged;
            ViewModel.OnCollectionChanged -= OnViewModelCollectionChanged;
            ViewModel = null;
        }

        /// <summary>
        /// 属性变更回调（子类实现具体渲染逻辑）
        /// </summary>
        protected abstract void OnViewModelChanged(string propertyName);

        /// <summary>
        /// 集合变更回调（子类可覆盖）
        /// </summary>
        protected virtual void OnViewModelCollectionChanged(string collectionName)
        {
            OnViewModelChanged(collectionName);
        }

        protected virtual void OnDestroy()
        {
            Unbind();
        }
    }
}
