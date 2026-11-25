using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Cards
{
    /// <summary>Owns cost-only feedback overlays, never the real cost text, card
    /// pose or visibility. HandView supplies legal draw arrivals and instance
    /// mappings; a pooled slot cannot carry a previous instance's flash.</summary>
    internal sealed class HandCostChangePresentation
    {
        private sealed class Flash
        {
            internal int InstanceId;
            internal CardView View;
            internal Image Image;
            internal float Elapsed;
        }

        private readonly HandView _hand;
        private readonly Dictionary<int, HandCardCostChange> _pending =
            new Dictionary<int, HandCardCostChange>();
        private readonly List<Flash> _flashes = new List<Flash>();
        private Coroutine _routine;
        private int _generation;

        internal HandCostChangePresentation(HandView hand) => _hand = hand;

        internal void Queue(IReadOnlyList<HandCardCostChange> changes)
        {
            if (!_hand.isActiveAndEnabled || changes == null)
            { Cancel(); return; }
            // Replace visible feedback, but retain not-yet-arrived receipts.
            // Consecutive automatic plays may leave one earlier target unchanged.
            CancelFlashes();
            foreach (HandCardCostChange change in changes)
            {
                if (change.RuntimeInstanceId <= 0) continue;
                int before = _pending.TryGetValue(change.RuntimeInstanceId, out var previous)
                    ? previous.BeforeCost : change.BeforeCost;
                _pending[change.RuntimeInstanceId] = new HandCardCostChange(
                    change.RuntimeInstanceId, before, change.AfterCost);
            }
            // Wait for HandView's authoritative refresh/arrival, not an arbitrary frame.
        }

        internal void RefreshReadyCards(System.Func<int, bool> isReady)
        {
            if (!_hand.isActiveAndEnabled) { Cancel(); return; }
            PruneReboundFlashes();
            foreach (int id in new List<int>(_pending.Keys))
            {
                HandCardCostChange change = _pending[id];
                CardView view = _hand.FindInstanceView(id);
                if (change.BeforeCost == change.AfterCost || view == null)
                { _pending.Remove(id); continue; }
                if (!isReady(id) || view.CardData.cost != change.AfterCost) continue;
                _pending.Remove(id);
                if (view.costFrameImage == null) continue;

                var go = new GameObject("CostChangeFlash_" + id,
                    typeof(RectTransform), typeof(Image));
                go.transform.SetParent(view.costFrameImage.transform, false);
                // A readable rim pulse leaves the authoritative number, its
                // affordability colour and the persistent status seal untouched.
                go.transform.SetAsFirstSibling();
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(-3f, -3f);
                rect.offsetMax = new Vector2(3f, 3f);
                Image image = go.GetComponent<Image>();
                image.sprite = RoundedRectSpriteGenerator.GetOutline(6, 3);
                image.type = Image.Type.Sliced;
                image.fillCenter = false;
                image.color = Color.clear;
                image.raycastTarget = false;
                _flashes.Add(new Flash { InstanceId = id, View = view, Image = image });
            }
            if (_flashes.Count > 0 && _routine == null)
                _routine = _hand.StartCoroutine(Animate(_generation));
        }

        private IEnumerator Animate(int generation)
        {
            const float duration = 0.72f;
            while (generation == _generation && _hand.isActiveAndEnabled && _flashes.Count > 0)
            {
                PruneReboundFlashes();
                for (int i = _flashes.Count - 1; i >= 0; i--)
                {
                    Flash flash = _flashes[i];
                    flash.Elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(flash.Elapsed / duration);
                    flash.Image.color = new Color(0.62f, 0.39f, 0.84f,
                        Mathf.Sin(t * Mathf.PI) * 0.9f);
                    if (t >= 1f) RemoveAt(i);
                }
                yield return null;
            }
            if (generation == _generation) _routine = null;
        }

        private void PruneReboundFlashes()
        {
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                Flash flash = _flashes[i];
                if (flash.View == null || flash.Image == null ||
                    !flash.View.gameObject.activeInHierarchy ||
                    flash.View.CardData.runtimeInstanceId != flash.InstanceId)
                    RemoveAt(i);
            }
        }

        private void RemoveAt(int index)
        {
            Image image = _flashes[index].Image;
            _flashes.RemoveAt(index);
            if (image == null) return;
            image.gameObject.SetActive(false);
            Object.Destroy(image.gameObject);
        }

        internal void Cancel()
        {
            _pending.Clear();
            CancelFlashes();
            // Cosmetic feedback has no business callback or completion waiter.
        }

        private void CancelFlashes()
        {
            _generation++;
            Coroutine routine = _routine;
            _routine = null;
            while (_flashes.Count > 0) RemoveAt(_flashes.Count - 1);
            if (routine != null) _hand.StopCoroutine(routine);
        }
    }
}
