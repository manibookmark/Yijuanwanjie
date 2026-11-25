using UnityEngine;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 敌人当前表现状态的只读快照。调用方可以据此做场景组装与断言，
    /// 但不能越过 EnemyAreaView 改写渲染器、激活状态或阵型位置。
    /// </summary>
    public readonly struct EnemyPresentationSnapshot
    {
        public EnemyPresentationSnapshot(
            string spriteName,
            Vector3 localPosition,
            Bounds worldBounds,
            int sortingOrder,
            bool activeSelf,
            bool activeInHierarchy,
            bool rendererEnabled,
            float alpha)
        {
            SpriteName = spriteName;
            LocalPosition = localPosition;
            WorldBounds = worldBounds;
            SortingOrder = sortingOrder;
            ActiveSelf = activeSelf;
            ActiveInHierarchy = activeInHierarchy;
            RendererEnabled = rendererEnabled;
            Alpha = alpha;
        }

        public string SpriteName { get; }
        /// <summary>可见角色主体在 EnemyArea 坐标系中的当前位置。</summary>
        public Vector3 LocalPosition { get; }
        public Bounds WorldBounds { get; }
        public int SortingOrder { get; }
        public bool ActiveSelf { get; }
        public bool ActiveInHierarchy { get; }
        public bool RendererEnabled { get; }
        public float Alpha { get; }

        public bool IsVisible =>
            ActiveInHierarchy && RendererEnabled && Alpha > 0.001f;
    }
}
