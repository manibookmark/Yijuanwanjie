using System;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Cards
{
    /// <summary>
    /// Owns only the persistent purple card-edge overlay. CardView
    /// supplies the latest entity snapshot; this owner never infers a status
    /// from a cost difference or writes the real number, tint or payment state.
    /// No animation, callback or external-canvas object can outlive the card.
    /// </summary>
    internal sealed class CardCostConfusionPresentation : IDisposable
    {
        private GameObject _root;
        private Image _rim, _halo;
        private RectTransform _parent;

        internal void Show(RectTransform cardFrame, int cornerRadius)
        {
            if (cardFrame == null) { Hide(); return; }
            if (_root == null || _parent != cardFrame)
            {
                Dispose();
                Build(cardFrame);
            }
            _rim.sprite = RoundedRectSpriteGenerator.GetOutline(cornerRadius, 2);
            _halo.sprite = RoundedRectSpriteGenerator.GetOutline(cornerRadius, 4);
            _root.SetActive(true);
        }

        internal void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void Build(RectTransform cardFrame)
        {
            _parent = cardFrame;
            _root = new GameObject("ConfusedCostStatus", typeof(RectTransform));
            _root.SetActive(false);
            _root.transform.SetParent(cardFrame, false);
            var rootRect = (RectTransform)_root.transform;
            Stretch(rootRect);

            var haloObject = new GameObject("ConfusionEdgeLight", typeof(RectTransform), typeof(Image));
            haloObject.transform.SetParent(rootRect, false);
            _halo = haloObject.GetComponent<Image>();
            Stretch(_halo.rectTransform);
            _halo.rectTransform.offsetMin = new Vector2(-3f, -3f);
            _halo.rectTransform.offsetMax = new Vector2(3f, 3f);
            _halo.type = Image.Type.Sliced;
            _halo.fillCenter = false;
            _halo.color = new Color(0.57f, 0.32f, 0.79f, 0.24f);
            _halo.raycastTarget = false;

            var rimObject = new GameObject("CostStatusRim", typeof(RectTransform), typeof(Image));
            rimObject.transform.SetParent(rootRect, false);
            _rim = rimObject.GetComponent<Image>();
            Stretch(_rim.rectTransform);
            _rim.rectTransform.offsetMin = new Vector2(-1.5f, -1.5f);
            _rim.rectTransform.offsetMax = new Vector2(1.5f, 1.5f);
            _rim.type = Image.Type.Sliced;
            _rim.fillCenter = false;
            _rim.color = new Color(0.53f, 0.30f, 0.72f, 0.94f);
            _rim.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public void Dispose()
        {
            GameObject root = _root;
            _root = null;
            _parent = null;
            _rim = null;
            _halo = null;
            if (root == null) return;
            root.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
