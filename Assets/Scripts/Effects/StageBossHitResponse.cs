using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// A stage boss's restrained contact highlight. The owner borrows the current
    /// body sprite onto an independent, locally masked overlay; it never writes
    /// the body's pose, frame, color, visibility, ground anchor or shadow.
    /// One bounded overlay/material pair stays resident while this owner is active.
    /// Pulse completion hides it; Cancel/disable/destroy synchronously hide and
    /// release the pair and retire the pulse generation. There are no coroutines,
    /// delayed callbacks or external waiters to complete after cancellation.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class StageBossHitResponse : MonoBehaviour
    {
        private const string ShaderResourcePath =
            "Shaders/StageBossHitOverlay";
        private static readonly int HitLocalId =
            Shader.PropertyToID("_HitLocal");
        private static readonly int WorldScaleId =
            Shader.PropertyToID("_WorldScale");
        private static readonly int RadiusId =
            Shader.PropertyToID("_Radius");
        private static readonly Color PaperHighlight =
            new Color(1f, 0.94f, 0.83f, 1f);

        private readonly StageBossHitPulse _pulse = new StageBossHitPulse();
        private SpriteRenderer _body;
        private SpriteRenderer _overlay;
        private Material _material;
        private Vector3 _contactLocal;
        private float _radius;
        private bool _missingShaderReported;

        public void Play(Vector3 worldHitPosition, bool critical)
        {
            if (!isActiveAndEnabled || !IsFinite(worldHitPosition))
                return;
            if (_body == null)
                _body = GetComponent<SpriteRenderer>();
            if (!CanShowBody() || !EnsureOverlay())
                return;

            double now = Time.unscaledTimeAsDouble;
            if (_pulse.RegisterHit(now, critical))
            {
                _contactLocal = transform.InverseTransformPoint(
                    worldHitPosition);
                _radius = critical ? 0.9f : 0.7f;
            }
            RefreshOverlay(now);
        }

        private void LateUpdate()
        {
            if (_overlay != null)
                RefreshOverlay(Time.unscaledTimeAsDouble);
        }

        private void RefreshOverlay(double now)
        {
            float strength = _pulse.Sample(now);
            if (_overlay == null)
                return;
            if (strength <= 0f || !CanShowBody())
            {
                _overlay.enabled = false;
                return;
            }

            // The sequence owner may have changed frame, flip, sorting or fade
            // since the impact. Borrow that latest snapshot, never restore one.
            _overlay.sprite = _body.sprite;
            _overlay.flipX = _body.flipX;
            _overlay.flipY = _body.flipY;
            _overlay.sortingLayerID = _body.sortingLayerID;
            _overlay.sortingOrder = Mathf.Min(
                short.MaxValue, _body.sortingOrder + 1);
            _overlay.maskInteraction = _body.maskInteraction;
            _overlay.forceRenderingOff = _body.forceRenderingOff;
            Color color = PaperHighlight;
            color.a = strength * _body.color.a;
            _overlay.color = color;

            Vector3 worldScale = transform.lossyScale;
            _material.SetVector(HitLocalId, new Vector4(
                _contactLocal.x, _contactLocal.y, 0f, 0f));
            _material.SetVector(WorldScaleId, new Vector4(
                Mathf.Abs(worldScale.x), Mathf.Abs(worldScale.y), 1f, 1f));
            _material.SetFloat(RadiusId, _radius);
            _overlay.enabled = true;
        }

        private bool CanShowBody()
            => _body != null && _body.enabled &&
               _body.gameObject.activeInHierarchy &&
               !_body.forceRenderingOff && _body.sprite != null &&
               _body.color.a > 0f;

        private bool EnsureOverlay()
        {
            if (_overlay != null && _material != null)
                return true;

            // A Resources asset gives the player build an explicit shader root;
            // Shader.Find alone can succeed in Editor and be stripped in Player.
            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                if (!_missingShaderReported)
                {
                    _missingShaderReported = true;
                    Debug.LogError(
                        "[StageBossHitResponse] Missing shader resource: " +
                        ShaderResourcePath, this);
                }
                return false;
            }

            _material = new Material(shader)
            {
                name = "StageBossHitOverlay (Owned)",
                hideFlags = HideFlags.DontSave
            };
            var overlayObject = new GameObject("StageBossHitOverlay")
            {
                hideFlags = HideFlags.DontSave,
                layer = gameObject.layer
            };
            overlayObject.transform.SetParent(transform, false);
            _overlay = overlayObject.AddComponent<SpriteRenderer>();
            _overlay.sharedMaterial = _material;
            _overlay.enabled = false;
            return true;
        }

        /// <summary>
        /// Synchronously retires all presentation owned here. The body and its
        /// borrowed sprite/atlas belong to other owners and are never restored
        /// or destroyed by this component. A later Play starts from current state.
        /// </summary>
        public void Cancel()
        {
            _pulse.Cancel();
            SpriteRenderer previousOverlay = _overlay;
            Material previousMaterial = _material;
            _overlay = null;
            _material = null;
            if (previousOverlay != null)
            {
                previousOverlay.enabled = false;
                previousOverlay.sprite = null;
                previousOverlay.gameObject.SetActive(false);
                Destroy(previousOverlay.gameObject);
            }
            if (previousMaterial != null)
                Destroy(previousMaterial);
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();

        private static bool IsFinite(Vector3 point)
            => !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
               !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
               !float.IsNaN(point.z) && !float.IsInfinity(point.z);
    }
}
