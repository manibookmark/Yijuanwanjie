using System.Collections.Generic;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 正式战斗 HUD 组件。外部只更新语义状态，不直接操作内部图片或文字层级。
    /// 规模审查：文件超过 500 行但仍只拥有 HUD 状态投影；独立动画状态机由
    /// ArmorShieldView、BuffBarView、MaxHealthGainView 等子模块持有，本类只
    /// 暴露窄语义入口并注入已有目标组件。
    /// 2026-10-17：跨战复用只增加 HUD 投影自身的释放入口；资源转场、
    /// 护甲与增益分别由既有子 owner 取消，不在此持有第二套回执状态机。
    /// 2026-10-21：本类已有的受击与经验条动画补齐停用收尾；隐藏时只
    /// 暂存同一经验段供重新显示，不提交规则或奖励。再增加独立表现前拆分。
    /// </summary>
    public sealed class BattleHudView : MonoBehaviour
    {
        // 编辑模式用 Background 预览效果，运行时关闭（真实背景是世界空间 SpriteRenderer）
        private void Awake()
        {
            var bg = transform.Find("Background");
            if (bg != null) bg.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            // A disabled HUD must not retain intermediate resource snapshots.
            // The shared transfer owners separately cancel their own particles.
            CommitDeferredHealthDisplay();
            CommitDeferredManaDisplay();
            StopExperienceAnimation(discardPending: false);
            if (_playerHitAnimation != null)
                StopCoroutine(_playerHitAnimation);
            _playerHitAnimation = null;
            RestorePlayerHitBaseline();
        }

        private void OnEnable()
        {
            if (_hasPendingExperienceSegment)
                _progressAnimation = StartCoroutine(AnimateExperienceSegmentRoutine(
                    _pendingExperienceSegment, _experienceAnimationVersion));
        }

        [Header("顶部")]
        public Image progressEmptyImage;
        public Image progressFullImage;
        public Text waveText;
        public Text comboText;

        [Header("连击倍率")]
        [Tooltip("出牌堆上方的大号×N，在 Prefab 里设置位置和样式")]
        public Text comboMultiplierText;
        [Tooltip("毛笔数字连击显示组件，优先使用")]
        public BrushComboView brushComboView;

        [Header("英灵与状态")]
        public Image heroAvatarImage;
        public Image heroFrameImage;
        public Image heroInfoPanelImage;
        public Image heroInfoLeftCapImage;
        public Image heroInfoCenterTileImage;
        public Image heroInfoRightCapImage;
        public Text heroInfoText;

        [Header("增益图标栏")]
        public BuffBarView leftBuffBar;
        public BuffBarView rightBuffBar;
        [Tooltip("每行图标数")]
        public int buffIconsPerRow = 4;
        [Tooltip("图标间距")]
        public Vector2 buffIconSpacing2D = new Vector2(6f, 6f);
        [Tooltip("图标大小")]
        public Vector2 buffIconSize = new Vector2(48f, 48f);
        [Tooltip("数值字号")]
        public int buffValueFontSize = 14;

        [Header("资源")]
        public Image hpEmptyImage;
        public Image hpFullImage;
        public Text hpText;
        public Image mpEmptyImage;
        public Image mpFullImage;
        public Text mpText;

        [Header("护甲盾")]
        public ArmorShieldView armorShieldView;

        [Header("牌区计数")]
        public Text deckCountText;
        public Text discardCountText;

        [Header("操作")]
        public Button drawButton;
        public Button playAllButton;
        public Button endTurnButton;
        public Button discardButton;

        [Header("费用预览")]
        public Text costPreviewText; // hover卡牌时在MP右侧显示费用

        [Header("表现资产")]
        [SerializeField] private BattleHudPresentationProfile presentation;
        private Coroutine _progressAnimation;
        private CombatExperienceSegmentResult _pendingExperienceSegment;
        private Action _experienceCompletion;
        private bool _hasPendingExperienceSegment;
        private int _experienceAnimationVersion;
        private Coroutine _playerHitAnimation;
        private MaxHealthGainView _healthGainView;
        private ManaGainView _manaGainView;
        private bool _hasDeferredHealthDisplay;
        private int _deferredCurrentHp;
        private int _deferredMaxHp;
        private bool _hasDeferredManaDisplay;
        private int _deferredMana;
        private bool _hasPlayerHitBaseline;
        private Vector3 _playerHitBaseScale;
        private Color _playerHitAvatarColor;
        private Color _playerHitHpColor;

        /// <summary>
        /// Cancels the old encounter's feedback without completing experience
        /// or gain receipts into a later combat bound to this shared HUD.
        /// </summary>
        public void ResetEncounterPresentation()
        {
            StopExperienceAnimation(discardPending: true);
            if (_playerHitAnimation != null)
                StopCoroutine(_playerHitAnimation);
            _progressAnimation = null;
            _playerHitAnimation = null;
            RestorePlayerHitBaseline();
            _hasDeferredHealthDisplay = false;
            _hasDeferredManaDisplay = false;
            foreach (ManaGainView transfer in
                GetComponentsInChildren<ManaGainView>(true))
                transfer.CancelPendingPresentation();
            armorShieldView?.ResetEncounterPresentation();
            leftBuffBar?.ResetEncounterPresentation();
            rightBuffBar?.ResetEncounterPresentation();
            HideCostPreview();
            brushComboView?.ResetEncounterPresentation();
        }

        /// <summary>只读返回灵力显示中心，供外部表现定位，不暴露可变UI组件。</summary>
        public bool TryGetManaScreenPosition(out Vector2 position)
        {
            position = default;
            RectTransform target = mpFullImage != null
                ? mpFullImage.rectTransform
                : mpText != null ? mpText.rectTransform : null;
            Canvas canvas = target != null ? target.GetComponentInParent<Canvas>() : null;
            if (target == null || canvas == null)
                return false;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera;
            position = RectTransformUtility.WorldToScreenPoint(
                camera, target.TransformPoint(target.rect.center));
            return true;
        }

        public bool TryGetStatIconScreenPosition(StatType stat, out Vector2 position)
        {
            position = default;
            return (leftBuffBar != null && leftBuffBar.TryGetStatIconScreenPosition(stat, out position)) ||
                (rightBuffBar != null && rightBuffBar.TryGetStatIconScreenPosition(stat, out position));
        }

        public RectTransform ProgressTarget =>
            progressFullImage != null
                ? progressFullImage.rectTransform
                : progressEmptyImage != null
                    ? progressEmptyImage.rectTransform
                    : null;

        public void ApplyPresentation(BattleHudPresentationProfile profile)
        {
            presentation = profile != null
                ? profile
                : BattleHudPresentationProfile.LoadDefault();
            if (presentation == null) return;

            SetSprite(progressEmptyImage, presentation.progressEmpty);
            SetSprite(progressFullImage, presentation.progressFull);
            SetSprite(heroAvatarImage, presentation.heroAvatar);
            SetSprite(heroFrameImage, presentation.heroAvatarFrame);
            SetSprite(heroInfoPanelImage, presentation.heroInfoPanel);
            SetSprite(
                heroInfoLeftCapImage,
                presentation.heroInfoLeftCap);
            SetSprite(
                heroInfoCenterTileImage,
                presentation.heroInfoCenterTile);
            SetSprite(
                heroInfoRightCapImage,
                presentation.heroInfoRightCap);
            if (heroInfoPanelImage != null)
            {
                heroInfoPanelImage.color =
                    new Color(1f, 1f, 1f, 0.28f);
            }
            if (heroInfoCenterTileImage != null)
            {
                heroInfoCenterTileImage.preserveAspect = false;
                heroInfoCenterTileImage.type = Image.Type.Tiled;
            }
            SetSprite(hpEmptyImage, presentation.hpEmpty);
            SetSprite(hpFullImage, presentation.hpFull);
            SetSprite(mpEmptyImage, presentation.mpEmpty);
            SetSprite(mpFullImage, presentation.mpFull);

            ConfigureButton(
                drawButton,
                presentation.drawNormal,
                presentation.drawHover);
            ConfigureButton(
                playAllButton,
                presentation.playAllNormal,
                presentation.playAllHover);
            ConfigureButton(
                endTurnButton,
                presentation.endTurnNormal,
                presentation.endTurnHover);
            ConfigureButton(
                discardButton,
                presentation.discardNormal,
                presentation.discardHover);

            ApplyTextStyle(
                waveText,
                presentation.titleFont,
                presentation.progressFontSize,
                presentation.primaryTextColor);
            ApplyTextStyle(
                comboText,
                presentation.titleFont,
                presentation.progressFontSize,
                presentation.primaryTextColor);
            ApplyTextStyle(
                heroInfoText,
                presentation.bodyFont,
                presentation.statusFontSize,
                presentation.primaryTextColor);
            ApplyTextStyle(
                hpText,
                presentation.titleFont,
                presentation.resourceFontSize,
                presentation.resourceTextColor);
            ApplyTextStyle(
                mpText,
                presentation.titleFont,
                presentation.resourceFontSize,
                presentation.resourceTextColor);

            SetupBuffBars();
        }

        /// <summary>
        /// 在 prefab 已有的 leftBuffBar/rightBuffBar 容器内填充 BuffIconView。
        /// 容器结构和位置在 Prefab 编辑模式调整，这里只创建图标子物体。
        /// </summary>
        private void SetupBuffBars()
        {
            if (presentation == null) return;

            if (leftBuffBar != null)
                PopulateBuffBar(leftBuffBar, BuffBarView.Side.Left);
            if (rightBuffBar != null)
                PopulateBuffBar(rightBuffBar, BuffBarView.Side.Right);
        }

        private void PopulateBuffBar(BuffBarView bar, BuffBarView.Side side)
        {
            var glyphs = side == BuffBarView.Side.Left
                ? PowerUpGlyphConfig.LeftBarGlyphs
                : PowerUpGlyphConfig.RightBarGlyphs;

            var icons = new BuffIconView[glyphs.Length];

            for (int i = 0; i < glyphs.Length; i++)
            {
                // 优先查找 prefab 中已有的子物体（按 glyph ID 命名）
                Transform existing = bar.transform.Find(glyphs[i].Id);
                GameObject iconGo;

                if (existing != null)
                {
                    // 绑定已有子物体
                    iconGo = existing.gameObject;
                    // 清除占位文字
                    var existingText = iconGo.transform.Find("Value")?.GetComponent<Text>();
                    if (existingText != null) existingText.text = "";
                    // 无条件清除占位灰色，统一设为白色
                    var existingImg = iconGo.transform.Find("Icon")?.GetComponent<Image>();
                    if (existingImg != null)
                        existingImg.color = Color.white;
                }
                else
                {
                    // 运行时 fallback：创建新子物体
                    iconGo = CreateIconGo(bar.transform, glyphs[i].Id, i);
                }

                EnsureBuffBackground(iconGo);
                var iconImage = iconGo.transform.Find("Icon")?.GetComponent<Image>();
                var valueText = iconGo.transform.Find("Value")?.GetComponent<Text>();

                // 补充缺失的组件
                if (iconImage == null)
                {
                    var imgGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    imgGo.transform.SetParent(iconGo.transform, false);
                    imgGo.GetComponent<RectTransform>().sizeDelta = buffIconSize;
                    iconImage = imgGo.GetComponent<Image>();
                    iconImage.preserveAspect = true;
                    iconImage.raycastTarget = false;
                }
                if (valueText == null)
                {
                    var valGo = new GameObject("Value", typeof(RectTransform), typeof(Text));
                    valGo.transform.SetParent(iconGo.transform, false);
                    valGo.GetComponent<RectTransform>().sizeDelta = new Vector2(buffIconSize.x + 10f, 18f);
                    valueText = valGo.GetComponent<Text>();
                    valueText.font = Resources.Load<Font>("Fonts/NotoSerifSC-Medium")
                        ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    valueText.fontSize = buffValueFontSize;
                    valueText.raycastTarget = false;
                    valueText.alignment = TextAnchor.MiddleCenter;
                    valueText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    valueText.verticalOverflow = VerticalWrapMode.Overflow;
                }

                // 获取或添加 BuffIconView
                var icon = iconGo.GetComponent<BuffIconView>();
                if (icon == null) icon = iconGo.AddComponent<BuffIconView>();
                icon.IconImage = iconImage;
                icon.ValueText = valueText;

                Sprite sprite = Resources.Load<Sprite>(
                    PowerUpGlyphConfig.SpritePathPrefix + glyphs[i].Id);
                Color activeColor = presentation != null ? presentation.buffActiveColor : Color.white;
                Color inactiveColor = presentation != null ? presentation.buffInactiveColor : new Color(0.15f, 0.12f, 0.08f, 0.5f);
                icon.Setup(glyphs[i], sprite, activeColor, inactiveColor, buffValueFontSize);

                iconGo.SetActive(false);
                icons[i] = icon;
            }

            bar.Initialize(side, glyphs, icons);
        }

        private GameObject CreateIconGo(Transform parent, string id, int index)
        {
            int perRow = Mathf.Max(1, buffIconsPerRow);
            int col = index % perRow;
            int row = index / perRow;

            var iconGo = new GameObject(id, typeof(RectTransform), typeof(VerticalLayoutGroup));
            iconGo.transform.SetParent(parent, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0f, 1f);
            iconRt.anchorMax = new Vector2(0f, 1f);
            iconRt.pivot = new Vector2(0f, 1f);
            iconRt.sizeDelta = new Vector2(buffIconSize.x + 4f, buffIconSize.y + 22f);
            iconRt.anchoredPosition = new Vector2(
                col * (buffIconSize.x + 4f + buffIconSpacing2D.x),
                -row * (buffIconSize.y + 22f + buffIconSpacing2D.y));

            var vlg = iconGo.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            return iconGo;
        }

        /// <summary>
        /// Prefab 中部分旧增益底板仍是左下角 6×6 的占位 Rect。
        /// 运行时统一补齐并拉伸，确保左右增益栏都具有稳定、可读的色块底板。
        /// </summary>
        private void EnsureBuffBackground(GameObject iconGo)
        {
            Transform background = iconGo.transform.Find("Bg");
            if (background == null)
            {
                var backgroundGo = new GameObject(
                    "Bg",
                    typeof(RectTransform),
                    typeof(Image));
                backgroundGo.transform.SetParent(iconGo.transform, false);
                background = backgroundGo.transform;
            }

            background.SetAsFirstSibling();
            var rect = background.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            var image = background.GetComponent<Image>();
            if (image == null) image = background.gameObject.AddComponent<Image>();
            image.color = presentation != null
                ? presentation.buffBackgroundColor
                : new Color(0.1f, 0.08f, 0.06f, 0.68f);
            image.raycastTarget = false;
            image.preserveAspect = false;

            var layout = background.GetComponent<LayoutElement>();
            if (layout == null)
                layout = background.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
        }

        public void SetStageProgress(
            float normalizedProgress,
            int wave,
            int combo)
        {
            if (progressFullImage != null)
                progressFullImage.fillAmount =
                    Mathf.Clamp01(normalizedProgress);
            if (waveText != null)
                waveText.text = wave.ToString();
            if (comboText != null)
                comboText.text = $"连击 ×{Mathf.Max(0, combo)}";
        }

        /// <summary>
        /// Animates one authoritative Core experience segment. UI receives
        /// both endpoints and never derives level thresholds itself.
        /// </summary>
        public void AnimateExperienceSegment(
            CombatExperienceSegmentResult segment,
            Action onComplete)
        {
            StopExperienceAnimation(discardPending: true);
            _pendingExperienceSegment = segment;
            _experienceCompletion = onComplete;
            _hasPendingExperienceSegment = true;
            if (isActiveAndEnabled)
                _progressAnimation = StartCoroutine(
                    AnimateExperienceSegmentRoutine(segment, _experienceAnimationVersion));
        }

        private void StopExperienceAnimation(bool discardPending)
        {
            unchecked { _experienceAnimationVersion++; }
            if (_progressAnimation != null)
                StopCoroutine(_progressAnimation);
            _progressAnimation = null;
            if (!discardPending) return;
            _hasPendingExperienceSegment = false;
            _experienceCompletion = null;
            _pendingExperienceSegment = default;
        }

        public void SetExperienceSnapshot(
            int level,
            int experience,
            int requiredExperience)
        {
            if (progressFullImage != null)
            {
                progressFullImage.fillAmount = requiredExperience > 0
                    ? Mathf.Clamp01((float)experience / requiredExperience)
                    : 0f;
            }
            if (waveText != null)
                waveText.text = $"Lv.{Mathf.Max(1, level)}";
        }

        private IEnumerator AnimateExperienceSegmentRoutine(
            CombatExperienceSegmentResult segment,
            int version)
        {
            float from = segment.RequiredExperienceBefore > 0
                ? Mathf.Clamp01(
                    (float)segment.ExperienceBefore /
                    segment.RequiredExperienceBefore)
                : 0f;
            float to = segment.TriggeredLevelUp
                ? 1f
                : segment.RequiredExperienceAfter > 0
                    ? Mathf.Clamp01(
                        (float)segment.ExperienceAfter /
                        segment.RequiredExperienceAfter)
                    : 0f;
            float duration = Mathf.Lerp(
                0.18f,
                0.34f,
                Mathf.Abs(to - from));
            float elapsed = 0f;
            SetExperienceSnapshot(
                segment.LevelBefore,
                segment.ExperienceBefore,
                segment.RequiredExperienceBefore);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                if (progressFullImage != null)
                    progressFullImage.fillAmount = Mathf.Lerp(from, to, eased);
                yield return null;
            }

            if (progressFullImage != null)
                progressFullImage.fillAmount = to;
            if (!segment.TriggeredLevelUp)
            {
                SetExperienceSnapshot(
                    segment.LevelAfter,
                    segment.ExperienceAfter,
                    segment.RequiredExperienceAfter);
            }

            if (version != _experienceAnimationVersion)
                yield break;
            _progressAnimation = null;
            _hasPendingExperienceSegment = false;
            Action completion = _experienceCompletion;
            _experienceCompletion = null;
            _pendingExperienceSegment = default;
            completion?.Invoke();
        }

        /// <summary>
        /// hover 卡牌时显示支付预览；返灵只显示支付owner预测的实际入账。
        /// </summary>
        public void ShowCostPreview(int cost, int creditedAmount)
        {
            if (costPreviewText != null)
            {
                if (cost > 0 || creditedAmount > 0)
                {
                    costPreviewText.text = cost > 0 ? $"-{cost}" : $"+{creditedAmount}";
                    costPreviewText.color = new Color(1f, 0.86f, 0.48f); // 经验条金色
                    costPreviewText.gameObject.SetActive(true);
                }
                else
                {
                    costPreviewText.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// hover 退出时隐藏费用预览
        /// </summary>
        public void HideCostPreview()
        {
            if (costPreviewText != null)
                costPreviewText.gameObject.SetActive(false);
        }

        public void SetResources(
            int currentHp,
            int maxHp,
            int currentMana,
            int maxMana,
            bool deferResourceDisplay = false)
        {
            // A post-play seal may refresh state after a resource gain but before
            // the card's typed receipt. Each resource retains its own snapshot
            // until its transfer arrives, or a final/non-card refresh submits it.
            if (deferResourceDisplay ||
                (_healthGainView != null && _healthGainView.IsAwaitingArrival))
            {
                _deferredCurrentHp = currentHp;
                _deferredMaxHp = maxHp;
                _hasDeferredHealthDisplay = true;
            }
            else
            {
                SetResource(
                    hpFullImage,
                    hpText,
                    currentHp,
                    maxHp);
                _hasDeferredHealthDisplay = false;
            }
            if (deferResourceDisplay ||
                (_manaGainView != null && _manaGainView.IsAwaitingArrival))
            {
                _deferredMana = currentMana;
                _hasDeferredManaDisplay = true;
            }
            else
            {
                SetManaResource(mpFullImage, mpText, currentMana);
                _hasDeferredManaDisplay = false;
            }
            _ = maxMana;
        }

        /// <summary>
        /// 将实际回血与最大生命收益合并为一次 HP 粒子转场。两项正收益的
        /// 总量只决定粒子密度；当前生命与上限始终由 Core 的状态快照提交。
        /// </summary>
        public void PlayHealthGain(int healAmount, int maxHealthGained)
        {
            int amount = (int)Math.Min(int.MaxValue,
                (long)Math.Max(0, healAmount) + Math.Max(0, maxHealthGained));
            if (amount <= 0 || hpFullImage == null)
                return;
            if (_healthGainView == null)
            {
                _healthGainView = GetComponent<MaxHealthGainView>();
                if (_healthGainView == null)
                    _healthGainView =
                        gameObject.AddComponent<MaxHealthGainView>();
            }
            _healthGainView.Bind(hpFullImage);
            _healthGainView.Play(amount, CommitDeferredHealthDisplay);
        }

        private void CommitDeferredHealthDisplay()
        {
            if (!_hasDeferredHealthDisplay)
                return;
            SetResource(
                hpFullImage,
                hpText,
                _deferredCurrentHp,
                _deferredMaxHp);
            _hasDeferredHealthDisplay = false;
        }

        /// <summary>
        /// 将实际获得的临时灵力飞入 MP。灵力是可继续累加的绝对值，HUD 不把
        /// 它钳制到基础灵力，也不使用“当前/上限”的显示口径。
        /// </summary>
        public void PlayManaGain(int amount)
        {
            if (amount <= 0 || mpFullImage == null)
                return;
            if (_manaGainView == null)
            {
                _manaGainView = GetComponent<ManaGainView>();
                if (_manaGainView == null)
                    _manaGainView = gameObject.AddComponent<ManaGainView>();
            }
            _manaGainView.Bind(mpFullImage);
            _manaGainView.Play(amount, CommitDeferredManaDisplay);
        }

        public Action<Vector2> PrepareManaGain(int amount)
        {
            if (amount <= 0 || mpFullImage == null) return null;
            if (_manaGainView == null)
                _manaGainView = GetComponent<ManaGainView>() ?? gameObject.AddComponent<ManaGainView>();
            _manaGainView.Bind(mpFullImage);
            return _manaGainView.Prepare(amount, CommitDeferredManaDisplay);
        }

        private void CommitDeferredManaDisplay()
        {
            if (!_hasDeferredManaDisplay)
                return;
            SetManaResource(mpFullImage, mpText, _deferredMana);
            _hasDeferredManaDisplay = false;
        }

        /// <summary>Uses the existing deferred MP owner for the sand-symbol
        /// skin; no rule value is written by this presentation.</summary>
        public void PlaySandManaGain(int amount, Vector2 cardScreenPosition)
        {
            if (amount <= 0 || mpFullImage == null) return;
            if (_manaGainView == null)
                _manaGainView = GetComponent<ManaGainView>() ?? gameObject.AddComponent<ManaGainView>();
            _manaGainView.Bind(mpFullImage);
            _manaGainView.PlaySand(amount, cardScreenPosition, CommitDeferredManaDisplay);
        }

        /// <summary>
        /// Brief player-hit response owned by the HUD. Health has already
        /// changed in Core; this only punches and flashes the avatar/HP ink.
        /// </summary>
        public void PlayPlayerHitFeedback(int damage)
        {
            if (damage <= 0 || !isActiveAndEnabled)
                return;
            if (_playerHitAnimation != null)
                StopCoroutine(_playerHitAnimation);
            if (!_hasPlayerHitBaseline)
            {
                _playerHitBaseScale = heroAvatarImage != null
                    ? heroAvatarImage.rectTransform.localScale : Vector3.one;
                _playerHitAvatarColor = heroAvatarImage != null
                    ? heroAvatarImage.color : Color.white;
                _playerHitHpColor = hpFullImage != null
                    ? hpFullImage.color : Color.white;
                _hasPlayerHitBaseline = true;
            }
            _playerHitAnimation = StartCoroutine(PlayerHitFeedback());
        }

        private void RestorePlayerHitBaseline()
        {
            if (!_hasPlayerHitBaseline)
                return;
            if (heroAvatarImage != null)
            {
                heroAvatarImage.rectTransform.localScale = _playerHitBaseScale;
                heroAvatarImage.color = _playerHitAvatarColor;
            }
            if (hpFullImage != null)
                hpFullImage.color = _playerHitHpColor;
            _hasPlayerHitBaseline = false;
        }

        private IEnumerator PlayerHitFeedback()
        {
            RectTransform avatar = heroAvatarImage != null
                ? heroAvatarImage.rectTransform
                : null;
            Vector3 baseScale = _playerHitBaseScale;
            Color avatarColor = _playerHitAvatarColor;
            Color hpColor = _playerHitHpColor;
            const float punchDuration = 0.07f;
            const float recoverDuration = 0.13f;

            float elapsed = 0f;
            while (elapsed < punchDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / punchDuration);
                if (avatar != null)
                    avatar.localScale = Vector3.Lerp(
                        baseScale,
                        baseScale * 1.08f,
                        1f - Mathf.Pow(1f - t, 3f));
                if (heroAvatarImage != null)
                    heroAvatarImage.color = Color.Lerp(
                        avatarColor,
                        new Color(1f, 0.42f, 0.32f, avatarColor.a),
                        t);
                if (hpFullImage != null)
                    hpFullImage.color = Color.Lerp(
                        hpColor,
                        new Color(1f, 0.24f, 0.16f, hpColor.a),
                        t);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < recoverDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / recoverDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                if (avatar != null)
                    avatar.localScale = Vector3.Lerp(
                        baseScale * 1.08f,
                        baseScale,
                        eased);
                if (heroAvatarImage != null)
                    heroAvatarImage.color = Color.Lerp(
                        new Color(1f, 0.42f, 0.32f, avatarColor.a),
                        avatarColor,
                        eased);
                if (hpFullImage != null)
                    hpFullImage.color = Color.Lerp(
                        new Color(1f, 0.24f, 0.16f, hpColor.a),
                        hpColor,
                        eased);
                yield return null;
            }

            if (avatar != null)
                avatar.localScale = baseScale;
            if (heroAvatarImage != null)
                heroAvatarImage.color = avatarColor;
            if (hpFullImage != null)
                hpFullImage.color = hpColor;
            _hasPlayerHitBaseline = false;
            _playerHitAnimation = null;
        }

        /// <summary>
        /// 更新护甲盾显示。由 CombatSceneController.RefreshBattleHud 调用。
        /// </summary>
        public void SetArmor(int armor)
        {
            if (armorShieldView != null)
                armorShieldView.UpdateArmor(armor);
        }

        public void SetHeroInfo(int handBonus, int remainingTriggers)
        {
            // Hero gains are already shown by their corresponding HUD glyphs.
            if (heroInfoText != null) heroInfoText.gameObject.SetActive(false);
        }

        /// <summary>
        /// 刷新左右两侧增益图标栏。
        /// </summary>
        public void RefreshBuffs(
            PlayerStats stats,
            Func<StatType, int> getCurrentStatValue = null)
        {
            leftBuffBar?.Refresh(stats, getCurrentStatValue);
            rightBuffBar?.Refresh(stats, getCurrentStatValue);
        }

        /// <summary>Mid-card draws/progression can observe the whole resident
        /// reaction batch before its individual receipts have been published.
        /// Wait for the resident or card boundary before committing buff numbers;
        /// resources and hand presentation can continue to refresh independently.</summary>
        public void RefreshBuffsForStateChange(PlayerStats stats,
            Func<StatType, int> getCurrentStatValue, CombatStateChangeReason reason, BattlePhase phase)
        {
            if (phase == BattlePhase.CardResolving &&
                reason != CombatStateChangeReason.CrawlerChanged &&
                reason != CombatStateChangeReason.CardPlayed) return;
            RefreshBuffs(stats, getCurrentStatValue);
        }

        /// <summary>
        /// Routes authoritative card stat changes to the bar that owns each
        /// StatType. Adding another stat card only requires content config and
        /// an existing glyph mapping; no card-name branching is needed.
        /// </summary>
        public void PlayStatGains(
            IReadOnlyList<CombatStatChange> statChanges,
            RectTransform source = null)
        {
            if (statChanges == null) return;

            for (int i = 0; i < statChanges.Count; i++)
            {
                CombatStatChange change = statChanges[i];
                if (leftBuffBar != null &&
                    leftBuffBar.PlayStatGain(change, source))
                    continue;
                rightBuffBar?.PlayStatGain(change, source);
            }
        }

        /// <summary>UI owns numeric holds and transfer versions. Effects only
        /// supplies the screen position when an artifact finishes manifesting.</summary>
        public Action<Vector2> PrepareStatGains(IReadOnlyList<CombatStatChange> changes)
        {
            var arrivals = new List<Action<Vector2>>();
            if (changes != null)
                for (int i = 0; i < changes.Count; i++)
                {
                    Action<Vector2> arrival = leftBuffBar?.PrepareStatGain(changes[i]) ??
                        rightBuffBar?.PrepareStatGain(changes[i]);
                    if (arrival != null) arrivals.Add(arrival);
                }
            return screen =>
            {
                for (int i = 0; i < arrivals.Count; i++) arrivals[i](screen);
            };
        }

        /// <summary>预览卡牌的增益效果（hover 时调用）。</summary>
        public void PreviewBuffs(List<BuffPreview> previews)
        {
            ClearBuffPreview();
            if (previews == null || previews.Count == 0) return;

            foreach (var p in previews)
            {
                leftBuffBar?.ShowPreview(p.statType, p.projectedValue);
                rightBuffBar?.ShowPreview(p.statType, p.projectedValue);
            }
        }

        /// <summary>清除增益预览（hover 退出时调用）。</summary>
        public void ClearBuffPreview()
        {
            leftBuffBar?.ClearPreview();
            rightBuffBar?.ClearPreview();
        }

        private static void SetResource(
            Image fullImage,
            Text valueText,
            int current,
            int maximum)
        {
            int safeMaximum = Mathf.Max(0, maximum);
            int safeCurrent = Mathf.Clamp(current, 0, safeMaximum);
            if (fullImage != null)
            {
                fullImage.fillAmount = safeMaximum > 0
                    ? (float)safeCurrent / safeMaximum
                    : 0f;
            }
            if (valueText != null)
                valueText.text = safeCurrent.ToString();
        }

        private static void SetManaResource(
            Image fullImage,
            Text valueText,
            int current)
        {
            int safeCurrent = Mathf.Max(0, current);
            if (fullImage != null)
                fullImage.fillAmount = safeCurrent > 0 ? 1f : 0f;
            if (valueText != null)
                valueText.text = safeCurrent.ToString();
        }

        private static void ConfigureButton(
            Button button,
            Sprite normal,
            Sprite hover)
        {
            if (button == null) return;
            var image = button.targetGraphic as Image;
            if (image != null)
            {
                image.sprite = normal;
                image.color = Color.white;
                image.preserveAspect = true;
            }

            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = hover,
                selectedSprite = hover,
                disabledSprite = normal
            };

            BattleHudButtonFeedback feedback =
                button.GetComponent<BattleHudButtonFeedback>();
            if (feedback == null)
                feedback = button.gameObject.AddComponent<BattleHudButtonFeedback>();
            feedback.Bind(image);
        }

        private static void SetSprite(Image image, Sprite sprite)
        {
            if (image == null) return;
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : Color.clear;
            image.preserveAspect = true;
        }

        private static void ApplyTextStyle(
            Text text,
            Font font,
            int fontSize,
            Color color)
        {
            if (text == null) return;
            if (font != null) text.font = font;
            text.fontSize = fontSize;
            text.color = color;
        }
    }
}
