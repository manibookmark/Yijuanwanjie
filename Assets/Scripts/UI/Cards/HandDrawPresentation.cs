using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Cards
{
    /// <summary>
    /// Owns drawn identities, frozen faces and the draw-to-automatic-play lifecycle.
    /// HandView alone maps real hand instances to slots; CardPlaySequencer alone
    /// animates cards. This owner never restores a played card to Core or the pool.
    /// Cancellation invalidates callbacks before releasing faces/completions.
    /// </summary>
    internal sealed class HandDrawPresentation
    {
        private sealed class FrozenCard
        {
            internal CardDisplayData Data;
            internal Vector2 Position, Size;
            internal Vector3 Scale, VisualScale;
            internal Quaternion Rotation, VisualRotation;
            internal bool AutomaticallyPlayed, DestroyOnUse;
        }

        private sealed class AutomaticFace
        {
            internal CardView View;
            internal bool DestroyOnUse;
        }

        private sealed class Batch
        {
            internal bool DrawFinished;
            internal int AutomaticPlays;
            internal Action Complete;
        }

        private readonly HandView _hand;
        private readonly CardPlaySequencer _motion;
        private readonly CardPresentationProfile _profile;
        private readonly List<int> _pending = new List<int>();
        private readonly HashSet<int> _presenting = new HashSet<int>();
        private readonly Dictionary<int, FrozenCard> _frozen = new Dictionary<int, FrozenCard>();
        private readonly Dictionary<int, AutomaticFace> _faces = new Dictionary<int, AutomaticFace>();
        private readonly HashSet<Batch> _batches = new HashSet<Batch>();
        private RectTransform _source;
        private Action _pendingComplete;
        private Coroutine _pendingRoutine;
        private int _generation;

        internal HandDrawPresentation(HandView hand, CardPlaySequencer motion,
            CardPresentationProfile profile)
        { _hand = hand; _motion = motion; _profile = profile; }

        internal bool IsActive => _pendingRoutine != null || _batches.Count > 0;
        internal bool IsAwaiting(int id) => _pending.Contains(id) || _presenting.Contains(id);
        internal bool IsPending(int id) => _pending.Contains(id);
        internal bool IsPresenting(int id) => _presenting.Contains(id);

        internal void Queue(IReadOnlyList<int> ids, RectTransform source, Action onComplete)
        {
            if (ids == null || ids.Count == 0) { onComplete?.Invoke(); return; }
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] != 0 && !IsAwaiting(ids[i])) _pending.Add(ids[i]);
            if (_pending.Count == 0) { onComplete?.Invoke(); return; }
            _source = source;
            // A second batch with no callback must not erase initial entry completion.
            _pendingComplete += onComplete;
            _hand.RefreshDrawPresentationVisibility();
            if (_pendingRoutine == null && _pending.Count > 0)
                _pendingRoutine = _hand.StartCoroutine(PresentNextFrame());
        }

        internal void CaptureLayout(CardView view)
        {
            int id = view.CardData.runtimeInstanceId;
            if (!_pending.Contains(id)) return;
            RectTransform rect = view.transform as RectTransform;
            if (rect == null) return;
            if (!_frozen.TryGetValue(id, out FrozenCard frozen))
            { frozen = new FrozenCard(); _frozen.Add(id, frozen); }
            if (frozen.AutomaticallyPlayed) return;
            Transform visual = view.visualRoot != null ? view.visualRoot : view.transform;
            frozen.Data = view.CardData;
            // Copy collections as well as scalar display values, without retaining
            // a reusable CardView or changing the authoritative card definition.
            if (frozen.Data.effects != null)
                frozen.Data.effects = new List<CardEffectSpec>(frozen.Data.effects);
            if (frozen.Data.seals != null)
                frozen.Data.seals = new List<CardSealSpec>(frozen.Data.seals);
            frozen.Position = rect.anchoredPosition;
            frozen.Size = rect.sizeDelta;
            frozen.Scale = rect.localScale;
            frozen.Rotation = rect.localRotation;
            frozen.VisualScale = visual.localScale;
            frozen.VisualRotation = visual.localRotation;
        }

        internal void RecordAutomaticPlay(int id, bool destroyOnUse)
        {
            // Only an actual CardEffect receipt marks this snapshot. Author flags
            // alone cannot turn an unplayed card into a departing face.
            if (!_frozen.TryGetValue(id, out FrozenCard frozen)) return;
            frozen.AutomaticallyPlayed = true;
            frozen.DestroyOnUse = destroyOnUse;
        }

        private IEnumerator PresentNextFrame()
        {
            // Core publishes draw IDs, refreshes the complete hand (capturing
            // poses), then synchronously resolves automatic cards before here.
            yield return null;
            int generation = _generation;
            int[] ids = _pending.ToArray();
            _pending.Clear();
            _pendingRoutine = null;
            var views = new List<CardView>();
            var batch = new Batch { Complete = _pendingComplete };
            RectTransform source = _source;
            _pendingComplete = null;
            _batches.Add(batch);
            try
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    int id = ids[i];
                    CardView view;
                    if (_frozen.TryGetValue(id, out FrozenCard frozen) && frozen.AutomaticallyPlayed)
                    {
                        view = CreateFace(frozen);
                        _faces.Add(id, new AutomaticFace { View = view, DestroyOnUse = frozen.DestroyOnUse });
                    }
                    else view = _hand.FindInstanceView(id);
                    _frozen.Remove(id);
                    if (view == null) continue;
                    views.Add(view);
                    _presenting.Add(id);
                }
                _hand.RefreshDrawPresentationVisibility();
                if (views.Count == 0)
                {
                    batch.DrawFinished = true;
                    CompleteBatch(batch);
                }
                else _motion.DrawCardsAnimation(views, source,
                    id => OnArrived(id, batch, generation),
                    () =>
                    {
                        if (generation != _generation) return;
                        batch.DrawFinished = true;
                        CompleteBatch(batch);
                    });
            }
            catch (Exception exception)
            {
                if (generation == _generation)
                {
                    foreach (int id in ids)
                    {
                        _frozen.Remove(id);
                        _presenting.Remove(id);
                        if (!_faces.TryGetValue(id, out AutomaticFace face)) continue;
                        _faces.Remove(id);
                        if (face.View != null) UnityEngine.Object.Destroy(face.View.gameObject);
                    }
                    _hand.RefreshDrawPresentationVisibility();
                    batch.DrawFinished = true;
                    batch.AutomaticPlays = 0;
                    CompleteBatch(batch);
                }
                Debug.LogException(exception, _hand);
            }
        }

        private CardView CreateFace(FrozenCard frozen)
        {
            // Own the construction tree even if the factory fails before it
            // can return a face. Only the completed card enters the hand canvas.
            var staging = new GameObject("AutomaticDrawFaceSetup", typeof(RectTransform));
            staging.transform.SetParent(_hand.cardContainer, false);
            staging.SetActive(false);
            CardView face = null;
            try
            {
                face = CardViewFactory.Create(staging.transform, _profile, addHoverHandler: false);
                face.transform.SetParent(_hand.cardContainer, false);
                face.gameObject.name = "AutomaticDrawFace_" + frozen.Data.runtimeInstanceId;
                face.UpdateCard(frozen.Data, -1);
                RectTransform rect = face.transform as RectTransform;
                rect.anchoredPosition = frozen.Position;
                rect.sizeDelta = frozen.Size;
                rect.localRotation = frozen.Rotation;
                rect.localScale = frozen.Scale;
                if (face.visualRoot != null)
                {
                    face.visualRoot.localRotation = frozen.VisualRotation;
                    face.visualRoot.localScale = frozen.VisualScale;
                }
                CanvasGroup group = face.GetComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = group.interactable = false;
                if (face.cardButton != null) face.cardButton.enabled = false;
                foreach (Graphic graphic in face.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
                return face;
            }
            catch
            {
                if (face != null) UnityEngine.Object.Destroy(face.gameObject);
                throw;
            }
            finally { UnityEngine.Object.Destroy(staging); }
        }

        private void OnArrived(int id, Batch batch, int generation)
        {
            if (generation != _generation || !_presenting.Remove(id)) return;
            if (_faces.TryGetValue(id, out AutomaticFace face))
            {
                // Transfer ownership before starting motion. Cancellation of the
                // sequencer now cleans this card; this ledger must not destroy it.
                _faces.Remove(id);
                batch.AutomaticPlays++;
                _motion.PlayCardAnimation(face.View, face.DestroyOnUse, () =>
                {
                    if (generation != _generation) return;
                    batch.AutomaticPlays--;
                    CompleteBatch(batch);
                });
            }
            _hand.OnDrawCardArrived(id);
        }

        private void CompleteBatch(Batch batch)
        {
            if (!batch.DrawFinished || batch.AutomaticPlays != 0 || !_batches.Remove(batch)) return;
            Action complete = batch.Complete;
            batch.Complete = null;
            complete?.Invoke();
        }

        internal Action Cancel()
        {
            _generation++;
            if (_pendingRoutine != null) _hand.StopCoroutine(_pendingRoutine);
            _pendingRoutine = null;
            Action completion = _pendingComplete;
            _pendingComplete = null;
            foreach (Batch batch in _batches) completion += batch.Complete;
            _batches.Clear();
            foreach (AutomaticFace face in _faces.Values)
                if (face.View != null) UnityEngine.Object.Destroy(face.View.gameObject);
            _faces.Clear();
            _frozen.Clear();
            _pending.Clear();
            _presenting.Clear();
            return completion;
        }
    }
}
