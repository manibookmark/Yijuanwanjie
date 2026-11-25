using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Map;
using WanjieScroll.UI;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 地图悬停只显示共享样式定义的节点名称。Effects 不认识内容或规则，
    /// Tooltip 不查询奖励事务，也不生成候选或消耗随机状态。
    /// </summary>
    public sealed class MapInteractionPreviewCoordinator : MonoBehaviour
    {
        private MapPresentation _presentation;
        private DungeonRunSession _run;
        private CardTooltipView _tooltip;

        public void Bind(
            MapPresentation presentation,
            DungeonRunSession run)
        {
            Unbind();
            _presentation = presentation;
            _run = run;
            if (_presentation != null)
                _presentation.HoverChanged += OnHoverChanged;
        }

        private void OnHoverChanged(string groupId)
        {
            _tooltip?.Hide();
            if (string.IsNullOrWhiteSpace(groupId) || _run == null ||
                _presentation == null || !_presentation.IsInteractionEnabled)
                return;
            LevelRunSession session = _run.CurrentFloor;
            LevelNode node = session.FindNode(groupId);
            LevelNodeViewState state = session.GetNodeState(groupId);
            // MapPresentation already suppresses Locked/Hidden hover events.
            // Keep this semantic guard as well so programmatic/future event
            // sources cannot make an unavailable node look interactive.
            if (state == LevelNodeViewState.Locked ||
                state == LevelNodeViewState.Hidden)
                return;
            string nodeName = MapRewardNodeStyleResolver.ResolveName(node,
                state == LevelNodeViewState.Completed);
            if (_run.CardConversions.HasPendingManaPickup(_run.CurrentFloorContentId, groupId))
                nodeName = "灵力";
            if (string.IsNullOrWhiteSpace(nodeName))
                return;
            EnsureTooltip().ShowText("<b>" + Plain(nodeName) + "</b>");
        }

        private CardTooltipView EnsureTooltip()
        {
            if (_tooltip != null) return _tooltip;
            var canvasObject = new GameObject("MapInteractionPreviewCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 31900;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var viewObject = new GameObject("MapInteractionTooltip", typeof(RectTransform));
            viewObject.transform.SetParent(canvasObject.transform, false);
            _tooltip = viewObject.AddComponent<CardTooltipView>();
            return _tooltip;
        }

        private static string Plain(string text)
            => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("<", "＜").Replace(">", "＞");

        private void Unbind()
        {
            if (_presentation != null)
                _presentation.HoverChanged -= OnHoverChanged;
            _tooltip?.Hide();
        }

        private void OnDisable() => _tooltip?.Hide();
        private void OnDestroy() => Unbind();
    }
}
