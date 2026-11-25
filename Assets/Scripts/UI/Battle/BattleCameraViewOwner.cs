using System;
using UnityEngine;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// Owns one real battle-camera view channel. An identity parent supplies
    /// view translation while ScreenShake keeps its existing camera-child local
    /// motion. This owner alone writes its parent offset and the camera lens;
    /// StageAspectEnforcer still owns camera.rect. No second camera is created.
    /// Cleanup removes this channel and uses the latest configured base size
    /// and current parent transform, never a saved world-space camera pose.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(400)]
    public sealed class BattleCameraViewOwner : MonoBehaviour
    {
        public sealed class ViewLease : IDisposable
        {
            private BattleCameraViewOwner _owner;
            private readonly long _token;

            internal ViewLease(BattleCameraViewOwner owner, long token)
            {
                _owner = owner;
                _token = token;
            }

            public bool IsCurrent => _owner != null && _owner.IsCurrent(_token);

            public bool SetProgress(float progress)
                => _owner != null && _owner.SetProgress(_token, progress);

            public void Dispose()
            {
                BattleCameraViewOwner owner = _owner;
                _owner = null;
                if (owner != null) owner.Release(_token);
            }
        }

        private readonly BattleCameraViewLifetime _lifetime = new BattleCameraViewLifetime();
        private Camera _camera;
        private Transform _viewRoot;
        private Vector3 _baseCameraLocalPosition;
        private float _baseOrthographicSize;
        private Vector3 _focusWorld;
        private BattleBoardHudLayout _hudLayout;
        private BattleBoardHudLayout.ViewProjectionHold _hudHold;
        private bool _destroying;

        public bool IsViewActive => _lifetime.IsActive;

        public static BattleCameraViewOwner EnsureOn(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            BattleCameraViewOwner owner = camera.GetComponent<BattleCameraViewOwner>();
            if (owner == null) owner = camera.gameObject.AddComponent<BattleCameraViewOwner>();
            owner.EnsureRig();
            return owner;
        }

        public void ConfigureBaseOrthographicSize(float size)
        {
            if (!Finite(size) || size <= 0f) throw new ArgumentOutOfRangeException(nameof(size));
            if (_destroying) return;
            EnsureRig();
            _baseOrthographicSize = size;
            // Bootstrap calls this after writing its canonical camera position.
            // Never capture an in-flight shake as a new base during a view.
            if (!_lifetime.IsActive && _camera != null)
                _baseCameraLocalPosition = _camera.transform.localPosition;
            ApplyView();
        }

        public void BindHudLayout(BattleBoardHudLayout layout)
        {
            if (_hudLayout == layout) return;
            CancelView();
            _hudLayout = layout;
        }

        public bool TryAcquire(Vector3 focusWorld, float targetZoom, out ViewLease lease)
        {
            lease = null;
            if (!ContextAvailable() || !Finite(focusWorld.x) || !Finite(focusWorld.y) ||
                !Finite(focusWorld.z) || !BattleCameraViewLifetime.IsValidZoom(targetZoom)) return false;
            CancelView();
            if (!EnsureHudHold()) return false;
            long token = _lifetime.Begin(targetZoom);
            _focusWorld = focusWorld;
            ApplyView();
            lease = new ViewLease(this, token);
            return true;
        }

        public void CancelView()
        {
            _lifetime.Cancel();
            ApplyNeutral();
            ReleaseHudHold();
        }

        private bool IsCurrent(long token) => !_destroying && _lifetime.IsCurrent(token);

        private bool SetProgress(long token, float progress)
        {
            if (!_lifetime.IsCurrent(token)) return false;
            if (!ContextAvailable())
            {
                CancelView();
                return false;
            }
            if (!EnsureHudHold() || !_lifetime.TrySetProgress(token, progress)) return false;
            ApplyView();
            return true;
        }

        private void Release(long token)
        {
            if (!_lifetime.TryRelease(token)) return;
            ApplyNeutral();
            ReleaseHudHold();
        }

        private bool ContextAvailable()
        {
            return !_destroying && isActiveAndEnabled && _camera != null &&
                   _camera.isActiveAndEnabled && _camera.orthographic &&
                   _viewRoot != null && _camera.transform.parent == _viewRoot &&
                   _hudLayout != null && _hudLayout.isActiveAndEnabled &&
                   Finite(_baseOrthographicSize) && _baseOrthographicSize > 0f;
        }

        private void EnsureRig()
        {
            if (_destroying) return;
            if (_camera == null) _camera = GetComponent<Camera>();
            if (_camera == null) throw new InvalidOperationException("Battle camera owner has no Camera.");
            if (_viewRoot != null)
            {
                if (_camera.transform.parent == _viewRoot) return;
                // A different scene/rig has taken the camera. Retire our old
                // channel without writing its cached lens into that new rig.
                CancelView();
                GameObject retiredRoot = _viewRoot.gameObject;
                _viewRoot = null;
                DestroyOwnedRoot(retiredRoot);
            }
            Transform cameraTransform = _camera.transform;
            Transform originalParent = cameraTransform.parent;
            var root = new GameObject("CameraViewRoot");
            if (root.scene != gameObject.scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.transform.SetParent(originalParent, false);
            _viewRoot = root.transform;
            _viewRoot.localPosition = Vector3.zero;
            _viewRoot.localRotation = Quaternion.identity;
            _viewRoot.localScale = Vector3.one;
            _baseCameraLocalPosition = cameraTransform.localPosition;
            _baseOrthographicSize = _camera.orthographicSize;
            // The inserted parent is identity relative to the original parent:
            // child local pose and ScreenShake's cached local base stay valid.
            cameraTransform.SetParent(_viewRoot, false);
        }

        private bool EnsureHudHold()
        {
            if (_hudHold != null && _hudHold.IsCurrent) return true;
            ReleaseHudHold();
            if (_hudLayout == null || !_hudLayout.isActiveAndEnabled) return false;
            _hudHold = _hudLayout.AcquireViewProjectionHold();
            return _hudHold != null && _hudHold.IsCurrent;
        }

        private void ReleaseHudHold()
        {
            BattleBoardHudLayout.ViewProjectionHold hold = _hudHold;
            _hudHold = null;
            hold?.Dispose();
        }

        private void ApplyView()
        {
            if (_camera == null || _viewRoot == null || _camera.transform.parent != _viewRoot) return;
            if (!_lifetime.IsActive)
            {
                ApplyNeutral();
                return;
            }
            Transform parent = _viewRoot.parent;
            Vector3 baseCenter = parent != null
                ? parent.TransformPoint(_baseCameraLocalPosition) : _baseCameraLocalPosition;
            Vector3 focusDelta = _focusWorld - baseCenter;
            Vector3 offset = (_camera.transform.right * Vector3.Dot(focusDelta, _camera.transform.right) +
                              _camera.transform.up * Vector3.Dot(focusDelta, _camera.transform.up)) * _lifetime.Progress;
            _viewRoot.localPosition = parent != null ? parent.InverseTransformVector(offset) : offset;
            _camera.orthographicSize = _baseOrthographicSize / _lifetime.Zoom;
        }

        private void ApplyNeutral()
        {
            if (_viewRoot == null || _camera == null || _camera.transform.parent != _viewRoot) return;
            _viewRoot.localPosition = Vector3.zero;
            if (Finite(_baseOrthographicSize) && _baseOrthographicSize > 0f)
                _camera.orthographicSize = _baseOrthographicSize;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void Awake() => EnsureRig();

        private void OnEnable()
        {
            EnsureRig();
            ApplyView();
        }

        private void LateUpdate()
        {
            if (!_lifetime.IsActive) return;
            if (!ContextAvailable() || !EnsureHudHold()) { CancelView(); return; }
            ApplyView();
        }

        private void OnDisable() => CancelView();

        private void OnDestroy()
        {
            _destroying = true;
            CancelView();
            Transform root = _viewRoot;
            _viewRoot = null;
            if (root == null) return;
            // Component removal must not destroy a surviving camera. Keep its
            // current child-local pose under the current authoritative parent.
            if (_camera != null && _camera.transform.parent == root)
                _camera.transform.SetParent(root.parent, false);
            DestroyOwnedRoot(root.gameObject);
        }

        private static void DestroyOwnedRoot(GameObject root)
        {
            if (Application.isPlaying) Destroy(root);
            else DestroyImmediate(root);
        }
    }
}
