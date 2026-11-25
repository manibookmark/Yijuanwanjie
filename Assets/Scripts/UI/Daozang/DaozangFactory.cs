using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Daozang
{
    /// <summary>
    /// 道藏界面装配入口。
    /// 布局来源唯一：DaozangRoot.prefab。
    /// 工厂只负责实例化 prefab + 注入表现配置 + 构建动态内容，不从零构建 UI 层级。
    /// </summary>
    public static class DaozangFactory
    {
        public const string ResourcePath = "UI/Daozang/DaozangRoot";

        public static DaozangView Create(
            Transform parent,
            DaozangController controller,
            DaozangPresentationProfile profile)
        {
            profile ??= ScriptableObject.CreateInstance<DaozangPresentationProfile>();

            // Editor and Player share the same authored asset and loading route.
            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"[DaozangFactory] DaozangRoot prefab not found: Resources/{ResourcePath}");
                return null;
            }

            var go = Object.Instantiate(prefab, parent, false);
            var view = go.GetComponent<DaozangView>();
            if (view == null)
            {
                Debug.LogError("[DaozangFactory] DaozangView component not found on prefab");
                return null;
            }

            view.ApplyPresentation(profile);
            view.BuildDynamicContent(controller);
            view.Initialize(controller, profile);
            return view;
        }
    }
}
