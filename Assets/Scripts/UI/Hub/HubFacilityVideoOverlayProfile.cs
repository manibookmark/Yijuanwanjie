using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// Runtime authoring source for the video-based Hub facilities. Coordinates
    /// are fixed in the shared 1920x1080 art space and masks/materials travel
    /// with the corresponding video entry.
    /// </summary>
    [CreateAssetMenu(fileName = "hub-video-facility-art",
        menuName = "Wanjie Scroll/Hub/Video Facility Art")]
    public sealed class HubFacilityVideoOverlayProfile : ScriptableObject
    {
        public const float DefaultCanvasWidth = 1920f;
        public const float DefaultCanvasHeight = 1080f;

        [SerializeField] private string _contentId;
        [SerializeField] private Texture2D _background;
        [SerializeField] private Vector2 _canvasSize =
            new Vector2(DefaultCanvasWidth, DefaultCanvasHeight);
        [SerializeField] private Facility[] _facilities = Array.Empty<Facility>();

        public string ContentId => _contentId;
        public Texture2D Background => _background;
        public Vector2 CanvasSize => _canvasSize;
        public IReadOnlyList<Facility> Facilities => _facilities;

        [Serializable]
        public sealed class Facility
        {
            [SerializeField] private string _registryEntryId;
            [SerializeField] private string _displayName;
            [SerializeField] private VideoClip _video;
            [SerializeField] private Material _material;
            [SerializeField] private Texture2D _hitMask;
            [SerializeField] private Rect _artRect;
            [SerializeField, Min(0.05f)] private float _playbackSpeed = 1f;
            [SerializeField, Range(0f, 1f)] private float _audioVolume;

            public string RegistryEntryId => _registryEntryId;
            public string DisplayName => _displayName;
            public VideoClip Video => _video;
            public Material Material => _material;
            public Texture2D HitMask => _hitMask;
            public Rect ArtRect => _artRect;
            public float PlaybackSpeed => Mathf.Max(0.05f, _playbackSpeed);
            public float AudioVolume => Mathf.Clamp01(_audioVolume);
        }

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(_contentId) || _background == null ||
                _canvasSize.x <= 0f || _canvasSize.y <= 0f ||
                _facilities == null || _facilities.Length == 0)
            {
                error = "Video facility art requires a stable ID, background, canvas size and facilities.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Facility facility in _facilities)
            {
                if (facility == null ||
                    string.IsNullOrWhiteSpace(facility.RegistryEntryId) ||
                    !ids.Add(facility.RegistryEntryId) ||
                    string.IsNullOrWhiteSpace(facility.DisplayName) ||
                    facility.Video == null || facility.Material == null ||
                    facility.HitMask == null)
                {
                    error = "Each video facility needs a unique registry ID, name, video, material and mask.";
                    return false;
                }

                Rect rect = facility.ArtRect;
                // The authored preview scene intentionally keeps the original
                // RectTransform offsets. Those offsets can place part of an
                // art rect outside the shared stage while still producing the
                // exact preview composition, so only reject invalid sizes here.
                if (rect.width <= 0f || rect.height <= 0f)
                {
                    error = $"Facility {facility.RegistryEntryId} has an invalid art rectangle.";
                    return false;
                }
            }

            error = null;
            return true;
        }
    }
}
