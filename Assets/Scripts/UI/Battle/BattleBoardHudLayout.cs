using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// Owns board-to-Canvas projection and the three outer layout anchors only.
    /// Child views retain their authored 1920x1080 coordinates and exclusively own
    /// animation transforms. The prefab is the persistent layout source; no screen
    /// edge offsets or board transforms are copied into scene controllers.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class BattleBoardHudLayout : MonoBehaviour
    {
        private const string ResourcePath = "UI/Battle/BattleBoardHudLayout";
        [SerializeField] private RectTransform crawlerRoot;
        [SerializeField] private RectTransform pileRoot;
        [SerializeField] private RectTransform comboRoot;
        [SerializeField] private Vector2 referenceSize = new Vector2(1920f, 1080f);
        [SerializeField] private Vector2 referenceFrontCenter = new Vector2(960f, 350f);
        [SerializeField] private Vector2 referenceBackCenter = new Vector2(960f, 765f);
        [SerializeField] private float referenceFrontWidth = 1260f;
        [SerializeField] private float referenceBackWidth = 1040f;
        [SerializeField] private Vector2 sizeScaleLimits = new Vector2(0.85f, 1.15f);
        private EnemyAreaView _board;
        private Camera _worldCamera;
        private Canvas _canvas;
        private RectTransform _rect;
        private PlayPileView _pile;
        private CrawlerResidentLayoutProfile _crawlerLayout;
        private readonly HashSet<long> _viewProjectionHolds = new HashSet<long>();
        private long _nextViewProjectionHold;

        /// <summary>Holds only outer board-projected anchors during a camera
        /// close-up. Child animations and authored offsets retain their owners.</summary>
        public sealed class ViewProjectionHold : IDisposable
        {
            private BattleBoardHudLayout _owner;
            private readonly long _token;

            internal ViewProjectionHold(BattleBoardHudLayout owner, long token)
            {
                _owner = owner;
                _token = token;
            }

            public bool IsCurrent => _owner != null && _owner._viewProjectionHolds.Contains(_token);

            public void Dispose()
            {
                BattleBoardHudLayout owner = _owner;
                _owner = null;
                if (owner != null) owner._viewProjectionHolds.Remove(_token);
            }
        }

        public ViewProjectionHold AcquireViewProjectionHold()
        {
            if (!isActiveAndEnabled) return null;
            long token = checked(_nextViewProjectionHold + 1);
            _nextViewProjectionHold = token;
            _viewProjectionHolds.Add(token);
            return new ViewProjectionHold(this, token);
        }

        public static BattleBoardHudLayout Create(Canvas canvas, EnemyAreaView board, Camera camera)
        {
            GameObject prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
                throw new System.InvalidOperationException("缺少棋盘 HUD 布局预制体：" + ResourcePath);
            var instance = Instantiate(prefab, canvas.transform, false);
            var layout = instance.GetComponent<BattleBoardHudLayout>();
            layout._canvas = canvas;
            layout._board = board;
            layout._worldCamera = camera;
            layout._rect = (RectTransform)layout.transform;
            layout.transform.SetAsFirstSibling();
            return layout;
        }

        public void AttachCrawler(CrawlerResidentPresenter view, CrawlerResidentLayoutProfile profile)
        {
            if (view == null) return;
            _crawlerLayout = profile != null ? profile : CrawlerResidentLayoutProfile.LoadDefault();
            view.transform.SetParent(crawlerRoot, false);
            var rect = (RectTransform)view.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public void AttachPile(PlayPileView view)
        {
            if (view == null) return;
            _pile = view;
            view.transform.SetParent(pileRoot, false);
            ((RectTransform)view.transform).anchoredPosition = view.pilePosition;
            // The point under the stack, not its invisible hit rectangle, follows the board.
            pileRoot.pivot = new Vector2(1f, 0.5f) +
                new Vector2(view.pilePosition.x / referenceSize.x,
                    view.pilePosition.y / referenceSize.y);
        }

        public void AttachCombo(BrushComboView view, Vector2 anchor, Vector2 offset)
        {
            if (view == null) return;
            if (view.transform.parent != comboRoot)
                view.transform.SetParent(comboRoot, false);
            comboRoot.pivot = anchor + new Vector2(
                offset.x / referenceSize.x, offset.y / referenceSize.y);
        }

        private void LateUpdate()
        {
            // Release resumes projection from the newest board/camera/profile;
            // no cached anchoredPosition/scale is restored over newer state.
            if (_viewProjectionHolds.Count > 0) return;
            // Tuning panels keep owning their authored offsets; reread them instead
            // of retaining a second, stale copy when a user adjusts layout in Play.
            if (_pile != null)
                pileRoot.pivot = new Vector2(1f, 0.5f) + new Vector2(
                    _pile.pilePosition.x / referenceSize.x, _pile.pilePosition.y / referenceSize.y);
            if (_crawlerLayout != null)
            {
                Vector2 first = _crawlerLayout.GetSlot(0).anchoredPosition;
                crawlerRoot.pivot = new Vector2(first.x / referenceSize.x, first.y / referenceSize.y);
            }
            if (_board == null || _worldCamera == null || _canvas == null ||
                !_board.TryGetBoardWorldFrame(out Vector3 frontLeft, out Vector3 frontRight,
                    out Vector3 backLeft, out Vector3 backRight)) return;
            if (!TryProject(frontLeft, out Vector2 fl) ||
                !TryProject(frontRight, out Vector2 fr) ||
                !TryProject(backLeft, out Vector2 bl) ||
                !TryProject(backRight, out Vector2 br)) return;
            Place(crawlerRoot, fl, fr, bl, br);
            Place(pileRoot, fl, fr, bl, br);
            Place(comboRoot, fl, fr, bl, br);
        }

        private bool TryProject(Vector3 world, out Vector2 local)
        {
            Vector3 screen = _worldCamera.WorldToScreenPoint(world);
            local = default;
            if (screen.z <= 0f) return false;
            Camera uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : _canvas.worldCamera;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rect, screen, uiCamera, out local);
        }

        private void Place(RectTransform target, Vector2 fl, Vector2 fr, Vector2 bl, Vector2 br)
        {
            Vector2 point = Vector2.Scale(target.pivot, referenceSize);
            float referenceDepth = referenceBackCenter.y - referenceFrontCenter.y;
            float depth = (point.y - referenceFrontCenter.y) / referenceDepth;
            float referenceWidth = Mathf.LerpUnclamped(referenceFrontWidth, referenceBackWidth, depth);
            float referenceCenterX = Mathf.LerpUnclamped(referenceFrontCenter.x, referenceBackCenter.x, depth);
            float column = 0.5f + (point.x - referenceCenterX) / Mathf.Max(1f, referenceWidth);
            Vector2 left = Vector2.LerpUnclamped(fl, bl, depth);
            Vector2 right = Vector2.LerpUnclamped(fr, br, depth);
            target.anchoredPosition = Vector2.LerpUnclamped(left, right, column);
            float widthScale = Vector2.Distance(left, right) / Mathf.Max(1f, referenceWidth);
            float heightScale = Vector2.Distance((fl + fr) * 0.5f, (bl + br) * 0.5f) /
                Mathf.Max(1f, referenceDepth);
            // Keep text and portraits readable without unbounded zoom or stretching.
            float scale = Mathf.Clamp(Mathf.Min(widthScale, heightScale),
                sizeScaleLimits.x, sizeScaleLimits.y);
            target.localScale = Vector3.one * scale;
        }

        private void OnDisable() => _viewProjectionHolds.Clear();
        private void OnDestroy() => _viewProjectionHolds.Clear();
    }
}
