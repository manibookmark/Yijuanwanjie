using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// Owns a stage's facility artwork and its placement on the background.
    /// Rectangles use the background's top-left pixel coordinates; unlock
    /// rules and navigation belong to the scene's progression assembly.
    /// </summary>
    [CreateAssetMenu(fileName = "hub-facility-art",
        menuName = "Wanjie Scroll/Hub/Facility Art")]
    public sealed class HubFacilityArtProfile : ScriptableObject
    {
        public enum HoverPlaybackMode
        {
            Standard = 0,
            SplitEnterExit = 1
        }

        [SerializeField] private string _contentId;
        [SerializeField] private Sprite _background;
        [SerializeField] private Facility[] _facilities = Array.Empty<Facility>();

        public string ContentId => _contentId;
        public Sprite Background => _background;
        public IReadOnlyList<Facility> Facilities => _facilities;

        [Serializable]
        public sealed class Facility
        {
            [SerializeField] private string _registryEntryId;
            [SerializeField] private string _displayName;
            [SerializeField] private Sprite _body;
            [Tooltip("Import grayscale emission with alpha from grayscale.")]
            [SerializeField] private Sprite _emission;
            [Tooltip("Readable grayscale texture; black pixels do not receive hover.")]
            [SerializeField] private Texture2D _hitMask;
            [SerializeField] private Rect _artRect;
            [Tooltip("显示缩放。artRect 尺寸仍锁定导入贴图大小，本值只影响渲染与命中区域，按建筑中心缩放。")]
            [SerializeField, Min(0.05f)] private float _visualScale = 1f;
            [SerializeField] private Color _glowColor =
                new Color(0.55f, 1f, 0.92f, 0.85f);
            [Tooltip("仅在开启后播放 Hover 序列；关闭时固定本体，仅显示发光反馈。")]
            [SerializeField] private bool _animateHoverFrames;
            [Tooltip("Optional Resources folder containing ordered hover frames.")]
            [SerializeField] private string _hoverFramesResourcePath;
            [SerializeField, Min(0.1f)] private float _hoverDuration = 4f;
            [SerializeField] private bool _loopHover = true;
            [Tooltip("SplitEnterExit 播放前半段后停住，移出时播放后半段。")]
            [SerializeField] private HoverPlaybackMode _hoverPlaybackMode;
            [SerializeField, Min(0.1f)] private float _hoverExitDuration = 0.5f;

            public string RegistryEntryId => _registryEntryId;
            public string DisplayName => _displayName;
            public Sprite Body => _body;
            public Sprite Emission => _emission;
            public Texture2D HitMask => _hitMask;
            public Rect ArtRect => _artRect;
            public float VisualScale => Mathf.Max(0.05f, _visualScale);
            public Color GlowColor => _glowColor;
            public float HoverDuration => Mathf.Max(0.1f, _hoverDuration);
            public bool LoopHover => _loopHover;
            public bool AnimateHoverFrames => _animateHoverFrames;
            public HoverPlaybackMode PlaybackMode => _hoverPlaybackMode;
            public float HoverExitDuration =>
                Mathf.Max(0.1f, _hoverExitDuration);
            public bool HasHoverAnimation =>
                _animateHoverFrames &&
                !string.IsNullOrWhiteSpace(_hoverFramesResourcePath);

            public Sprite[] LoadHoverFrames()
            {
                if (!_animateHoverFrames ||
                    string.IsNullOrWhiteSpace(_hoverFramesResourcePath))
                    return Array.Empty<Sprite>();

                Sprite[] frames = Resources.LoadAll<Sprite>(
                    _hoverFramesResourcePath.Trim());
                Array.Sort(frames, (left, right) => string.CompareOrdinal(
                    left != null ? left.name : string.Empty,
                    right != null ? right.name : string.Empty));
                return frames;
            }
        }

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(_contentId) || _background == null ||
                _facilities == null || _facilities.Length == 0)
            {
                error = "Facility art requires a stable ID, background and facilities.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Facility facility in _facilities)
            {
                if (facility == null ||
                    string.IsNullOrWhiteSpace(facility.RegistryEntryId) ||
                    !ids.Add(facility.RegistryEntryId) ||
                    string.IsNullOrWhiteSpace(facility.DisplayName) ||
                    facility.Body == null || facility.Emission == null ||
                    facility.HitMask == null || !facility.HitMask.isReadable)
                {
                    error = "Each facility needs a unique registry ID, name, body, " +
                        "emission and readable hit mask.";
                    return false;
                }

                Rect rect = facility.ArtRect;
                if (rect.width <= 0f || rect.height <= 0f ||
                    rect.xMin < 0f || rect.yMin < 0f ||
                    rect.xMax > _background.rect.width ||
                    rect.yMax > _background.rect.height ||
                    facility.Body.rect.size != rect.size ||
                    facility.Emission.rect.size != rect.size ||
                    facility.HitMask.width != rect.width ||
                    facility.HitMask.height != rect.height)
                {
                    error = $"Facility {facility.RegistryEntryId} has mismatched " +
                        "layer dimensions or lies outside its background.";
                    return false;
                }

                Sprite[] hoverFrames = facility.LoadHoverFrames();
                if (facility.HasHoverAnimation)
                {
                    if (hoverFrames.Length < 2)
                    {
                        error = $"Facility {facility.RegistryEntryId} needs at " +
                            "least two hover frames.";
                        return false;
                    }

                    foreach (Sprite frame in hoverFrames)
                    {
                        if (frame == null || frame.rect.size != rect.size)
                        {
                            error = $"Facility {facility.RegistryEntryId} has a " +
                                "hover frame whose dimensions do not match its art rect.";
                            return false;
                        }
                    }
                }
            }

            error = null;
            return true;
        }
    }
}
