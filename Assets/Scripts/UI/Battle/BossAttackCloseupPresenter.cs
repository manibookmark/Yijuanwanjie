using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// Owns one close-up playback channel: prepared Sprite slices, the external
    /// temporary stage, round identity, interruption and source-audio intent.
    /// It exclusively leases the source body's forceRenderingOff channel;
    /// no other body property, formation or entrance is changed. A typed lease
    /// requests one shared world-camera view; the camera owner also holds HUD
    /// projection stable. The original battlefield is never replaced.
    /// Disabling explicitly hides/releases the stage and slices; callbacks are
    /// invalidated before cleanup and receive interruption instead of fake hits.
    /// </summary>
    public sealed class BossAttackCloseupPresenter : MonoBehaviour
    {
        private sealed class Round
        {
            public int Version;
            public Coroutine Routine;
            public GameObject Root;
            public SpriteRenderer SourceRenderer;
            public bool SourceForceRenderingOffBefore;
            public bool SourceVisibilityOwned;
            public BattleCameraViewOwner.ViewLease View;
            public SpriteRenderer Actor;
            public SpriteRenderer Dim;
            public Sprite DimSprite;
            public SpriteRenderer LeftFootContact;
            public SpriteRenderer RightFootContact;
            public Sprite ContactSprite;
            public Texture2D ContactTexture;
            public Vector3 BackgroundPosition;
            public float NormalHeightViewport;
            public Vector3 WorldCenter;
            public Quaternion WorldRotation;
            public float WorldHeight;
            public Action Impact;
            public Action Complete;
            public Action Interrupted;
            public bool ImpactDelivered;
            public double StartedAt;
        }

        private BossAttackCloseupProfile _profile;
        private Camera _camera;
        private BattleCameraViewOwner _viewOwner;
        private SpriteRenderer _background;
        private Action<BossAudioRequest> _audioSink;
        private readonly List<Sprite> _ownedSprites = new List<Sprite>();
        private Sprite[] _frames;
        private AudioClip _audio;
        private Coroutine _preparation;
        private int _bindingVersion;
        private int _playVersion;
        private bool _ready;
        private bool _destroying;
        private Round _round;
        private long _audioOwnerKey;
        private long _audioGeneration;
        private BossAudioRequest? _audioIntent;
        private double _audioStartedAt;

        public bool IsReady => _ready;
        public bool IsPlaying => _round != null;
        public bool HasSourceAudio => _ready && _audio != null;
        public string ContentId => _profile != null ? _profile.contentId : null;

        /// <summary>Read-only contact geometry for the current source frame.
        /// Effects are assembled by Bootstrap; UI never calls Effects directly.</summary>
        public bool TryGetImpactAnchorWorld(out Vector3 world)
        {
            world = default;
            Round round = _round;
            if (round == null || !IsCurrent(round) || _profile == null ||
                _frames == null || _frames.Length == 0 || round.View == null || !round.View.IsCurrent)
                return false;
            float aspect = _frames[0].rect.width / _frames[0].rect.height;
            Vector2 uv = _profile.impactAnchorSourceUv;
            world = round.WorldCenter + round.WorldRotation * new Vector3(
                (uv.x - 0.5f) * round.WorldHeight * aspect,
                (uv.y - 0.5f) * round.WorldHeight, 0f);
            return true;
        }

        public void Bind(BossAttackCloseupProfile profile, Camera camera,
            SpriteRenderer background, Action<BossAudioRequest> sink)
        {
            if (_destroying) return;
            int expected = unchecked(_bindingVersion + 1);
            ReleasePrepared();
            if (_bindingVersion != expected) return;
            _profile = profile;
            _camera = camera;
            _viewOwner = camera != null ? camera.GetComponent<BattleCameraViewOwner>() : null;
            _background = background;
            _audioSink = sink;
            BeginPreparation();
        }

        public bool CanPlay(string actionId)
        {
            SpriteRenderer source = GetComponent<SpriteRenderer>();
            bool sourceAvailable = source != null && source.sprite != null &&
                (!source.forceRenderingOff || (_round != null && _round.SourceVisibilityOwned &&
                 _round.SourceRenderer == source));
            return sourceAvailable && isActiveAndEnabled && _ready && _profile != null &&
                   _camera != null && _camera.isActiveAndEnabled && _camera.orthographic &&
                   _viewOwner != null && _viewOwner.isActiveAndEnabled &&
                   (!_viewOwner.IsViewActive || (_round != null && _round.View != null && _round.View.IsCurrent)) &&
                   _background != null && _background.sprite != null &&
                   string.Equals(_profile.actionId, actionId, StringComparison.OrdinalIgnoreCase);
        }

        public bool TryPlay(string actionId, Action onImpact, Action onComplete,
            Action onInterrupted)
        {
            if (!CanPlay(actionId)) return false;
            int expected = unchecked(_playVersion + 1);
            Cancel();
            // An old interruption listener may have started a newer round/binding.
            if (_playVersion != expected || _round != null || !CanPlay(actionId))
            {
                InvokeSafely(onInterrupted);
                return true;
            }
            var round = new Round
            {
                Version = _playVersion, Impact = onImpact, Complete = onComplete,
                Interrupted = onInterrupted, StartedAt = Time.unscaledTimeAsDouble
            };
            _round = round;
            try
            {
                CaptureNormalPose(round);
                BuildStage(round);
                float sourceAspect = _frames[0].rect.width / _frames[0].rect.height;
                float nearScale = _profile.closeupCanvasScale * Mathf.Max(1f, _camera.aspect / sourceAspect);
                float zoom = nearScale / round.NormalHeightViewport;
                if (!_viewOwner.TryAcquire(round.WorldCenter, zoom, out round.View))
                    throw new InvalidOperationException("World-camera view is unavailable.");
                if (!Render(round, 0f, 0))
                    throw new InvalidOperationException("World-camera view was interrupted during preparation.");
                SuppressSourceBody(round);
            }
            catch (Exception error)
            {
                // Retire this candidate after a native construction failure.
                // Core may retry the uncommitted action through its old clip.
                _ready = false;
                Debug.LogException(error);
                Cancel();
                return true;
            }
            StartAudio();
            if (!IsCurrent(round)) return true;
            Coroutine routine = StartCoroutine(Play(round));
            if (IsCurrent(round)) round.Routine = routine;
            return true;
        }

        public void Cancel()
        {
            unchecked { _playVersion++; }
            Round round = _round;
            _round = null;
            if (round == null) return;
            Action callback = round.Interrupted;
            ClearCallbacks(round);
            CleanupStage(round, stopRoutine: true);
            StopAudio();
            InvokeSafely(callback);
        }

        public void ReplayAudioRequestTo(Action<BossAudioRequest> sink)
        {
            if (sink == null || !_audioIntent.HasValue || !isActiveAndEnabled || !IsPlaying) return;
            BossAudioRequest previous = _audioIntent.Value;
            float offset = (float)Math.Max(0d, Time.unscaledTimeAsDouble - _audioStartedAt);
            if (previous.Clip == null || offset >= previous.Clip.length) return;
            var replay = new BossAudioRequest(_audioOwnerKey, ++_audioGeneration,
                BossAudioRequestKind.Play, previous.Clip, false, previous.Volume, 1f, offset);
            _audioIntent = replay;
            DeliverAudio(sink, replay);
        }

        private void BeginPreparation()
        {
            if (_destroying || !isActiveAndEnabled || _profile == null || _preparation != null) return;
            if (!_profile.TryValidate(out string error))
            {
                Debug.LogWarning("Boss close-up candidate is invalid: " + error);
                return;
            }
            _preparation = StartCoroutine(Prepare(_bindingVersion));
        }

        private IEnumerator Prepare(int version)
        {
            // Never create native Sprite objects in OnEnable's activation stack.
            yield return null;
            if (!CurrentBinding(version)) yield break;
            var frames = new Sprite[_profile.frameCount];
            int perPage = _profile.atlasColumns * _profile.atlasRows;
            int frameWidth = 0, frameHeight = 0;
            for (int page = 0; page * perPage < frames.Length; page++)
            {
                ResourceRequest request = Resources.LoadAsync<Texture2D>(_profile.frameAtlasResourcePaths[page]);
                yield return request;
                if (!CurrentBinding(version)) yield break;
                Texture2D texture = request.asset as Texture2D;
                if (texture == null || texture.width % _profile.atlasColumns != 0 ||
                    texture.height % _profile.atlasRows != 0)
                {
                    FailPreparation("missing or non-divisible atlas page", version);
                    yield break;
                }
                int width = texture.width / _profile.atlasColumns;
                int height = texture.height / _profile.atlasRows;
                if (page > 0 && (width != frameWidth || height != frameHeight))
                {
                    FailPreparation("atlas pages have different source canvases", version);
                    yield break;
                }
                frameWidth = width;
                frameHeight = height;
                yield return null;
                if (!CurrentBinding(version)) yield break;
                for (int cell = 0; cell < perPage && page * perPage + cell < frames.Length; cell++)
                {
                    var rect = new Rect((cell % _profile.atlasColumns) * width,
                        texture.height - (cell / _profile.atlasColumns + 1) * height, width, height);
                    Sprite sprite = CreateSlice(texture, rect, height);
                    if (sprite == null)
                    {
                        FailPreparation("could not create source-frame slice", version);
                        yield break;
                    }
                    _ownedSprites.Add(sprite);
                    frames[page * perPage + cell] = sprite;
                    if ((cell + 1) % 4 == 0)
                    {
                        yield return null;
                        if (!CurrentBinding(version)) yield break;
                    }
                }
            }
            ResourceRequest audioRequest = Resources.LoadAsync<AudioClip>(_profile.audioResourcePath);
            yield return audioRequest;
            if (!CurrentBinding(version)) yield break;
            AudioClip audio = audioRequest.asset as AudioClip;
            if (audio == null)
            {
                FailPreparation("missing native source audio", version);
                yield break;
            }
            if (audio.loadState == AudioDataLoadState.Unloaded) audio.LoadAudioData();
            while (audio.loadState == AudioDataLoadState.Loading)
            {
                yield return null;
                if (!CurrentBinding(version)) yield break;
            }
            if (audio.loadState != AudioDataLoadState.Loaded)
            {
                FailPreparation("native source audio failed to load", version);
                yield break;
            }
            _frames = frames;
            _audio = audio;
            _ready = true;
            _preparation = null;
        }

        private static Sprite CreateSlice(Texture2D texture, Rect rect, int height)
        {
            try { return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), height, 0, SpriteMeshType.FullRect); }
            catch (Exception error) { Debug.LogException(error); return null; }
        }

        private bool CurrentBinding(int version)
            => !_destroying && isActiveAndEnabled && version == _bindingVersion;

        private void FailPreparation(string error, int version)
        {
            if (!CurrentBinding(version)) return;
            _preparation = null;
            _ready = false;
            DestroySlices();
            Debug.LogWarning("Boss close-up preparation failed; ordinary animation remains available: " + error);
        }

        private void CaptureNormalPose(Round round)
        {
            // This presenter is attached to the real Boss SpriteRenderer. Read
            // its full-canvas bounds after the source-matched Idle is applied;
            // do not infer a starting size/position from a screen constant.
            SpriteRenderer source = GetComponent<SpriteRenderer>();
            if (source == null || source.sprite == null)
                throw new InvalidOperationException("Boss close-up has no source renderer.");
            round.SourceRenderer = source;
            Bounds bounds = source.bounds;
            Vector3 center = _camera.WorldToViewportPoint(bounds.center);
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + new Vector3(
                    (corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                    (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                    (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                float y = _camera.WorldToViewportPoint(point).y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            float height = maxY - minY;
            if (center.z <= 0f || float.IsNaN(height) || float.IsInfinity(height) || height <= 0.0001f)
                throw new InvalidOperationException("Boss close-up source canvas is not visible to its camera.");
            round.NormalHeightViewport = height;
            round.WorldCenter = bounds.center;
            round.WorldRotation = _camera.transform.rotation;
            round.WorldHeight = height * _camera.orthographicSize * 2f;
        }

        private void BuildStage(Round round)
        {
            round.Root = new GameObject("BossAttackCloseupStage");
            SceneManager.MoveGameObjectToScene(round.Root, gameObject.scene);
            round.Root.layer = _background.gameObject.layer;
            // All actual world layers share the existing camera. This stage
            // contains only the source motion and temporary feedback.
            round.BackgroundPosition = _background.transform.position;
            round.Actor = CreateRenderer(round.Root.transform, "SourceMotion", _profile.actorSortingOrder);
            round.Dim = CreateRenderer(round.Root.transform, "BackdropDim", _profile.backgroundSortingOrder + 1);
            round.DimSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
            round.Dim.sprite = round.DimSprite;
            BuildFootContacts(round);
        }

        private void SuppressSourceBody(Round round)
        {
            SpriteRenderer source = round.SourceRenderer;
            if (source == null) throw new InvalidOperationException("Boss source renderer was withdrawn.");
            round.SourceForceRenderingOffBefore = source.forceRenderingOff;
            if (round.SourceForceRenderingOffBefore)
                throw new InvalidOperationException("Boss source renderer is already hidden by another owner.");
            round.SourceVisibilityOwned = true;
            source.forceRenderingOff = true;
        }

        private static void RestoreSourceBody(Round round)
        {
            if (!round.SourceVisibilityOwned) return;
            // Retire the lease BEFORE writing the old flag: repeated cleanup
            // or late callbacks cannot restore over a newer playback round.
            round.SourceVisibilityOwned = false;
            SpriteRenderer source = round.SourceRenderer;
            round.SourceRenderer = null;
            if (source != null) source.forceRenderingOff = round.SourceForceRenderingOffBefore;
        }

        private void BuildFootContacts(Round round)
        {
            // Small grayscale data mask, matching the preview's two soft
            // contacts; this is not a new character/reference illustration.
            const int width = 64, height = 32;
            round.ContactTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            round.ContactTexture.name = "BossCloseupContactMask";
            round.ContactTexture.wrapMode = TextureWrapMode.Clamp;
            round.ContactTexture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float dx = (x + 0.5f) / width * 2f - 1f;
                    float dy = (y + 0.5f) / height * 2f - 1f;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, radius));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            round.ContactTexture.SetPixels32(pixels);
            round.ContactTexture.Apply(false, true);
            round.ContactSprite = Sprite.Create(round.ContactTexture,
                new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), height, 0, SpriteMeshType.FullRect);
            round.LeftFootContact = CreateRenderer(round.Root.transform, "LeftFootContact", _profile.backgroundSortingOrder + 1);
            round.RightFootContact = CreateRenderer(round.Root.transform, "RightFootContact", _profile.backgroundSortingOrder + 1);
            round.LeftFootContact.sprite = round.ContactSprite;
            round.RightFootContact.sprite = round.ContactSprite;
        }

        private SpriteRenderer CreateRenderer(Transform parent, string label, int order)
        {
            var child = new GameObject(label, typeof(SpriteRenderer));
            child.layer = _background.gameObject.layer;
            child.transform.SetParent(parent, false);
            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            renderer.sortingLayerID = _background.sortingLayerID;
            renderer.sortingOrder = order;
            renderer.sharedMaterial = _background.sharedMaterial;
            return renderer;
        }

        private IEnumerator Play(Round round)
        {
            float duration = _profile.Duration;
            while (IsCurrent(round))
            {
                if (_camera == null || !_camera.isActiveAndEnabled || _background == null)
                {
                    Cancel();
                    yield break;
                }
                float elapsed = (float)(Time.unscaledTimeAsDouble - round.StartedAt);
                float sourceFrame = elapsed * _profile.framesPerSecond;
                if (!Render(round, sourceFrame, Mathf.Clamp(Mathf.FloorToInt(sourceFrame), 0, _frames.Length - 1)))
                {
                    Cancel();
                    yield break;
                }
                if (!round.ImpactDelivered && sourceFrame >= _profile.hitFrame)
                {
                    round.ImpactDelivered = true;
                    try { round.Impact?.Invoke(); }
                    catch (Exception error) { Debug.LogException(error); if (IsCurrent(round)) Cancel(); }
                    if (!IsCurrent(round)) yield break;
                }
                if (elapsed >= duration)
                {
                    Action callback = round.Complete;
                    _round = null;
                    unchecked { _playVersion++; }
                    ClearCallbacks(round);
                    CleanupStage(round, stopRoutine: false);
                    StopAudio();
                    InvokeSafely(callback);
                    yield break;
                }
                yield return null;
            }
        }

        private bool IsCurrent(Round round) => ReferenceEquals(_round, round) && round.Version == _playVersion;

        private bool Render(Round round, float frame, int frameIndex)
        {
            float weight = _profile.GetCloseupWeight(frame);
            if (round.View == null || !round.View.SetProgress(weight)) return false;
            float height = _camera.orthographicSize * 2f;
            float width = height * _camera.aspect;
            float aspect = _frames[frameIndex].rect.width / _frames[frameIndex].rect.height;
            float depth = Mathf.Max(_camera.nearClipPlane + 0.1f,
                Vector3.Dot(round.BackgroundPosition - _camera.transform.position, _camera.transform.forward));

            // Keep source-frame geometry fixed in WORLD space. The single
            // camera now enlarges background, grid and Boss together, rather
            // than scaling only this actor over a stationary battlefield.
            round.Actor.sprite = _frames[frameIndex];
            round.Actor.transform.SetPositionAndRotation(round.WorldCenter, round.WorldRotation);
            round.Actor.transform.localScale = Vector3.one * round.WorldHeight;
            round.Dim.transform.SetPositionAndRotation(_camera.ViewportToWorldPoint(
                new Vector3(0.5f, 0.5f, depth)), _camera.transform.rotation);
            round.Dim.transform.localScale = new Vector3(width, height, 1f);
            round.Dim.color = new Color(0.03f, 0.075f, 0.11f, _profile.backgroundDim * weight);
            RenderFootContact(round.LeftFootContact, -40f / (1920f * 0.54f),
                round, weight, aspect);
            RenderFootContact(round.RightFootContact, 38f / (1920f * 0.54f),
                round, weight, aspect);
            return true;
        }

        private void RenderFootContact(SpriteRenderer contact, float xInSourceCanvas,
            Round round, float weight, float aspect)
        {
            Vector3 offset = new Vector3(xInSourceCanvas * round.WorldHeight * aspect,
                (0.5f - _profile.sourceFootY) * round.WorldHeight, 0f);
            contact.transform.SetPositionAndRotation(round.WorldCenter + round.WorldRotation * offset,
                round.WorldRotation);
            contact.transform.localScale = new Vector3(
                round.WorldHeight * aspect * (54f / (1920f * 0.54f)) * 0.5f,
                round.WorldHeight * (13f / (1080f * 0.54f)), 1f);
            contact.color = new Color(0.02f, 0.05f, 0.08f, 0.57f * (1f - weight));
        }

        private void StartAudio()
        {
            if (_audioOwnerKey == 0) _audioOwnerKey = BossAudioOwnerIdentity.Allocate();
            _audioStartedAt = Time.unscaledTimeAsDouble;
            var request = new BossAudioRequest(_audioOwnerKey, ++_audioGeneration,
                BossAudioRequestKind.Play, _audio, false, _profile.audioVolume, 1f);
            _audioIntent = request;
            DeliverAudio(_audioSink, request);
        }

        private void StopAudio()
        {
            _audioIntent = null;
            if (_audioOwnerKey != 0)
                DeliverAudio(_audioSink, new BossAudioRequest(_audioOwnerKey, ++_audioGeneration, BossAudioRequestKind.Stop));
        }

        private static void DeliverAudio(Action<BossAudioRequest> sink, BossAudioRequest request)
        {
            try { sink?.Invoke(request); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private static void ClearCallbacks(Round round)
        {
            round.Impact = null;
            round.Complete = null;
            round.Interrupted = null;
        }

        private void CleanupStage(Round round, bool stopRoutine)
        {
            Coroutine routine = round.Routine;
            round.Routine = null;
            if (stopRoutine && routine != null) StopCoroutine(routine);
            if (round.Root != null)
            {
                round.Root.SetActive(false);
                Destroy(round.Root);
                round.Root = null;
            }
            BattleCameraViewOwner.ViewLease view = round.View;
            round.View = null;
            view?.Dispose();
            RestoreSourceBody(round);
            round.SourceRenderer = null;
            if (round.DimSprite != null) { Destroy(round.DimSprite); round.DimSprite = null; }
            if (round.ContactSprite != null) { Destroy(round.ContactSprite); round.ContactSprite = null; }
            if (round.ContactTexture != null) { Destroy(round.ContactTexture); round.ContactTexture = null; }
        }

        private void ReleasePrepared()
        {
            unchecked { _bindingVersion++; }
            _ready = false;
            Coroutine preparation = _preparation;
            _preparation = null;
            if (preparation != null) StopCoroutine(preparation);
            Cancel();
            DestroySlices();
            _audio = null;
            // Textures/audio came from Resources and may have other consumers;
            // release references, never Destroy/Unload shared native assets here.
        }

        private void DestroySlices()
        {
            _frames = null;
            foreach (Sprite sprite in _ownedSprites) if (sprite != null) Destroy(sprite);
            _ownedSprites.Clear();
        }

        private static void InvokeSafely(Action callback)
        {
            try { callback?.Invoke(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private void OnEnable() => BeginPreparation();
        private void OnDisable() => ReleasePrepared();
        private void OnDestroy()
        {
            _destroying = true;
            ReleasePrepared();
            _profile = null;
            _camera = null;
            _viewOwner = null;
            _background = null;
            _audioSink = null;
        }
    }
}
