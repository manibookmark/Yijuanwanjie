using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WanjieScroll.UI.Maps
{
    /// <summary>
    /// Owns the overlay's pointer/space hold and ordered input edges. A press asks
    /// the domain to throw; it never declares a hit. Multiple input devices form
    /// one hold, so pointer and space cannot strand the domain's hold counter.
    /// Pausing discards undelivered presses and retains one delivered release.
    /// </summary>
    public sealed class CrystalThrowingInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private readonly Queue<bool> _edges = new Queue<bool>();
        private bool _enabled;
        private bool _pointerHeld;
        private bool _keyboardHeld;
        private bool _combinedHeld;
        private int _deliveredHolds;
        private int _pendingReleases;
        private bool _hasFocus = true;
        private int _pointerId;

        public void SetInputEnabled(bool enabled)
        {
            if (!enabled) ReleaseHold();
            _enabled = enabled && _hasFocus;
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (!_enabled || data == null || data.button != PointerEventData.InputButton.Left || _pointerHeld) return;
            _pointerId = data.pointerId;
            _pointerHeld = true;
            QueueCombinedEdge();
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (data == null || !_pointerHeld || data.pointerId != _pointerId) return;
            _pointerHeld = false;
            QueueCombinedEdge();
        }

        private void Update()
        {
            if (!_enabled) return;
            if (Input.GetKeyDown(KeyCode.Space)) { _keyboardHeld = true; QueueCombinedEdge(); }
            if (Input.GetKeyUp(KeyCode.Space)) { _keyboardHeld = false; QueueCombinedEdge(); }
            // Pointer-up can occur outside the receiver or when another modal
            // takes focus. Mouse and touch release are still observed here.
            if (_pointerHeld && _pointerId < 0 && !Input.GetMouseButton(0))
            { _pointerHeld = false; QueueCombinedEdge(); }
            if (_pointerHeld && _pointerId >= 0)
            {
                bool present = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.fingerId == _pointerId && touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled)
                        present = true;
                }
                if (!present) { _pointerHeld = false; QueueCombinedEdge(); }
            }
        }

        /// <summary>Delivers at most one edge, preserving a quick press/release across frames.</summary>
        public void ReadInput(out bool pressed, out bool released, out bool resettingSavedHold)
        {
            pressed = false; released = false; resettingSavedHold = false;
            if (_pendingReleases > 0)
            {
                _pendingReleases--; _deliveredHolds = Mathf.Max(0, _deliveredHolds - 1);
                released = true; resettingSavedHold = true; return;
            }
            if (_edges.Count == 0) return;
            bool held = _edges.Dequeue();
            pressed = held; released = !held;
            _deliveredHolds = held ? _deliveredHolds + 1 : Mathf.Max(0, _deliveredHolds - 1);
        }

        /// <summary>Restored domain holds are released before accepting a new physical press.</summary>
        public void SynchronizeSavedHold(int heldButtonCount)
        {
            if (heldButtonCount < 0) throw new System.ArgumentOutOfRangeException(nameof(heldButtonCount));
            ReleaseHold();
            _deliveredHolds = heldButtonCount;
            _edges.Clear();
            // Current UI normalizes to a single hold; drain older multi-button
            // source checkpoints one release per legal domain frame.
            _pendingReleases = heldButtonCount;
        }

        /// <summary>Reconciles a failed input save against the actual durable hold counter.</summary>
        public void ApplySavedHold(int heldButtonCount)
        {
            if (heldButtonCount < 0) throw new System.ArgumentOutOfRangeException(nameof(heldButtonCount));
            _deliveredHolds = heldButtonCount;
            _pendingReleases = _combinedHeld && _enabled ? 0 : heldButtonCount;
        }

        private void QueueCombinedEdge()
        {
            bool held = _pointerHeld || _keyboardHeld;
            if (held == _combinedHeld) return;
            _combinedHeld = held;
            _edges.Enqueue(held);
        }

        private void ReleaseHold()
        {
            _pointerHeld = false; _keyboardHeld = false; _combinedHeld = false;
            _edges.Clear();
            _pendingReleases = _deliveredHolds;
        }

        private void OnApplicationFocus(bool hasFocus) { _hasFocus = hasFocus; if (!hasFocus) SetInputEnabled(false); }
        private void OnDisable() => SetInputEnabled(false);
    }
}
