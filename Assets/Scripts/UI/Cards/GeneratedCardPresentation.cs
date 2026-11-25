using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.UI;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Cards
{
    /// <summary>Owns generated-card proxies and sand condensation only.
    /// HandView owns the real instance-to-slot mapping and its visibility;
    /// cancellation always releases the original instance, never a sorted slot.</summary>
    public sealed class GeneratedCardPresentation : MonoBehaviour
    {
        private readonly HashSet<int> _activeIds = new HashSet<int>();
        private readonly List<GameObject> _proxies = new List<GameObject>();
        private Action<int> _onArrived;
        public bool IsActive => _activeIds.Count > 0;

        public void Play(IReadOnlyList<int> instanceIds, Vector2 sourceScreen,
            Func<int, CardView> findView, Action<int> onArrived)
        {
            _onArrived = onArrived;
            for (int i = 0; instanceIds != null && i < instanceIds.Count; i++)
            {
                int id = instanceIds[i];
                if (!_activeIds.Add(id)) continue;
                if (!isActiveAndEnabled) { Release(id); continue; }
                StartCoroutine(Present(id, sourceScreen, findView));
            }
        }

        private IEnumerator Present(int id, Vector2 sourceScreen, Func<int, CardView> findView)
        {
            yield return null;
            CardView view = findView?.Invoke(id);
            Canvas canvas = view != null ? view.GetComponentInParent<Canvas>() : null;
            RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            if (view == null || canvasRect == null) { Release(id); yield break; }
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sourceScreen, camera, out Vector2 source);
            var rootObject = new GameObject("CreatedSandSymbol_" + id, typeof(RectTransform), typeof(CanvasGroup));
            rootObject.transform.SetParent(canvas.transform, false);
            _proxies.Add(rootObject);
            RectTransform root = (RectTransform)rootObject.transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = ((RectTransform)view.transform).rect.size;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
            BuildFace(root, view);
            var sandObject = new GameObject("CondensingSand", typeof(RectTransform), typeof(SandSpiritTransferGraphic));
            sandObject.transform.SetParent(canvas.transform, false);
            _proxies.Add(sandObject);
            RectTransform sandRect = (RectTransform)sandObject.transform;
            sandRect.anchorMin = sandRect.anchorMax = sandRect.pivot = new Vector2(0.5f, 0.5f);
            sandRect.sizeDelta = canvasRect.rect.size;
            var sand = sandObject.GetComponent<SandSpiritTransferGraphic>();
            float elapsed = 0f;
            while (elapsed < 0.36f && _activeIds.Contains(id))
            {
                elapsed += Time.unscaledDeltaTime;
                view = findView?.Invoke(id);
                if (view == null || !view.gameObject.activeInHierarchy) break;
                Vector2 targetScreen = RectTransformUtility.WorldToScreenPoint(camera, view.transform.position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, targetScreen, camera, out Vector2 destination);
                float form = Mathf.Clamp01(elapsed / 0.13f);
                float flight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 0.13f) / 0.23f));
                root.anchoredPosition = Vector2.Lerp(source, destination, flight) +
                    Vector2.up * Mathf.Sin(flight * Mathf.PI) * 45f;
                root.localScale = Vector3.one * Mathf.Lerp(0.68f, view.visualRoot != null ? view.visualRoot.localScale.x : 0.6f, flight);
                root.localRotation = Quaternion.Slerp(Quaternion.identity,
                    view.visualRoot != null ? view.visualRoot.localRotation : Quaternion.identity, flight);
                group.alpha = Mathf.SmoothStep(0f, 1f, form);
                sand.SetPath(source, source, form, condensing: true);
                yield return null;
            }
            Remove(rootObject); Remove(sandObject); Release(id);
        }

        private static void BuildFace(RectTransform root, CardView view)
        {
            // Use the standard face factory and the actual instance's frozen
            // display data. No cloning of a live view, hover or button handlers.
            CardView face = CardViewFactory.Create(root, addHoverHandler: false);
            face.UpdateCard(view.CardData, -1);
            face.transform.localPosition = Vector3.zero;
            if (face.cardButton != null) face.cardButton.enabled = false;
            foreach (Graphic graphic in face.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
        }

        private void Release(int id)
        { if (_activeIds.Remove(id)) _onArrived?.Invoke(id); }
        private void Remove(GameObject proxy)
        { _proxies.Remove(proxy); if (proxy != null) Destroy(proxy); }
        public void Cancel()
        {
            StopAllCoroutines();
            foreach (var proxy in _proxies) if (proxy != null) Destroy(proxy);
            _proxies.Clear();
            foreach (int id in new List<int>(_activeIds)) Release(id);
        }
        private void OnDisable() => Cancel();
    }
}
