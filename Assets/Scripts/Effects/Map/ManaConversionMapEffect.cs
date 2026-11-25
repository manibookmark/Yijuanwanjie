using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WanjieScroll.Effects.Map
{
    /// <summary>
    /// Owns the shared one-use mana platform and its ready / spent lifecycle.
    /// The existing mana spirit exclusively owns its child motion and Infinity
    /// systems, including delayed native activation. No cards or conversion rules
    /// are embedded in this visual, and no new Infinity asset is generated.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ManaConversionMapEffect : MonoBehaviour,
        IMapNodeStateVisual, IMapNodeInteractionAnchor, IMapNodePickupStateVisual
    {
        private static readonly Color Paper = new Color(0.84f, 0.87f, 0.77f, 1f);
        private static readonly Color PaperEdge = new Color(0.34f, 0.51f, 0.43f, 1f);
        private static readonly Color Inlay = new Color(0.38f, 0.86f, 0.76f, 0.90f);

        private readonly List<Mesh> _meshes = new List<Mesh>(2);
        private readonly List<MeshRenderer> _paperLayers = new List<MeshRenderer>(2);
        private MaterialPropertyBlock _tint;
        private ManaSpiritInfinityEffect _spirit;
        private Transform _spiritAnchor;
        private LineRenderer _inlay;
        private Material _material;
        private bool _initialized;
        private bool _pickupAvailable;
        private bool _pickupArrivalPending;

        public MapVfxState State { get; private set; } = MapVfxState.Hidden;
        public bool RetainsCompletedVisual => false;

        public void Initialize(int sortingOrder, MapVfxState initialState)
        {
            if (!_initialized)
            {
                BuildDais(sortingOrder);
                var spiritObject = new GameObject("GatheredMana");
                spiritObject.transform.SetParent(transform, false);
                spiritObject.transform.localPosition = new Vector3(0f, 0.24f, 0f);
                _spiritAnchor = spiritObject.transform;
                // ManaSpirit owns a separate motion root so it cannot overwrite
                // the map-authored facility scale or the fixed paper platform.
                var motion = new GameObject("ManaMotion");
                motion.transform.SetParent(_spiritAnchor, false);
                _spiritAnchor.localScale = Vector3.one * 0.78f;
                _spirit = motion.AddComponent<ManaSpiritInfinityEffect>();
                if (!_spirit.Initialize(sortingOrder + 3))
                    throw new InvalidOperationException("聚灵台缺少灵气表现资源。");
                _initialized = true;
            }
            SetState(initialState, true);
        }

        public void SetState(MapVfxState state, bool immediate = true)
        {
            if (!_initialized)
                return;
            State = state;
            ApplyDaisState();
            // Keep the Infinity hierarchy active. Its own readiness owner
            // applies Stop / Play only after activation has stabilized.
            if (_pickupArrivalPending && _pickupAvailable)
            {
                _pickupArrivalPending = false;
                _spirit.PlayRewardArrival();
            }
            else _spirit.SetState(state, immediate);
        }

        public void SetPickupAvailable(bool available)
        {
            _pickupArrivalPending |= available && !_pickupAvailable;
            _pickupAvailable = available;
        }

        public bool PlayCompletion(Action onComplete = null)
        {
            if (!_initialized || State == MapVfxState.Completing ||
                State == MapVfxState.Completed)
                return false;
            State = MapVfxState.Completing;
            ApplyDaisState();
            bool started = _spirit.PlayCollection(() =>
            {
                if (this == null)
                    return;
                SetState(MapVfxState.Completed);
                onComplete?.Invoke();
            });
            if (!started)
                SetState(MapVfxState.Available);
            return started;
        }

        public bool TryGetInteractionWorldAnchor(out Vector3 worldPosition)
        {
            worldPosition = _spiritAnchor != null ? _spiritAnchor.position : transform.position;
            return _initialized && isActiveAndEnabled &&
                State != MapVfxState.Hidden && State != MapVfxState.Completed;
        }

        private void BuildDais(int sortingOrder)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                throw new InvalidOperationException("聚灵台缺少通用透明 Shader。");
            // MaterialPropertyBlock allocates native engine state. Component
            // field initializers can run during serialization, before creation
            // is allowed; the explicit map initialization owns that allocation.
            _tint = new MaterialPropertyBlock();
            _material = new Material(shader)
            {
                name = "ManaConversionPaper_Runtime",
                hideFlags = HideFlags.HideAndDontSave
            };
            CreatePaperLayer("FoldedPaperEdge", -0.37f, 0.64f, 0.18f, sortingOrder);
            CreatePaperLayer("PaperDais", -0.29f, 0.61f, 0.17f, sortingOrder + 1);

            var lineObject = new GameObject("ManaInlay");
            lineObject.transform.SetParent(transform, false);
            _inlay = lineObject.AddComponent<LineRenderer>();
            _inlay.useWorldSpace = false;
            _inlay.loop = true;
            _inlay.positionCount = 8;
            _inlay.widthMultiplier = 0.018f;
            _inlay.sharedMaterial = _material;
            _inlay.sortingOrder = sortingOrder + 2;
            _inlay.shadowCastingMode = ShadowCastingMode.Off;
            _inlay.receiveShadows = false;
            for (int i = 0; i < 8; i++)
            {
                float angle = Mathf.PI * 2f * i / 8f;
                _inlay.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * 0.44f,
                    -0.28f + Mathf.Sin(angle) * 0.115f, 0f));
            }
        }

        private void CreatePaperLayer(string label, float centreY,
            float halfWidth, float halfHeight, int sortingOrder)
        {
            var layer = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(transform, false);
            var vertices = new Vector3[9];
            var colors = new Color[9];
            var triangles = new int[24];
            vertices[0] = new Vector3(0f, centreY, 0f);
            colors[0] = Color.white;
            for (int i = 0; i < 8; i++)
            {
                float angle = Mathf.PI * 2f * i / 8f;
                vertices[i + 1] = new Vector3(
                    Mathf.Cos(angle) * halfWidth,
                    centreY + Mathf.Sin(angle) * halfHeight, 0f);
                colors[i + 1] = Color.white;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % 8 + 1;
            }
            var mesh = new Mesh { name = label + "_Runtime", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            layer.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _paperLayers.Add(renderer);
        }

        private void ApplyDaisState()
        {
            bool visible = State != MapVfxState.Hidden && State != MapVfxState.Completed;
            bool muted = State == MapVfxState.Locked;
            for (int i = 0; i < _paperLayers.Count; i++)
            {
                MeshRenderer renderer = _paperLayers[i];
                renderer.enabled = visible;
                Color color = i == 0 ? PaperEdge : Paper;
                if (muted) color = Color.Lerp(color, new Color(0.35f, 0.38f, 0.35f, 0.55f), 0.55f);
                _tint.SetColor("_Color", color);
                renderer.SetPropertyBlock(_tint);
            }
            _inlay.enabled = visible;
            Color line = muted ? new Color(0.30f, 0.44f, 0.39f, 0.35f) : Inlay;
            _inlay.startColor = _inlay.endColor = line;
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in _meshes)
                if (mesh != null) Destroy(mesh);
            if (_material != null) Destroy(_material);
        }
    }
}
