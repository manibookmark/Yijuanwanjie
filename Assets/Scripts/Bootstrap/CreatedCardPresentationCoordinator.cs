using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Audio;
using WanjieScroll.Core;
using WanjieScroll.Effects;
using WanjieScroll.Effects.Projectiles;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>Joins immutable creation receipts to the source's attack finish.
    /// Core never waits for this queue. UI owns real hand visibility and proxies;
    /// source identity and cancellation stay here, not in log text or card names.</summary>
    public sealed class CreatedCardPresentationCoordinator : MonoBehaviour
    {
        private sealed class Batch
        {
            public int SourceId;
            public Vector2 SourceScreen;
            public List<int> InstanceIds = new List<int>();
        }
        private CombatController _combat;
        private CombatVisualBridge _bridge;
        private HandView _hand;
        private Camera _camera;
        private SupportArtifactFeedbackCoordinator _origins;
        private readonly List<Batch> _pending = new List<Batch>();

        public void Bind(CombatController combat, CombatVisualBridge bridge, HandView hand,
            Camera camera, SupportArtifactFeedbackCoordinator origins)
        {
            Unbind(); _combat = combat; _bridge = bridge; _hand = hand;
            _camera = camera; _origins = origins;
            if (_combat != null) _combat.OnCardPlayResolved += OnResolved;
            if (_bridge != null) _bridge.CardAttackPresentationCompleted += OnAttackFinished;
            if (_bridge != null) _bridge.CardAttackPresentationCancelled += OnAttackCancelled;
        }

        private void OnResolved(CombatCardResolvedEvent result)
        {
            if (result?.Card == null || _hand == null || result.CreatedCards == null) return;
            VfxProfile profile = VfxProfileRegistry.Get(result.Card);
            if (profile?.visualType != AuthoritativeSandWavePlayer.VisualTypeName) return;
            var batch = new Batch { SourceId = result.Card.runtimeInstanceId };
            Vector3 origin = _origins?.FindCardOrigin(batch.SourceId) ??
                (_camera != null ? _camera.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f,
                    Screen.height * 0.2f, 10f)) : Vector3.zero);
            // Support feedback may already have consumed its origin. The
            // attack bridge's retained captured origin is therefore preferred.
            if (_bridge != null && _bridge.TryGetCardCastOrigin(batch.SourceId, out Vector3 cast)) origin = cast;
            batch.SourceScreen = _camera != null ? (Vector2)_camera.WorldToScreenPoint(origin) :
                new Vector2(Screen.width * 0.5f, Screen.height * 0.2f);
            foreach (CombatCreatedCardReceipt receipt in result.CreatedCards)
                if (receipt.Placement == EncounterTemporaryCardPlacement.Hand && receipt.CreatedCardInstanceId > 0)
                    batch.InstanceIds.Add(receipt.CreatedCardInstanceId);
            if (batch.InstanceIds.Count == 0) return;
            _hand.QueueGeneratedCards(batch.InstanceIds);
            if (_bridge == null) Present(batch);
            else _pending.Add(batch);
        }

        private void OnAttackFinished(int sourceId)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].SourceId == sourceId)
                { Batch batch = _pending[i]; _pending.RemoveAt(i); Present(batch); }
        }

        private void Present(Batch batch)
        {
            _hand?.PresentGeneratedCards(batch.InstanceIds, batch.SourceScreen);
            AudioClip clip = Resources.Load<AudioClip>("Audio/SFX/liusha-condense-v2");
            if (clip != null && AudioManager.Instance != null)
                AudioManager.Instance.PlayOneShot(clip, SfxType.CardPlay, 1f, 0.64f);
        }

        private void OnAttackCancelled(int sourceId)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].SourceId == sourceId)
                { _hand?.RevealGeneratedCards(_pending[i].InstanceIds); _pending.RemoveAt(i); }
        }

        public void Unbind()
        {
            if (_combat != null) _combat.OnCardPlayResolved -= OnResolved;
            if (_bridge != null) _bridge.CardAttackPresentationCompleted -= OnAttackFinished;
            if (_bridge != null) _bridge.CardAttackPresentationCancelled -= OnAttackCancelled;
            foreach (var batch in _pending) _hand?.RevealGeneratedCards(batch.InstanceIds);
            _pending.Clear(); _combat = null; _bridge = null; _hand = null; _origins = null; _camera = null;
        }
        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();
    }
}
