using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 统一保证 hover 只由当前指针下最上层的 UI 命中目标处理。
    /// 这也覆盖了跨 Canvas 的面板：底层页面不能因为自己的事件链仍在运行而响应。
    /// </summary>
    internal static class TopmostHoverGate
    {
        private static EventSystem _worldEventSystem;
        private static PointerEventData _worldPointerData;
        private static readonly System.Collections.Generic.List<RaycastResult>
            WorldRaycastResults = new System.Collections.Generic.List<RaycastResult>();
        private static readonly System.Collections.Generic.List<RaycastResult>
            UiRaycastResults = new System.Collections.Generic.List<RaycastResult>();
        private static readonly System.Collections.Generic.List<CanvasGroup>
            ParentCanvasGroups = new System.Collections.Generic.List<CanvasGroup>();

        /// <summary>
        /// 世界对象自行检测鼠标命中前，先尊重 UI 的射线遮挡。不可点击的按钮
        /// 和弹窗动画仍可挡住世界 Hover；Tooltip 等透射元素由 GraphicRaycaster
        /// 按 raycastTarget、blocksRaycasts 和 ignoreParentGroups 自行过滤。
        /// </summary>
        public static bool IsPointerOverBlockingUi(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

            if (_worldPointerData == null || _worldEventSystem != eventSystem)
            {
                _worldEventSystem = eventSystem;
                _worldPointerData = new PointerEventData(eventSystem);
            }
            _worldPointerData.Reset();
            _worldPointerData.position = screenPosition;
            WorldRaycastResults.Clear();
            try
            {
                eventSystem.RaycastAll(_worldPointerData, WorldRaycastResults);
                for (int i = 0; i < WorldRaycastResults.Count; i++)
                {
                    RaycastResult result = WorldRaycastResults[i];
                    if (!(result.module is GraphicRaycaster)) continue;
                    GameObject hit = result.gameObject;
                    if (hit == null || !hit.activeInHierarchy) continue;
                    Graphic graphic = hit.GetComponent<Graphic>();
                    if (graphic != null && graphic.isActiveAndEnabled &&
                        graphic.raycastTarget)
                        return true;
                }
                return false;
            }
            finally
            {
                WorldRaycastResults.Clear();
            }
        }

        public static bool IsTopmost(PointerEventData eventData, GameObject target)
        {
            if (eventData == null || target == null || EventSystem.current == null)
                return true;

            UiRaycastResults.Clear();
            try
            {
                EventSystem.current.RaycastAll(eventData, UiRaycastResults);
                Transform targetTransform = target.transform;
                for (int i = 0; i < UiRaycastResults.Count; i++)
                {
                    GameObject hit = UiRaycastResults[i].gameObject;
                    if (hit == null || !hit.activeInHierarchy) continue;
                    // 不缓存跨帧命中：同帧新开的模态面板仍须立即挡住底层 Hover。
                    if (!IsRaycastEligible(hit)) continue;
                    return hit.transform == targetTransform ||
                           hit.transform.IsChildOf(targetTransform);
                }
                return true;
            }
            finally
            {
                UiRaycastResults.Clear();
            }
        }

        private static bool IsRaycastEligible(GameObject hit)
        {
            ParentCanvasGroups.Clear();
            try
            {
                hit.GetComponentsInParent(true, ParentCanvasGroups);
                for (int i = 0; i < ParentCanvasGroups.Count; i++)
                {
                    if (!ParentCanvasGroups[i].interactable ||
                        !ParentCanvasGroups[i].blocksRaycasts)
                        return false;
                }
                Graphic graphic = hit.GetComponent<Graphic>();
                return graphic == null || graphic.raycastTarget;
            }
            finally
            {
                ParentCanvasGroups.Clear();
            }
        }
    }
}
