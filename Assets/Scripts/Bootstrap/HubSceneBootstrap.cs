#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.Progression;
using WanjieScroll.UI.Hub;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Assembles the progression-selected Hub artwork, interactive Wanjie
    /// formation and its transfer presentation. Placement uses the shared
    /// 1920×1080 art coordinate system; visual algorithms stay inside UI and
    /// Effects modules. This assembly file exceeds the 500-line review
    /// threshold; facility placement and hover state remain owned by the
    /// art profile and HubFacilityArtwork, not by this scene bootstrap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HubSceneBootstrap : SceneBootstrapBase
    {
        private const string StageBackgroundPath =
            "Sprites/Hub/stage-01-bg";
        private const string G1FacilityArtPath =
            "Content/Hub/hub-g1-facility-art";
        private const string VideoFacilityArtPath =
            "Content/Hub/hub-video-facility-art";
        private const string PortalPresentationPath =
            "Content/Hub/hub-portal-presentation";
        private const string TianluPresentationPath =
            "Content/Hub/hub-tianlu-presentation";
        private const string ArrivalPresentationPath =
            HubArrivalPresentationProfile.ResourcesPath;
        private const string WorldCatalogPath =
            "Content/Hub/hub-world-catalog";
        private const string TianluRegistryCatalogPath =
            "Content/Hub/tianlu-registry-catalog";
        // The logical Hub stage is a product contract, not an Inspector-tunable
        // ratio: all content-bearing artwork stays inside a 16:9 core.
        private const float CoreStageAspect = 16f / 9f;
        // 兜底宽高比与序列帧 (1240×370) 一致；正常路径运行时从当前帧读取。
        private const float TianluFallbackAspect = 1240f / 370f;

        [Header("Stage 1 (optional Inspector overrides)")]
        [SerializeField] private Sprite _stageBackground;
        [Tooltip("播放时直接修改该配置资产，参数会实时生效并持久保存。")]
        [SerializeField] private HubPortalPresentationProfile _portalPresentation;
        [Tooltip("播放时直接修改该配置资产，参数会实时生效并持久保存。")]
        [SerializeField] private HubTianluPresentationProfile _tianluPresentation;
        [Tooltip("首次返回神域时的卷灵与设施显化时间线；Play 中可重播调节。")]
        [SerializeField] private HubArrivalPresentationProfile _arrivalPresentation;
        [Tooltip("阶段建筑美术配置。播放时直接修改该资产，位置/显示缩放实时生效并持久保存；留空则按 G1 通关进度加载默认配置。")]
        [SerializeField] private HubFacilityArtProfile _facilityArt;
        [Tooltip("固定的仙域视频遮罩与位置配置；存在时作为正式神域背景和建筑层。")]
        [SerializeField] private HubFacilityVideoOverlayProfile _videoFacilityArt;

        private HubBaguaPortalGraphic _portal;
        private RectTransform _portalRect;
        private HubPortalTransferEffect _transferEffect;
        private HubWorldSelectionPanel _worldSelection;
        private HubWorldCatalog _worldCatalog;
        private HubPortalPresentationProfile _activePresentation;
        private HubPortalPresentationProfile _runtimeFallbackPresentation;
        private RectTransform _tianluRect;
        private Image _tianluImage;
        private HubTianluHoverAnim _tianluAnim;
        private HubTianluPresentationProfile _runtimeFallbackTianluPresentation;
        private TianluRegistryPanel _tianluRegistryPanel;
        private WanbaogeRelicPanel _wanbaogeRelicPanel;
        private TianjiArcanaPanel _tianjiArcanaPanel;
        private YinglingHallPanel _yinglingHallPanel;
        private HubFacilityArtwork _yinglingHallArtwork;
        private HubArrivalPresentationProfile _runtimeFallbackArrivalPresentation;
        private HubArrivalPresentationCoordinator _arrivalCoordinator;
        private HubRealmRestorationEffect _arrivalRestorationEffect;
        private HubFacilityArtProfile _activeFacilityArt;
        private HubFacilityVideoOverlayProfile _activeVideoFacilityArt;
        private readonly System.Collections.Generic.List<HubFacilityArtwork>
            _facilityArtworks = new System.Collections.Generic.List<HubFacilityArtwork>();
        private readonly System.Collections.Generic.List<HubFacilityVideoArtwork>
            _videoFacilityArtworks = new System.Collections.Generic.List<HubFacilityVideoArtwork>();
        private int _videoFacilityExpectedReady;
        private int _videoFacilityReady;
        // 当前阶段背景的像素尺寸（艺术坐标），供建筑锚点换算。
        private Vector2 _facilityCanvasSize;
        // 所有仙域核心视觉共用的固定 16:9 舞台。
        private RectTransform _stageArtRect;
        // 宽屏扩展只占核心舞台两侧；核心背景本身始终独立保持 16:9。
        private Texture2D _stageExtensionTexture;
        private RectTransform _stageExtensionLeftRect;
        private RectTransform _stageExtensionRightRect;
        private RawImage _stageExtensionLeftImage;
        private RawImage _stageExtensionRightImage;
#if UNITY_EDITOR
        private bool _quickPlayPanelExpanded = true;
        private bool _quickPlayPresetInitialized;
        private int _quickPlayPresetIndex;

        private static readonly string[] QuickPlayPresetLabels =
        {
            "S0 · 全新账号（教程/首启）",
            "S1 · 教程通关（G1 入口）",
            "S2 · G1 通关 / G2 关前（群英殿/道藏）",
            "专测 · G2F1＋观星设施（非自然进度）",
            "S3 · G2F4 完成 / G2F5 关前（未领终局奖励）",
            "S4 · G2 通关后（白龙马/观星台）"
        };
#endif

        public bool PortalPreviewReady =>
            Application.isPlaying &&
            _portal != null &&
            _transferEffect != null &&
            _worldSelection != null &&
            (_arrivalCoordinator == null || !_arrivalCoordinator.IsPlaying);

        public bool ArrivalPreviewReady =>
            Application.isPlaying &&
            _arrivalCoordinator != null &&
            !_arrivalCoordinator.IsPlaying &&
            _portal != null &&
            _portal.State == HubBaguaPortalGraphic.PortalState.Idle;

        private void LateUpdate()
        {
            // Runtime-created visuals keep following Inspector changes while
            // playing, so alignment can be tuned against the actual backdrop.
            ApplyStageExtensionLayout();
            ApplyPortalPresentation();
            ApplyTianluPlacement();
            ApplyTianluAnimation();
            ApplyFacilityPlacement();
        }

        /// <summary>
        /// 建筑每帧从阶段配置资产重读位置与显示缩放：播放中直接在
        /// Inspector 修改 hub-*-facility-art 即实时生效（与万界阵/天箓同模式）。
        /// </summary>
        private void ApplyFacilityPlacement()
        {
            for (int i = 0; i < _facilityArtworks.Count; i++)
                _facilityArtworks[i].RefreshPlacement();
        }

        protected override void OnSceneEnter()
        {
            RunAccountScope.Clear();
            ResolvePortalPresentation();
            ResolveTianluPresentation();
            ResolveArrivalPresentation();
            ResolveVideoFacilityArt();
            ResolveFacilityArt();
            CreateStageBackground();
            CreateTianluFacility();
            CreatePortal();
            CreateTransferEffect();
            CreateWorldSelectionPanel();
            CreateTianluRegistryPanel();
            CreateWanbaogeRelicPanel();
            CreateYinglingHallPanel();
            CreateTianjiArcanaPanel();

            _portal.OpeningRequested.AddListener(_transferEffect.Play);
            _portal.OpeningRequested.AddListener(() =>
                WanjieScroll.Audio.AudioManager.PlaySfxSafe(
                    WanjieScroll.Audio.SfxType.FormationActivate));
            _transferEffect.ScreenReady += HandleRealmScreenReady;
            _transferEffect.ScreenClosed += HandleRealmScreenClosed;
            _worldSelection.CloseRequested += HandleRealmScreenCloseRequested;
            _worldSelection.DestinationSelected += HandleDestinationSelected;
            CreateArrivalRestorationEffect();
            CreateArrivalPresentation();
        }

#if UNITY_EDITOR
        // ── 神域阶段快速调试窗口 ────────────────────────────────
        // F1 直进神域或 F5 会话挂了测试档时显示：随时切换进度阶段
        // 验收设施/解锁状态，与 F5 地图的关卡选择器同源共用预设档原语。

        private void OnGUI()
        {
            if (!EditorLevelMapQuickPlayRuntime.IsQuickPlayHubSession)
                return;
            DrawStageDebugWindow();
        }

        private void DrawStageDebugWindow()
        {
            EnsureQuickPlayPresetSelection();
            float height = _quickPlayPanelExpanded ? 280f : 42f;
            GUILayout.BeginArea(
                new Rect(16f, 16f, 430f, height),
                _quickPlayPanelExpanded ? "神域阶段快速调试" : "神域测试",
                GUI.skin.window);
            if (GUILayout.Button(
                    _quickPlayPanelExpanded ? "收起快速测试" : "展开快速测试",
                    GUILayout.Height(24f)))
            {
                _quickPlayPanelExpanded = !_quickPlayPanelExpanded;
                GUILayout.EndArea();
                return;
            }
            if (!_quickPlayPanelExpanded)
            {
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label(DescribeQuickPlayAccount());
            GUILayout.Label("仙域进度测试（隔离测试槽，不改真实存档）");
            _quickPlayPresetIndex = Mathf.Clamp(
                EditorGUILayout.Popup(
                    _quickPlayPresetIndex,
                    QuickPlayPresetLabels,
                    GUILayout.Height(24f)),
                0,
                QuickPlayPresetLabels.Length - 1);
            GUILayout.Label(
                DescribePreset((TestAccountPreset)_quickPlayPresetIndex),
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label(
                "通关后用于设施/英灵验收；地图快选会重新匹配目标关前数据。货币归零，不携带进行中牌组。",
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("应用所选测试数据", GUILayout.Height(28f)))
            {
                TestAccountPreset preset =
                    (TestAccountPreset)_quickPlayPresetIndex;
                GUILayout.EndArea();
                SwitchQuickPlayStage(preset);
                return;
            }
            if (GUILayout.Button("还原真实存档档位", GUILayout.Height(26f)))
            {
                GUILayout.EndArea();
                EditorLevelMapQuickPlayRuntime.RestoreActiveSlotIfRedirected();
                ReloadHubScene();
                return;
            }
            GUILayout.EndArea();
        }

        private void EnsureQuickPlayPresetSelection()
        {
            if (_quickPlayPresetInitialized)
                return;
            _quickPlayPresetInitialized = true;
            if (EditorLevelMapQuickPlayRuntime.TryGetLastStagedPreset(
                    out TestAccountPreset staged))
            {
                _quickPlayPresetIndex = Mathf.Clamp(
                    (int)staged,
                    0,
                    QuickPlayPresetLabels.Length - 1);
                return;
            }

            MetaProgression meta = LoadActiveMeta();
            if (meta.IsLevelCleared(MetaProgression.G2FacilityUnlockLevelContentId) &&
                meta.IsCrawlerUnlocked("crawler.xiyou.bailongma.v1"))
            {
                _quickPlayPresetIndex = (int)TestAccountPreset.G2Cleared;
            }
            else if (meta.IsRelicUnlocked(RelicContentIds.Polentir) &&
                meta.IsG1FacilityStageUnlocked)
            {
                _quickPlayPresetIndex =
                    (int)TestAccountPreset.G2F1Cleared;
            }
            else if (meta.IsG1FacilityStageUnlocked)
            {
                _quickPlayPresetIndex =
                    (int)TestAccountPreset.G1Cleared;
            }
            else if (TutorialProgressionGate.IsCompleted(meta))
            {
                _quickPlayPresetIndex =
                    (int)TestAccountPreset.PostTutorial;
            }
            else
            {
                _quickPlayPresetIndex =
                    (int)TestAccountPreset.FreshAccount;
            }
        }

        private static string DescribePreset(TestAccountPreset preset)
        {
            return EditorLevelMapQuickPlayRuntime.DescribePreset(preset);
        }

        private static string DescribeQuickPlayAccount()
        {
            return EditorLevelMapQuickPlayRuntime.HasRedirectedActiveSlot
                ? $"当前：测试档（槽 {EditorLevelMapQuickPlayRuntime.QuickPlayTestSlot}）· " +
                  EditorLevelMapQuickPlayRuntime.LastStagedPresetDescription
                : "当前：真实存档（活动槽）";
        }

        private static void SwitchQuickPlayStage(
            WanjieScroll.Progression.TestAccountPreset preset)
        {
            EditorLevelMapQuickPlayRuntime.ApplyPresetAccount(preset);
            // 与 LevelMapBootstrap.SwitchEditorLevel 同口径：换档后清空
            // 残留 Run/场景上下文，再重建 Hub，设施按新账号解析。
            LevelRunSessionStore.Clear();
            SceneContext.Clear();
            ReloadHubScene();
        }

        private static void ReloadHubScene()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                WanjieScroll.Core.SceneNames.Hub);
        }
#endif

        protected override void OnSceneExit()
        {
            if (_tianjiArcanaPanel != null)
            {
                _tianjiArcanaPanel.CloseRequested -= HandleTianjiCloseRequested;
                _tianjiArcanaPanel = null;
            }
            // 场景切换可能绕过面板的返回按钮，离开神域时也要截断登场语音。
            WanjieScroll.Audio.AudioManager.StopVoiceCueSafe();
            _facilityArtworks.Clear();
            _activeFacilityArt = null;
            _videoFacilityArtworks.Clear();
            _activeVideoFacilityArt = null;
            _videoFacilityExpectedReady = 0;
            _videoFacilityReady = 0;
            if (_portal != null && _transferEffect != null)
            {
                _portal.OpeningRequested.RemoveListener(_transferEffect.Play);
                _portal.OpeningRequested.RemoveListener(() =>
                    WanjieScroll.Audio.AudioManager.PlaySfxSafe(
                        WanjieScroll.Audio.SfxType.FormationActivate));
            }
            if (_transferEffect != null)
            {
                _transferEffect.ScreenReady -= HandleRealmScreenReady;
                _transferEffect.ScreenClosed -= HandleRealmScreenClosed;
            }
            if (_worldSelection != null)
            {
                _worldSelection.CloseRequested -= HandleRealmScreenCloseRequested;
                _worldSelection.DestinationSelected -= HandleDestinationSelected;
            }
            _worldCatalog = null;

            if (_tianluRegistryPanel != null)
            {
                _tianluRegistryPanel.CloseRequested -=
                    HandleTianluRegistryCloseRequested;
                _tianluRegistryPanel = null;
            }

            if (_yinglingHallArtwork != null)
            {
                _yinglingHallArtwork.Clicked -=
                    HandleYinglingHallFacilityClicked;
                _yinglingHallArtwork = null;
            }
            if (_yinglingHallPanel != null)
            {
                _yinglingHallPanel.CloseRequested -=
                    HandleYinglingHallCloseRequested;
                _yinglingHallPanel.ArrivalVoiceStopRequested -=
                    HandleYinglingHallVoiceStopRequested;
                _yinglingHallPanel.ArrivalVoiceRequested -=
                    HandleYinglingHallVoiceRequested;
                _yinglingHallPanel = null;
            }

            if (_runtimeFallbackPresentation != null)
            {
                Destroy(_runtimeFallbackPresentation);
                _runtimeFallbackPresentation = null;
            }

            if (_runtimeFallbackTianluPresentation != null)
            {
                Destroy(_runtimeFallbackTianluPresentation);
                _runtimeFallbackTianluPresentation = null;
            }

            if (_runtimeFallbackArrivalPresentation != null)
            {
                Destroy(_runtimeFallbackArrivalPresentation);
                _runtimeFallbackArrivalPresentation = null;
            }
        }

        private void ResolveVideoFacilityArt()
        {
            HubFacilityVideoOverlayProfile art = _videoFacilityArt != null
                ? _videoFacilityArt
                : Resources.Load<HubFacilityVideoOverlayProfile>(VideoFacilityArtPath);
            if (art == null)
                return;
            if (!art.TryValidate(out string error))
            {
                Debug.LogError($"[HubScene] Invalid video facility art: {error}", this);
                return;
            }
            _activeVideoFacilityArt = art;
        }

        private void ResolveFacilityArt()
        {
            if (_activeVideoFacilityArt != null)
                return;
            // The complete G1 facility stage stays hidden until G1 is cleared,
            // while preserving saves that only recorded the legacy shop flag.
            if (!LoadActiveMeta().IsG1FacilityStageUnlocked)
                return;

            HubFacilityArtProfile art = _facilityArt != null
                ? _facilityArt
                : Resources.Load<HubFacilityArtProfile>(G1FacilityArtPath);
            if (art == null)
            {
                Debug.LogError($"[HubScene] Missing Resources/{G1FacilityArtPath}.", this);
                return;
            }
            if (!art.TryValidate(out string error))
            {
                Debug.LogError($"[HubScene] Invalid G1 facility art: {error}", this);
                return;
            }
            _activeFacilityArt = art;
        }

        private void CreateStageBackground()
        {
            if (_activeVideoFacilityArt != null)
            {
                CreateVideoStageBackground();
                return;
            }

            CreateStaticStageBackground();
        }

        private void CreateVideoStageBackground()
        {
            Texture2D background = _activeVideoFacilityArt.Background;
            var backgroundObject = new GameObject(
                "StageVideoBackground",
                typeof(RectTransform),
                typeof(RawImage),
                typeof(AspectRatioFitter));
            backgroundObject.transform.SetParent(Canvas.transform, false);
            backgroundObject.transform.SetAsFirstSibling();

            RawImage image = backgroundObject.GetComponent<RawImage>();
            image.texture = background;
            image.color = Color.white;
            image.raycastTarget = false;
            ApplyCoreStageUvCrop(image, background);
            // The core layer is always rendered at 16:9 and 100% height.
            // Extension strips never participate in its scale or anchors.
            image.enabled = true;

            RectTransform rect = backgroundObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            AspectRatioFitter aspect = backgroundObject.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            aspect.aspectRatio = CoreStageAspect;
            _stageArtRect = rect;
            // Keep facilities on the centered 16:9 logical stage. The
            // extension container only fills the leftover side bands, so it
            // never changes facility anchors or the core's vertical scale.
            CreateStageExtensionBackdrop(background);
            CreateVideoFacilityArtworks();
        }

        private void CreateStaticStageBackground()
        {
            Sprite background = _activeFacilityArt != null
                ? _activeFacilityArt.Background
                : (_stageBackground != null ? _stageBackground
                    : Resources.Load<Sprite>(StageBackgroundPath));

            var backgroundObject = new GameObject(
                _activeFacilityArt != null ? "Stage2Background" : "Stage1Background",
                typeof(RectTransform),
                typeof(RawImage),
                typeof(AspectRatioFitter));
            backgroundObject.transform.SetParent(Canvas.transform, false);
            backgroundObject.transform.SetAsFirstSibling();

            RawImage image = backgroundObject.GetComponent<RawImage>();
            image.texture = background != null ? background.texture : null;
            image.color = background != null
                ? Color.white
                : new Color(0.035f, 0.08f, 0.095f, 1f);
            image.raycastTarget = false;
            ApplyCoreStageUvCrop(image, background != null ? background.texture : null);
            // The core layer is always rendered at 16:9 and 100% height.
            // Extension strips never participate in its scale or anchors.
            image.enabled = true;

            RectTransform rect = backgroundObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            AspectRatioFitter aspect =
                backgroundObject.GetComponent<AspectRatioFitter>();
            // 核心 16:9 地图始终占满屏幕高度。超宽屏多出的左右区域由
            // 下方延展背景承接；窄屏只裁切左右边缘，不改变地图比例。
            aspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            aspect.aspectRatio = CoreStageAspect;
            _stageArtRect = rect;
            CreateStageExtensionBackdrop(background != null ? background.texture : null);

            if (_activeFacilityArt != null)
            {
                _facilityCanvasSize = background.rect.size;
                foreach (HubFacilityArtProfile.Facility facility in
                         _activeFacilityArt.Facilities)
                {
                    var artworkObject = new GameObject(facility.RegistryEntryId,
                        typeof(RectTransform), typeof(HubFacilityArtwork));
                    artworkObject.transform.SetParent(rect, false);
                    HubFacilityArtwork artwork =
                        artworkObject.GetComponent<HubFacilityArtwork>();
                    artwork.Initialize(facility, _facilityCanvasSize);
                    if (facility.RegistryEntryId ==
                        "tianlu.entry.facility.daozang.v1")
                    {
                        artwork.Clicked += HandleDaozangFacilityClicked;
                    }
                    else if (facility.RegistryEntryId ==
                             "tianlu.entry.facility.qunyingdian.v1")
                    {
                        _yinglingHallArtwork = artwork;
                        artwork.Clicked += HandleYinglingHallFacilityClicked;
                    }
                    if (facility.RegistryEntryId == "tianlu.entry.facility.tianjige.v1")
                        artwork.Clicked += HandleTianjiFacilityClicked;
                    _facilityArtworks.Add(artwork);
                }
            }

            if (background == null)
            {
                Debug.LogError(
                    $"[HubScene] Missing Stage 1 background Resources/{StageBackgroundPath}.",
                    this);
            }
        }

        private void CreateVideoFacilityArtworks()
        {
            TianluRegistryCatalog catalog =
                Resources.Load<TianluRegistryCatalog>(TianluRegistryCatalogPath);
            if (catalog == null)
            {
                Debug.LogError(
                    $"[HubScene] Missing Resources/{TianluRegistryCatalogPath}; " +
                    "video facilities will remain hidden.", this);
                return;
            }

            var unlocked = new System.Collections.Generic.Dictionary<string, bool>();
            foreach (TianluRegistryItem item in catalog.Resolve(LoadActiveMeta()))
            {
                if (item.Kind == TianluEntryKind.Facility)
                    unlocked[item.ContentId] = item.Unlocked;
            }

            _facilityCanvasSize = _activeVideoFacilityArt.CanvasSize;
            var visibleFacilities =
                new System.Collections.Generic.List<HubFacilityVideoOverlayProfile.Facility>();
            foreach (HubFacilityVideoOverlayProfile.Facility facility in
                     _activeVideoFacilityArt.Facilities)
            {
                if (!unlocked.TryGetValue(facility.RegistryEntryId, out bool isUnlocked) ||
                    !isUnlocked)
                {
                    continue;
                }
                visibleFacilities.Add(facility);
            }

            _videoFacilityExpectedReady = visibleFacilities.Count;
            _videoFacilityReady = 0;
            foreach (HubFacilityVideoOverlayProfile.Facility facility in visibleFacilities)
            {

                var artworkObject = new GameObject(facility.RegistryEntryId,
                    typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(RawImage), typeof(VideoPlayer),
                    typeof(HubFacilityVideoArtwork));
                HubFacilityVideoArtwork artwork =
                    artworkObject.GetComponent<HubFacilityVideoArtwork>();
                artwork.Ready += HandleVideoFacilityReady;
                // Register before Prepare can complete synchronously on a
                // cached clip, so the final readiness reveal includes this
                // artwork as well.
                _videoFacilityArtworks.Add(artwork);
                artwork.Initialize(facility, _facilityCanvasSize, _stageArtRect);
                artwork.SetVisible(false);
                if (facility.RegistryEntryId == "tianlu.entry.facility.tianlu.v1")
                {
                    artwork.Clicked += () => _tianluRegistryPanel?.Show();
                }
                else if (facility.RegistryEntryId == "tianlu.entry.facility.tianjige.v1")
                {
                    artwork.Clicked += HandleTianjiFacilityClicked;
                }
                else if (facility.RegistryEntryId == "tianlu.entry.facility.wanbaoge.v1")
                {
                    artwork.Clicked += HandleWanbaogeFacilityClicked;
                }
                else if (facility.RegistryEntryId ==
                         "tianlu.entry.facility.daozang.v1")
                {
                    artwork.Clicked += HandleDaozangFacilityClicked;
                }
                else if (facility.RegistryEntryId ==
                         "tianlu.entry.facility.qunyingdian.v1")
                {
                    artwork.Clicked += HandleYinglingHallFacilityClicked;
                }
            }

            if (_videoFacilityExpectedReady == 0)
                ShowReadyVideoFacilities();
        }

        private void HandleVideoFacilityReady(HubFacilityVideoArtwork artwork)
        {
            _videoFacilityReady = Mathf.Min(
                _videoFacilityExpectedReady, _videoFacilityReady + 1);
            if (_videoFacilityReady >= _videoFacilityExpectedReady)
                ShowReadyVideoFacilities();
        }

        private void ShowReadyVideoFacilities()
        {
            for (int i = 0; i < _videoFacilityArtworks.Count; i++)
                _videoFacilityArtworks[i].SetVisible(true);
        }

        private void CreateStageExtensionBackdrop(Texture2D background)
        {
            var backdropObject = new GameObject(
                "StageExtensionBackdrop",
                typeof(RectTransform));
            backdropObject.transform.SetParent(Canvas.transform, false);
            backdropObject.transform.SetAsFirstSibling();

            RectTransform root = backdropObject.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            _stageExtensionTexture = background;
            CreateStageExtensionStrip(root, "StageExtensionLeft",
                out _stageExtensionLeftRect, out _stageExtensionLeftImage);
            CreateStageExtensionStrip(root, "StageExtensionRight",
                out _stageExtensionRightRect, out _stageExtensionRightImage);
            ApplyStageExtensionLayout();
        }

        private static void CreateStageExtensionStrip(
            Transform parent,
            string objectName,
            out RectTransform rect,
            out RawImage image)
        {
            var stripObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(RawImage));
            stripObject.transform.SetParent(parent, false);

            rect = stripObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            image = stripObject.GetComponent<RawImage>();
            image.raycastTarget = false;
        }

        private void ApplyStageExtensionLayout()
        {
            if (_stageExtensionLeftRect == null ||
                _stageExtensionRightRect == null ||
                _stageExtensionLeftImage == null ||
                _stageExtensionRightImage == null ||
                _stageArtRect == null ||
                Canvas == null ||
                _stageExtensionTexture == null ||
                _stageExtensionTexture.width <= 0 ||
                _stageExtensionTexture.height <= 0)
            {
                SetStageExtensionStripVisible(false, _stageExtensionLeftRect,
                    _stageExtensionLeftImage);
                SetStageExtensionStripVisible(false, _stageExtensionRightRect,
                    _stageExtensionRightImage);
                return;
            }

            RectTransform canvasRect = Canvas.GetComponent<RectTransform>();
            if (canvasRect == null || canvasRect.rect.width <= 0f ||
                canvasRect.rect.height <= 0f ||
                _stageArtRect.rect.width <= 0f ||
                _stageArtRect.rect.height <= 0f)
            {
                return;
            }

            float halfCanvasWidth = canvasRect.rect.width * 0.5f;
            float halfCoreWidth = _stageArtRect.rect.width * 0.5f;
            float leftWidth = Mathf.Max(0f, halfCanvasWidth - halfCoreWidth);
            float rightWidth = leftWidth;
            float leftCenter = -halfCoreWidth - leftWidth * 0.5f;
            float rightCenter = halfCoreWidth + rightWidth * 0.5f;

            float textureAspect = (float)_stageExtensionTexture.width /
                _stageExtensionTexture.height;
            if (textureAspect <= CoreStageAspect)
            {
                // A non-expanded 16:9 layer has no side band to sample.
                SetStageExtensionStripVisible(false, _stageExtensionLeftRect,
                    _stageExtensionLeftImage);
                SetStageExtensionStripVisible(false, _stageExtensionRightRect,
                    _stageExtensionRightImage);
                return;
            }

            float coreUvWidth = Mathf.Min(1f, CoreStageAspect / textureAspect);
            float coreUvMin = (1f - coreUvWidth) * 0.5f;
            float coreUvMax = coreUvMin + coreUvWidth;

            ConfigureStageExtensionStrip(
                _stageExtensionLeftRect,
                _stageExtensionLeftImage,
                leftWidth,
                leftCenter,
                new Rect(0f, 0f, coreUvMin, 1f));
            ConfigureStageExtensionStrip(
                _stageExtensionRightRect,
                _stageExtensionRightImage,
                rightWidth,
                rightCenter,
                new Rect(coreUvMax, 0f, 1f - coreUvMax, 1f));
        }

        private void ConfigureStageExtensionStrip(
            RectTransform rect,
            RawImage image,
            float width,
            float x,
            Rect uvRect)
        {
            bool visible = width > 0.01f && _stageExtensionTexture != null;
            SetStageExtensionStripVisible(visible, rect, image);
            if (!visible)
                return;

            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, 0f);
            image.texture = _stageExtensionTexture;
            image.color = Color.white;
            image.uvRect = uvRect;
        }

        private static void SetStageExtensionStripVisible(
            bool visible,
            RectTransform rect,
            RawImage image)
        {
            if (rect == null || image == null)
                return;

            rect.gameObject.SetActive(visible);
        }

        private static void ApplyCoreStageUvCrop(RawImage image, Texture2D texture)
        {
            if (image == null || texture == null || texture.width <= 0 ||
                texture.height <= 0)
            {
                return;
            }

            float textureAspect = (float)texture.width / texture.height;
            if (textureAspect <= CoreStageAspect)
            {
                image.uvRect = new Rect(0f, 0f, 1f, 1f);
                return;
            }

            float coreWidth = CoreStageAspect / textureAspect;
            image.uvRect = new Rect(
                (1f - coreWidth) * 0.5f,
                0f,
                coreWidth,
                1f);
        }

        private void CreateTianluFacility()
        {
            var facilityObject = new GameObject(
                "TianluRegistry",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(HubTianluHoverAnim));
            facilityObject.transform.SetParent(
                _stageArtRect != null ? _stageArtRect : Canvas.transform,
                false);

            Image image = facilityObject.GetComponent<Image>();
            image.raycastTarget = true;
            image.sprite = RuntimeArtLoader.GetTianluHoverFrame(0);
            if (_activeVideoFacilityArt != null)
            {
                image.color = Color.clear;
                image.raycastTarget = false;
            }
            _tianluImage = image;

            Button openRegistry = facilityObject.AddComponent<Button>();
            openRegistry.targetGraphic = image;
            openRegistry.transition = Selectable.Transition.None;
            openRegistry.onClick.AddListener(() => _tianluRegistryPanel?.Show());
            if (_activeVideoFacilityArt != null)
            {
                // The legacy Tianlu button remains for the arrival/panel
                // wiring, but its transparent Image must not sit above the
                // video Tianlu and swallow pointer enter/exit events.
                image.raycastTarget = false;
                openRegistry.interactable = false;
            }

            _tianluAnim = facilityObject.GetComponent<HubTianluHoverAnim>();
            _tianluAnim.frames = RuntimeArtLoader.GetTianluHoverFrames();

            _tianluRect = facilityObject.GetComponent<RectTransform>();
            ApplyTianluPlacement();
        }

        private void CreateTianluRegistryPanel()
        {
            var panelObject = new GameObject(
                "TianluRegistryPanel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(TianluRegistryPanel));
            panelObject.transform.SetParent(Canvas.transform, false);

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _tianluRegistryPanel = panelObject.GetComponent<TianluRegistryPanel>();
            _tianluRegistryPanel.CloseRequested += HandleTianluRegistryCloseRequested;
            // 返回按钮统一点击音：与全局按钮音效共用同帧去重，不会双响。
            _tianluRegistryPanel.ReturnButton.onClick.AddListener(
                GlobalButtonAudioRuntime.PlayClickSound);

            TianluRegistryCatalog catalog =
                Resources.Load<TianluRegistryCatalog>(TianluRegistryCatalogPath);
            if (catalog == null)
            {
                Debug.LogError(
                    $"[HubScene] Missing Resources/{TianluRegistryCatalogPath}; " +
                    "tianlu registry will be empty.",
                    this);
                _tianluRegistryPanel.SetItems(null);
                return;
            }

            _tianluRegistryPanel.SetItems(
                catalog.Resolve(LoadActiveMeta()));
        }

        private void CreateYinglingHallPanel()
        {
            GameObject prefab = Resources.Load<GameObject>(YinglingHallPanel.ResourcePath);
            if (prefab == null)
            {
                Debug.LogError("[HubScene] Missing Resources/" +
                    YinglingHallPanel.ResourcePath + ".", this);
                return;
            }
            GameObject panelObject = Instantiate(prefab, Canvas.transform, false);
            panelObject.name = "YinglingHallPanel";

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _yinglingHallPanel = panelObject.GetComponent<YinglingHallPanel>();
            _yinglingHallPanel.CloseRequested +=
                HandleYinglingHallCloseRequested;
            _yinglingHallPanel.ArrivalVoiceStopRequested +=
                HandleYinglingHallVoiceStopRequested;
            _yinglingHallPanel.ArrivalVoiceRequested +=
                HandleYinglingHallVoiceRequested;
            _yinglingHallPanel.ReturnButton.onClick.AddListener(
                GlobalButtonAudioRuntime.PlayClickSound);
            _yinglingHallPanel.Initialize(
                LoadActiveMeta(),
                WanjieScroll.Persistence.GameSaveService.CreateDefault());
        }

        private void HandleTianluRegistryCloseRequested()
        {
            _tianluRegistryPanel?.Hide();
        }

        private void CreateTianjiArcanaPanel()
        {
            GameObject prefab = Resources.Load<GameObject>(TianjiArcanaPanel.ResourcePath);
            if (prefab == null)
            {
                Debug.LogError("[HubScene] Missing Resources/" + TianjiArcanaPanel.ResourcePath, this);
                return;
            }
            GameObject view = Instantiate(prefab, Canvas.transform, false);
            RectTransform rect = view.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _tianjiArcanaPanel = view.GetComponent<TianjiArcanaPanel>();
            _tianjiArcanaPanel.CloseRequested += HandleTianjiCloseRequested;
            _tianjiArcanaPanel.ReturnButton.onClick.AddListener(GlobalButtonAudioRuntime.PlayClickSound);
        }

        private void HandleTianjiFacilityClicked()
        {
            if (_tianjiArcanaPanel == null || !LoadActiveMeta().IsTianjiBuildingUnlocked) return;
            SetPrimaryFacilityInteractionEnabled(false);
            _tianjiArcanaPanel.Show();
        }

        private void HandleTianjiCloseRequested()
        {
            _tianjiArcanaPanel?.Hide();
            SetPrimaryFacilityInteractionEnabled(true);
        }

        private void CreateWanbaogeRelicPanel()
        {
            var o = new GameObject("WanbaogeRelicPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(WanbaogeRelicPanel));
            o.transform.SetParent(Canvas.transform, false);
            var r = (RectTransform)o.transform; r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.offsetMin=r.offsetMax=Vector2.zero;
            _wanbaogeRelicPanel=o.GetComponent<WanbaogeRelicPanel>();
            _wanbaogeRelicPanel.CloseRequested += HandleWanbaogeRelicPanelCloseRequested;
            _wanbaogeRelicPanel.ReturnButton.onClick.AddListener(GlobalButtonAudioRuntime.PlayClickSound);
            _wanbaogeRelicPanel.SetCollection(RelicCollectionCatalog.Resolve(LoadActiveMeta(),
                WanjieScroll.Content.ContentCatalog.Current, Resources.LoadAll<LevelRunProfile>("Levels")));
        }

        private void HandleWanbaogeFacilityClicked()
        {
            MetaProgression meta=LoadActiveMeta();
            if (!meta.IsWanbaogeUnlocked || _wanbaogeRelicPanel==null) return;
            SetPrimaryFacilityInteractionEnabled(false);
            _wanbaogeRelicPanel.SetCollection(RelicCollectionCatalog.Resolve(meta,
                WanjieScroll.Content.ContentCatalog.Current, Resources.LoadAll<LevelRunProfile>("Levels")));
            _wanbaogeRelicPanel.Show();
        }

        private void HandleWanbaogeRelicPanelCloseRequested()
        {
            _wanbaogeRelicPanel?.HideImmediately();
            SetPrimaryFacilityInteractionEnabled(true);
        }

        private void HandleYinglingHallFacilityClicked()
        {
            WanjieScroll.Meta.MetaProgression meta = LoadActiveMeta();
            if (!meta.IsDaozangUnlocked || _yinglingHallPanel == null)
                return;

            SetPrimaryFacilityInteractionEnabled(false);
            _yinglingHallPanel?.Show();
        }

        private void HandleYinglingHallCloseRequested()
        {
            _yinglingHallPanel?.Hide();
            SetPrimaryFacilityInteractionEnabled(true);
        }

        private static void HandleYinglingHallVoiceStopRequested()
        {
            WanjieScroll.Audio.AudioManager.StopVoiceCueSafe();
        }

        private static void HandleYinglingHallVoiceRequested(string cueId)
        {
            if (!string.IsNullOrWhiteSpace(cueId))
                WanjieScroll.Audio.AudioManager.PlayVoiceCueSafe(cueId);
        }

        private static WanjieScroll.Meta.MetaProgression LoadActiveMeta()
        {
            var meta = new WanjieScroll.Meta.MetaProgression();
            WanjieScroll.Persistence.GameSaveService.CreateDefault()
                .TryLoad(meta, out _);
            return meta;
        }

        private void ResolveTianluPresentation()
        {
            if (_tianluPresentation == null)
                _tianluPresentation =
                    Resources.Load<HubTianluPresentationProfile>(
                        TianluPresentationPath);
            if (_tianluPresentation != null)
                return;

            _runtimeFallbackTianluPresentation =
                ScriptableObject.CreateInstance<HubTianluPresentationProfile>();
            _runtimeFallbackTianluPresentation.name =
                "Hub Tianlu Presentation (Fallback)";
            _runtimeFallbackTianluPresentation.hideFlags =
                HideFlags.HideAndDontSave;
            _tianluPresentation = _runtimeFallbackTianluPresentation;
            Debug.LogError(
                $"[HubScene] Missing Resources/{TianluPresentationPath}; using runtime defaults.",
                this);
        }

        private void ResolveArrivalPresentation()
        {
            if (_arrivalPresentation == null)
            {
                _arrivalPresentation =
                    Resources.Load<HubArrivalPresentationProfile>(
                        ArrivalPresentationPath);
            }
            if (_arrivalPresentation != null)
                return;

            _runtimeFallbackArrivalPresentation =
                ScriptableObject.CreateInstance<HubArrivalPresentationProfile>();
            _runtimeFallbackArrivalPresentation.name =
                "Hub First Restoration Presentation (Fallback)";
            _runtimeFallbackArrivalPresentation.hideFlags =
                HideFlags.HideAndDontSave;
            _arrivalPresentation = _runtimeFallbackArrivalPresentation;
            Debug.LogError(
                $"[HubScene] Missing Resources/{ArrivalPresentationPath}; " +
                "using runtime defaults.",
                this);
        }

        private void CreateArrivalPresentation()
        {
            var arrivalObject = new GameObject(
                "HubArrivalPresentationCoordinator");
            arrivalObject.transform.SetParent(transform, false);
            _arrivalCoordinator =
                arrivalObject.AddComponent<HubArrivalPresentationCoordinator>();
            _arrivalCoordinator.Bind(
                _portal,
                _portalRect,
                _tianluImage,
                _tianluRect,
                _tianluAnim,
                _arrivalRestorationEffect,
                _arrivalPresentation,
                LoadActiveMeta(),
                WanjieScroll.Persistence.GameSaveService.CreateDefault(),
                SetPrimaryFacilityInteractionEnabled);
            _arrivalCoordinator.BeginForCurrentProgression();
        }

        private void CreateArrivalRestorationEffect()
        {
            var effectObject = new GameObject(
                "HubFirstRestorationEffect",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(HubRealmRestorationEffect));
            effectObject.transform.SetParent(Canvas.transform, false);
            RectTransform rect = effectObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _arrivalRestorationEffect =
                effectObject.GetComponent<HubRealmRestorationEffect>();
        }

        private void SetPrimaryFacilityInteractionEnabled(bool enabled)
        {
            foreach (HubFacilityArtwork artwork in _facilityArtworks)
                artwork.SetHoverEnabled(enabled);
            foreach (HubFacilityVideoArtwork artwork in _videoFacilityArtworks)
                artwork.SetInteractionEnabled(enabled);
            if (_portal != null)
                _portal.raycastTarget = enabled;
            if (_tianluImage != null)
                _tianluImage.raycastTarget =
                    enabled && _activeVideoFacilityArt == null;
        }

        private void HandleDaozangFacilityClicked()
        {
            if (!LoadActiveMeta().IsDaozangUnlocked)
                return;
            SceneLoader.GoToDaozang();
        }

        private void ApplyTianluPlacement()
        {
            if (_tianluRect == null)
                return;

            HubTianluPresentationProfile profile = _tianluPresentation;
            if (profile == null)
                return;

            Sprite frame = _tianluImage != null ? _tianluImage.sprite : null;
            float aspect = frame != null && frame.rect.height > 0f
                ? frame.rect.width / frame.rect.height
                : TianluFallbackAspect;
            var size = new Vector2(profile.Width, profile.Width / aspect);

            PlaceAtArtCoordinate(
                _tianluRect, profile.Centre, size, GetStageArtSize());
            _tianluRect.localEulerAngles = profile.RotationEuler;
        }

        private void ApplyTianluAnimation()
        {
            if (_tianluAnim == null)
                return;

            HubTianluPresentationProfile profile = _tianluPresentation;
            if (profile == null)
                return;

            _tianluAnim.playDuration = profile.HoverPlayDuration;
            _tianluAnim.rewindDuration = profile.HoverRewindDuration;
        }

        private void CreatePortal()
        {
            var portalObject = new GameObject(
                "WanjieBaguaPortal",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(HubBaguaPortalGraphic));
            portalObject.transform.SetParent(
                _stageArtRect != null ? _stageArtRect : Canvas.transform,
                false);

            RectTransform rect = portalObject.GetComponent<RectTransform>();
            _portalRect = rect;
            _portal = portalObject.GetComponent<HubBaguaPortalGraphic>();
            ApplyPortalPresentation();
        }

        private void CreateTransferEffect()
        {
            var effectObject = new GameObject(
                "WanjieTransferEffect",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(HubPortalTransferEffect));
            effectObject.transform.SetParent(Canvas.transform, false);

            RectTransform rect = effectObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _transferEffect = effectObject.GetComponent<HubPortalTransferEffect>();
            ApplyPortalPresentation();
        }

        private void CreateWorldSelectionPanel()
        {
            var panelObject = new GameObject(
                "WanjieWorldSelectionPanel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(HubWorldSelectionPanel));
            panelObject.transform.SetParent(Canvas.transform, false);

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _worldSelection = panelObject.GetComponent<HubWorldSelectionPanel>();

            _worldCatalog = Resources.Load<HubWorldCatalog>(WorldCatalogPath);
            if (_worldCatalog == null)
            {
                Debug.LogError(
                    $"[HubScene] Missing Resources/{WorldCatalogPath}; " +
                    "world selection will be empty.",
                    this);
                _worldSelection.SetDestinations(null);
                return;
            }

            _worldSelection.SetDestinations(
                _worldCatalog.ResolveDestinations(LoadActiveMeta()));
        }

        private void ResolvePortalPresentation()
        {
            _activePresentation = _portalPresentation != null
                ? _portalPresentation
                : Resources.Load<HubPortalPresentationProfile>(PortalPresentationPath);
            if (_activePresentation != null)
                return;

            _runtimeFallbackPresentation =
                ScriptableObject.CreateInstance<HubPortalPresentationProfile>();
            _runtimeFallbackPresentation.name = "Hub Portal Presentation (Fallback)";
            _runtimeFallbackPresentation.hideFlags = HideFlags.HideAndDontSave;
            _activePresentation = _runtimeFallbackPresentation;
            Debug.LogError(
                $"[HubScene] Missing Resources/{PortalPresentationPath}; using runtime defaults.",
                this);
        }

        private void ApplyPortalPresentation()
        {
            HubPortalPresentationProfile profile = _activePresentation;
            if (profile == null)
                return;

            if (_portalRect != null)
            {
                PlaceAtArtCoordinate(
                    _portalRect,
                    profile.FormationCentre,
                    profile.FormationSize,
                    GetStageArtSize());
                _portalRect.localEulerAngles = new Vector3(
                    0f,
                    0f,
                    profile.FormationRotationDegrees);
            }

            if (_portal != null)
            {
                _portal.ConfigureMotion(new HubPortalMotionSettings(
                    profile.IdleBaseFlowStrength,
                    profile.IdleFlowInterval,
                    profile.IdleFlowDuration,
                    profile.IdleFlowStrength,
                    profile.IdleFlowSpeed,
                    profile.HoverSpinSpeedDegrees,
                    profile.HoverReturnSpeedDegrees,
                    profile.OpeningSpinDuration,
                    profile.OpeningSpinTurns,
                    profile.OpeningSinkStrength));
            }

            if (_transferEffect != null)
            {
                _transferEffect.Configure(
                    new HubPortalTransferEffect.PresentationSettings(
                        ToCanvasCoordinate(profile.FormationCentre),
                        profile.FormationSize,
                        profile.PullDuration,
                        profile.PullParticleCount,
                        profile.PullParticleScale,
                        profile.PullParticleBrightness,
                        profile.SelectionScrimAlpha));
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _activePresentation = _portalPresentation;
            ApplyPortalPresentation();
            ApplyTianluPlacement();
        }
#endif

        private void HandleRealmScreenReady()
        {
            if (TryResumeActiveRunFromPortal())
                return;
            _worldSelection.Show();
        }

        private bool TryResumeActiveRunFromPortal()
        {
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run == null && SaveSlotStore.Current.HasAnySave())
            {
                MetaProgression meta = LoadActiveMeta();
                GameSaveService service = GameSaveService.CreateDefault();
                if (service.TryLoad(meta, out SaveGameData data, out _) &&
                    !string.IsNullOrWhiteSpace(data.activeDungeonRunPayload))
                {
                    DungeonRunSessionStore.TryRestore(
                        data.activeDungeonRunPayload, out _, sideEventContexts:
                        plan => WanjieScroll.Progression.DungeonSideEventProductionResolver.CreateRestoreContext(plan, meta));
                    run = DungeonRunSessionStore.Current;
                }
            }
            if (run == null || run.CurrentFloor == null)
                return false;

            _worldSelection.HideImmediately();
            SceneContext.Clear();
            SceneLoader.GoTo(
                run.CurrentFloor.ActiveEncounter.HasValue
                    ? SceneNames.Combat
                    : SceneNames.LevelMap);
            return true;
        }

        private void HandleRealmScreenCloseRequested()
        {
            ClosePortalPresentation();
        }

        private void HandleRealmScreenClosed()
        {
            _worldSelection.HideImmediately();
            _portal.SetState(HubBaguaPortalGraphic.PortalState.Idle);
        }

        private void HandleDestinationSelected(string destinationId)
        {
            if (_worldCatalog == null)
                _worldCatalog = Resources.Load<HubWorldCatalog>(WorldCatalogPath);
            if (_worldCatalog == null)
            {
                Debug.LogError(
                    $"[HubScene] Missing Resources/{WorldCatalogPath}; " +
                    "cannot resolve selected destination.",
                    this);
                return;
            }

            if (!_worldCatalog.TryResolveEntry(
                    destinationId,
                    out HubWorldCatalog.Entry destination,
                    out string routeError))
            {
                Debug.LogWarning($"[HubScene] {routeError}", this);
                return;
            }
            string firstFloorContentId = destination.firstFloorContentId;

            LevelRunProfile firstFloor;
            try
            {
                // 在重置现有 Run 前完成路由与关卡自校验。
                firstFloor = LevelRunProfileCatalog.Get(firstFloorContentId);
                var validationErrors = firstFloor.Validate();
                if (validationErrors.Count > 0)
                {
                    Debug.LogError(
                        $"[HubScene] Invalid first floor {firstFloorContentId}:\n" +
                        string.Join("\n", validationErrors),
                        this);
                    return;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogError(
                    $"[HubScene] Cannot resolve first floor " +
                    $"{firstFloorContentId}: {exception.Message}",
                    this);
                return;
            }

            // 从战斗按 Esc 返回仙域后，保留同一地牢的活动 Run；再次从仙域进入时
            // 回到上次自动存档节点，不要无条件创建新 Run 覆盖进度。
            DungeonRunSession existingRun = DungeonRunSessionStore.Current;
            if (existingRun == null && SaveSlotStore.Current.HasAnySave())
            {
                MetaProgression savedMeta = LoadActiveMeta();
                GameSaveService service = GameSaveService.CreateDefault();
                if (service.TryLoad(savedMeta, out SaveGameData saveData, out _) &&
                    !string.IsNullOrWhiteSpace(saveData.activeDungeonRunPayload))
                {
                    DungeonRunSessionStore.TryRestore(
                        saveData.activeDungeonRunPayload, out _, sideEventContexts:
                        plan => WanjieScroll.Progression.DungeonSideEventProductionResolver.CreateRestoreContext(plan, savedMeta));
                    existingRun = DungeonRunSessionStore.Current;
                }
            }
            if (existingRun != null &&
                string.Equals(existingRun.DungeonContentId,
                    firstFloor.dungeonContentId,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                GameState.SelectedVolumeId = 0;
                GameState.SelectedDungeonIndex = 0;
                // A formal pause keeps the unresolved encounter in the Run
                // payload.  Clicking the same portal must therefore resume
                // Combat directly; only a quiescent Run belongs on LevelMap.
                if (existingRun.CurrentFloor != null &&
                    existingRun.CurrentFloor.ActiveEncounter.HasValue)
                {
                    _worldSelection.HideImmediately();
                    SceneContext.Clear();
                    SceneLoader.GoTo(SceneNames.Combat);
                    return;
                }
                SceneLoader.GoToLevelMap();
                return;
            }

            WanjieScroll.Meta.MetaProgression meta = LoadActiveMeta();
            // Hub is composition only. Progression owns admission and entry
            // values; Dungeon owns adoption. No old Run is reset during preflight.
            if (!string.IsNullOrWhiteSpace(destination.runEntryContentId))
            {
                if (!WanjieScroll.Progression.RunStartService.TryPrepare(
                        destination.runEntryContentId, meta,
                        GameSession.Current.SelectedCrawlerId,
                        WanjieScroll.UI.Daozang.DaozangConfig.ApplyToPlayerStats,
                        out DungeonRunSession prepared, out string startError))
                {
                    Debug.LogWarning($"[HubScene] {startError}", this);
                    return;
                }
                if (!string.Equals(prepared.FirstFloorContentId, firstFloorContentId,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogError("[HubScene] 正式 Run 装载与所选关区路由不一致。", this);
                    return;
                }
                if (!DungeonRunSessionStore.TryAdoptPrepared(prepared, out startError))
                {
                    Debug.LogWarning($"[HubScene] {startError}", this);
                    return;
                }
            }
            else
            {
                GameSession.Current.StartNewRun();
                LevelRunSessionStore.BeginConfigured(firstFloorContentId);
            }
            GameState.SelectedVolumeId = 0;
            GameState.SelectedDungeonIndex = 0;
            WanjieScroll.Persistence.GameSaveService.CreateDefault().Save(
                meta,
                DungeonRunSessionStore.Current.Serialize());
            SceneLoader.GoToLevelMap();
        }

        /// <summary>Editor-only preview control for replaying the opening.</summary>
        public void ReplayPortalPresentation()
        {
            if (!PortalPreviewReady)
                return;

            _worldSelection.HideImmediately();
            _portal.SetState(HubBaguaPortalGraphic.PortalState.Idle, true);
            _portal.SetState(HubBaguaPortalGraphic.PortalState.Opening);
            _transferEffect.Play();
            WanjieScroll.Audio.AudioManager.PlaySfxSafe(
                WanjieScroll.Audio.SfxType.FormationActivate);
        }

        /// <summary>Editor-only preview of the full first Hub restoration.</summary>
        public void ReplayArrivalPresentation()
        {
            if (!ArrivalPreviewReady)
                return;

            _worldSelection?.HideImmediately();
            _tianluRegistryPanel?.HideImmediately();
            _arrivalCoordinator.ReplayConfiguredSequence();
        }

        /// <summary>Editor-only fast preview of the realm awakening VFX.</summary>
        public void ReplayArrivalRestorationEffect()
        {
            if (!ArrivalPreviewReady)
                return;

            _worldSelection?.HideImmediately();
            _tianluRegistryPanel?.HideImmediately();
            _arrivalCoordinator.ReplayRestorationEffectOnly();
        }

        /// <summary>Editor and final panel control for returning to Hub idle.</summary>
        public void ClosePortalPresentation()
        {
            if (!PortalPreviewReady)
                return;

            _worldSelection.Hide();
            _transferEffect.Close();
        }

        /// <summary>Applies profile changes even while the Editor is paused.</summary>
        public void RefreshPortalPresentationPreview()
        {
            if (_activePresentation == null)
                _activePresentation = _portalPresentation;
            ApplyPortalPresentation();
        }

        /// <summary>Applies safe first-restoration VFX values without replaying.</summary>
        public void RefreshArrivalRestorationPreview()
        {
            _arrivalRestorationEffect?.RefreshTuningPreview();
        }

        private Vector2 GetStageArtSize()
        {
            if (_stageArtRect == null || _stageArtRect.rect.width <= 0f ||
                _stageArtRect.rect.height <= 0f)
                return new Vector2(1920f, 1080f);
            return _stageArtRect.rect.size;
        }

        private static void PlaceAtArtCoordinate(
            RectTransform rect,
            Vector2 topLeftCoordinate,
            Vector2 size,
            Vector2 stageSize)
        {
            float scaleX = stageSize.x / 1920f;
            float scaleY = stageSize.y / 1080f;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size.x * scaleX, size.y * scaleY);
            rect.anchoredPosition = new Vector2(
                (topLeftCoordinate.x - 960f) * scaleX,
                (540f - topLeftCoordinate.y) * scaleY);
        }

        private static Vector2 ToCanvasCoordinate(Vector2 topLeftCoordinate)
        {
            return new Vector2(
                topLeftCoordinate.x - 960f,
                540f - topLeftCoordinate.y);
        }
    }
}
