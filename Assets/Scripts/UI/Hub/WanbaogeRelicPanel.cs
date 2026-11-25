using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Progression;

namespace WanjieScroll.UI.Hub
{
    /// <summary>万宝阁的纯展示面板：只读展示遗珍收集状态，不提供局内效果。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class WanbaogeRelicPanel : MonoBehaviour
    {
        public event Action CloseRequested;
        public Button ReturnButton { get; private set; }
        private CanvasGroup _group;
        private Text _summary;
        private Text _body;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            HideImmediately();
            BuildView();
        }

        public void SetCollection(RelicCollectionSnapshot collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            _summary.text = $"遗珍收录  {collection.OwnedCount} / {collection.TotalCount}";
            var body = new StringBuilder();
            foreach (RelicCollectionItem item in collection.Items)
            {
                if (body.Length > 0) body.Append("\n\n");
                body.Append(item.Name).Append(item.IsOwned ? "  ·  已获得" : "  ·  未获得")
                    .Append('\n').Append(item.Description)
                    .Append("\n获取：").Append(item.UnlockLocation);
            }
            _body.text = body.Length > 0 ? body.ToString() : "暂无可收录的遗珍。";
            ((RectTransform)_body.transform).anchoredPosition = Vector2.zero;
        }

        public void Show() { transform.SetAsLastSibling(); _group.alpha = 1f; _group.interactable = true; _group.blocksRaycasts = true; }
        public void Hide() { HideImmediately(); CloseRequested?.Invoke(); }
        public void HideImmediately() { if (_group == null) return; _group.alpha = 0f; _group.interactable = false; _group.blocksRaycasts = false; }

        private void BuildView()
        {
            var root = (RectTransform)transform;
            var backdrop = gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.035f, 0.025f, 0.02f, 0.94f);
            backdrop.raycastTarget = true;
            var card = new GameObject("WanbaogeCard", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(root, false);
            var cardRect = (RectTransform)card.transform; cardRect.anchorMin = new Vector2(.16f,.10f); cardRect.anchorMax = new Vector2(.84f,.90f); cardRect.offsetMin=cardRect.offsetMax=Vector2.zero;
            card.GetComponent<Image>().color = new Color(.10f,.075f,.05f,.98f);
            var title = MakeText(cardRect, "万宝阁", 42, new Vector2(0, .86f), new Vector2(1,.97f), TextAnchor.MiddleCenter);
            title.color = new Color(.95f,.78f,.35f);
            _summary = MakeText(cardRect, "", 24, new Vector2(.10f,.78f), new Vector2(.90f,.86f), TextAnchor.MiddleCenter);
            _summary.color = new Color(.92f,.88f,.76f);
            var viewport = new GameObject("RelicList", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(cardRect, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = new Vector2(.10f,.19f);
            viewportRect.anchorMax = new Vector2(.90f,.76f);
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = Color.clear;
            _body = MakeText(viewportRect, "", 24, new Vector2(0f,1f), Vector2.one, TextAnchor.UpperLeft);
            _body.color = new Color(.92f,.88f,.76f);
            _body.lineSpacing = 1.12f;
            var bodyRect = (RectTransform)_body.transform;
            bodyRect.pivot = new Vector2(.5f, 1f);
            var fitter = _body.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = bodyRect;
            scroll.horizontal = false;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
            var hint = MakeText(cardRect, "滚动查看遗珍", 18, new Vector2(.10f,.14f), new Vector2(.90f,.19f), TextAnchor.MiddleCenter);
            hint.color = new Color(.68f,.60f,.46f);
            ReturnButton = MakeButton(cardRect, "返回", new Vector2(.38f,.05f), new Vector2(.62f,.14f));
            ReturnButton.onClick.AddListener(Hide);
        }

        private static Text MakeText(RectTransform parent,string value,int size,Vector2 min,Vector2 max,TextAnchor anchor)
        { var o=new GameObject("Text",typeof(RectTransform),typeof(Text)); o.transform.SetParent(parent,false); var r=(RectTransform)o.transform;r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;var t=o.GetComponent<Text>();t.text=value;t.font=Resources.Load<Font>("Fonts/NotoSerifSC-Medium") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=size;t.alignment=anchor;t.supportRichText=false;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Overflow;return t; }
        private static Button MakeButton(RectTransform parent,string label,Vector2 min,Vector2 max)
        { var o=new GameObject("ReturnButton",typeof(RectTransform),typeof(Image),typeof(Button));o.transform.SetParent(parent,false);var r=(RectTransform)o.transform;r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;o.GetComponent<Image>().color=new Color(.35f,.22f,.10f,1);var t=MakeText(r,label,22,Vector2.zero,Vector2.one,TextAnchor.MiddleCenter);t.color=Color.white;return o.GetComponent<Button>(); }
    }
}
