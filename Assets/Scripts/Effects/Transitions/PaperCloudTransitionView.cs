using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Transitions
{
    /// <summary>
    /// 将一张静态纹理切成三角碎片，并按“中心先破、向外飞散”的顺序绘制。
    /// 所有碎片共用一个 UI Graphic 和一次纹理采样，不依赖额外美术素材。
    /// </summary>
        [DisallowMultipleComponent]
        public sealed class ScreenTearGraphic : MaskableGraphic
        {
            private sealed class Fragment
            {
                public Vector2 Position0;
                public Vector2 Position1;
                public Vector2 Position2;
                public Vector2 Uv0;
                public Vector2 Uv1;
                public Vector2 Uv2;
                public Vector2 Center;
                public Vector2 Direction;
                public float Delay;
                public float Travel;
                public float Rotation;
                public float Fall;
            }

        private readonly List<Fragment> _fragments = new List<Fragment>(168);
        private Texture _frameTexture;
        private Rect _sourceUvRect = new Rect(0f, 0f, 1f, 1f);
        private Rect _layoutRect;
            private int _columns = 12;
            private int _rows = 7;
            private float _tearProgress;
            private bool _loggedFirstMesh;

            public override Texture mainTexture =>
                _frameTexture != null ? _frameTexture : s_WhiteTexture;

            public Texture FrameTexture => _frameTexture;
            public int FragmentCount => _columns * _rows * 2;
            public float TearProgress => _tearProgress;

            /// <summary>绑定当前画面并生成指定密度的程序化三角碎片。</summary>
        public void SetFrame(Texture texture, int columns = 12, int rows = 7)
        {
            SetFrame(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                columns,
                rows);
        }

        /// <summary>绑定纹理中的指定区域，供直接使用 Sprite 资源而无需生成截图。</summary>
        public void SetFrame(
            Texture texture,
            Rect sourceUvRect,
            int columns = 12,
            int rows = 7)
        {
            _frameTexture = texture;
            _sourceUvRect = sourceUvRect;
            _columns = Mathf.Max(2, columns);
                _rows = Mathf.Max(2, rows);
                _layoutRect = default;
                _fragments.Clear();
                _tearProgress = 0f;
                _loggedFirstMesh = false;
                SetMaterialDirty();
                SetVerticesDirty();
            }

            /// <summary>设置 0（完整画面）到 1（完全撕开）的进度。</summary>
            public void SetTearProgress(float progress)
            {
                float next = Mathf.Clamp01(progress);
                if (Mathf.Approximately(_tearProgress, next)) return;
                _tearProgress = next;
                SetVerticesDirty();
            }

            /// <summary>解除截图纹理，避免跨场景纹理被长期持有。</summary>
            public void ClearFrame()
            {
                _frameTexture = null;
                _tearProgress = 0f;
                SetMaterialDirty();
                SetVerticesDirty();
            }

            protected override void OnPopulateMesh(VertexHelper vertexHelper)
            {
                vertexHelper.Clear();
                if (_frameTexture == null) return;

                Rect rect = GetPixelAdjustedRect();
                EnsureLayout(rect);

                if (!_loggedFirstMesh)
                {
                    _loggedFirstMesh = true;
                    Debug.Log(
                        $"[ScreenTearTransition] 首次绘制网格：" +
                        $"rect={rect.width:0.#}x{rect.height:0.#}，" +
                        $"fragments={_fragments.Count}。");
                }

                for (int i = 0; i < _fragments.Count; i++)
                    AddFragment(vertexHelper, _fragments[i]);
            }

            private void EnsureLayout(Rect rect)
            {
                if (_fragments.Count == FragmentCount && _layoutRect == rect)
                    return;

                _layoutRect = rect;
                _fragments.Clear();

                float cellWidth = rect.width / _columns;
                float cellHeight = rect.height / _rows;
                int fragmentIndex = 0;

                for (int row = 0; row < _rows; row++)
                {
                    float y0 = rect.yMin + row * cellHeight;
                    float y1 = y0 + cellHeight;
                    float v0 = Mathf.Lerp(
                        _sourceUvRect.yMin,
                        _sourceUvRect.yMax,
                        row / (float)_rows);
                    float v1 = Mathf.Lerp(
                        _sourceUvRect.yMin,
                        _sourceUvRect.yMax,
                        (row + 1) / (float)_rows);

                    for (int column = 0; column < _columns; column++)
                    {
                        float x0 = rect.xMin + column * cellWidth;
                        float x1 = x0 + cellWidth;
                        float u0 = Mathf.Lerp(
                            _sourceUvRect.xMin,
                            _sourceUvRect.xMax,
                            column / (float)_columns);
                        float u1 = Mathf.Lerp(
                            _sourceUvRect.xMin,
                            _sourceUvRect.xMax,
                            (column + 1) / (float)_columns);

                        Vector2 p00 = new Vector2(x0, y0);
                        Vector2 p10 = new Vector2(x1, y0);
                        Vector2 p01 = new Vector2(x0, y1);
                        Vector2 p11 = new Vector2(x1, y1);
                        Vector2 uv00 = new Vector2(u0, v0);
                        Vector2 uv10 = new Vector2(u1, v0);
                        Vector2 uv01 = new Vector2(u0, v1);
                        Vector2 uv11 = new Vector2(u1, v1);

                        bool forwardDiagonal = Hash01(fragmentIndex, 3) >= 0.5f;
                        if (forwardDiagonal)
                        {
                            AddLayoutFragment(
                                p00, p10, p11,
                                uv00, uv10, uv11,
                                rect, fragmentIndex++);
                            AddLayoutFragment(
                                p00, p11, p01,
                                uv00, uv11, uv01,
                                rect, fragmentIndex++);
                        }
                        else
                        {
                            AddLayoutFragment(
                                p00, p10, p01,
                                uv00, uv10, uv01,
                                rect, fragmentIndex++);
                            AddLayoutFragment(
                                p10, p11, p01,
                                uv10, uv11, uv01,
                                rect, fragmentIndex++);
                        }
                    }
                }
            }

            private void AddLayoutFragment(
                Vector2 position0,
                Vector2 position1,
                Vector2 position2,
                Vector2 uv0,
                Vector2 uv1,
                Vector2 uv2,
                Rect rect,
                int index)
            {
                Vector2 center = (position0 + position1 + position2) / 3f;
                Vector2 fromScreenCenter = center - rect.center;
                Vector2 normalized = new Vector2(
                    fromScreenCenter.x / Mathf.Max(1f, rect.width * 0.5f),
                    fromScreenCenter.y / Mathf.Max(1f, rect.height * 0.5f));
                float distance = Mathf.Clamp01(normalized.magnitude / 1.41421356f);

                Vector2 direction = fromScreenCenter.sqrMagnitude > 0.001f
                    ? fromScreenCenter.normalized
                    : Vector2.up;
                float angle = Mathf.Lerp(-18f, 18f, Hash01(index, 11));
                direction = Rotate(direction, angle * Mathf.Deg2Rad);

                float minDimension = Mathf.Min(rect.width, rect.height);
                float rotationMagnitude = Mathf.Lerp(
                    65f,
                    210f,
                    Hash01(index, 17));

                _fragments.Add(new Fragment
                {
                    Position0 = position0,
                    Position1 = position1,
                    Position2 = position2,
                    Uv0 = uv0,
                    Uv1 = uv1,
                    Uv2 = uv2,
                    Center = center,
                    Direction = direction,
                    Delay = distance * 0.28f + Hash01(index, 23) * 0.055f,
                    Travel = minDimension * Mathf.Lerp(
                        0.34f,
                        0.78f,
                        Hash01(index, 29)),
                    Rotation = (Hash01(index, 31) >= 0.5f ? 1f : -1f) *
                               rotationMagnitude,
                    Fall = minDimension * Mathf.Lerp(
                        0.035f,
                        0.14f,
                        Hash01(index, 37))
                });
            }

            private void AddFragment(VertexHelper vertexHelper, Fragment fragment)
            {
                float localProgress = Mathf.Clamp01(
                    (_tearProgress - fragment.Delay) /
                    Mathf.Max(0.001f, 1f - fragment.Delay));
                float eased = 1f - Mathf.Pow(1f - localProgress, 3f);
                float angle = fragment.Rotation * Mathf.Deg2Rad * eased;
                float scale = Mathf.Lerp(1f, 0.62f, eased);
                Vector2 offset = fragment.Direction * fragment.Travel * eased;
                offset.y -= fragment.Fall * eased * eased;

                float fade = 1f - Mathf.SmoothStep(0.48f, 1f, localProgress);
                Color32 vertexColor = color;
                vertexColor.a = (byte)Mathf.RoundToInt(vertexColor.a * fade);

                int vertexIndex = vertexHelper.currentVertCount;
                AddVertex(
                    vertexHelper,
                    TransformPoint(fragment.Position0, fragment.Center, offset, angle, scale),
                    fragment.Uv0,
                    vertexColor);
                AddVertex(
                    vertexHelper,
                    TransformPoint(fragment.Position1, fragment.Center, offset, angle, scale),
                    fragment.Uv1,
                    vertexColor);
                AddVertex(
                    vertexHelper,
                    TransformPoint(fragment.Position2, fragment.Center, offset, angle, scale),
                    fragment.Uv2,
                    vertexColor);
                vertexHelper.AddTriangle(vertexIndex, vertexIndex + 1, vertexIndex + 2);
            }

            private static void AddVertex(
                VertexHelper vertexHelper,
                Vector2 position,
                Vector2 uv,
                Color32 color)
            {
                UIVertex vertex = UIVertex.simpleVert;
                vertex.position = position;
                vertex.uv0 = uv;
                vertex.color = color;
                vertexHelper.AddVert(vertex);
            }

            private static Vector2 TransformPoint(
                Vector2 point,
                Vector2 center,
                Vector2 offset,
                float angle,
                float scale)
            {
                Vector2 local = (point - center) * scale;
                local = Rotate(local, angle);
                return center + local + offset;
            }

            private static Vector2 Rotate(Vector2 value, float angle)
            {
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);
                return new Vector2(
                    value.x * cos - value.y * sin,
                    value.x * sin + value.y * cos);
            }

            private static float Hash01(int index, int salt)
            {
                uint value = unchecked((uint)(index * 374761393 + salt * 668265263));
                value = (value ^ (value >> 13)) * 1274126177u;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777215f;
            }
        }

    /// <summary>
    /// 无卡顿跨场景撕纸遮幕。将地图背景分为左右两半，
    /// 进入战斗时从中间向两边撕开，返回时从两边向中间拼合。
    /// 不截图、不读回、不创建粒子系统。纸片位置与遮幕状态由本类唯一拥有；
    /// 入战预览/柔雾由独立 BattlePreparationMistView 拥有，不把雾的材质和节拍叠入此类。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PaperCloudTransitionView : MonoBehaviour, ISceneTransitionCurtain
    {
        private static readonly bool LoadingStatusEnabled = false;
        private enum CurtainState
        {
            Covering,
            Covered,
            PreparingBattle,
            Revealing,
            Revealed
        }

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private RawImage _leftPanel;
        private RawImage _rightPanel;
        private RectTransform _leftRect;
        private RectTransform _rightRect;
        private SceneTransitionProfile _profile;
        private Sprite _coverSprite;
        private Color _coverTint = Color.white;
        private CurtainState _state = CurtainState.Revealed;
        private float _animTimer;
        private float _cachedPanelWidth;
        private Texture2D _fallbackTexture;
        private bool _coverWasAnimated;
        private float _currentRevealDuration = 0.35f;
        private RectTransform _statusRoot;
        private Text _statusLabel;
        private string _statusCaption = "载入中";
        private float _statusDotTimer;
        private int _statusDotCount;
        private BattlePreparationMistView _battleMist;
        private bool _battlePreparationActive;
        private bool _battleOpeningComplete;

        public bool IsCovered => _state == CurtainState.Covered;
        public bool IsRevealed => _state == CurtainState.Revealed;
        /// <summary>地图纸片已打开到安全、不透出真实场景的准备背景，可以启动重工作。</summary>
        public bool IsPreparationFeedbackComplete =>
            _battlePreparationActive && _battleOpeningComplete;

        public void Initialize(SceneTransitionProfile profile)
        {
            _profile = profile;

            var canvasGo = new GameObject(
                "PaperTearCurtain",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            // 左面板：覆盖左半屏，pivot 在右边缘（撕纸中线）
            _leftPanel = CreatePanel("LeftTearPanel", canvasGo.transform);
            _leftRect = _leftPanel.rectTransform;
            _leftRect.anchorMin = Vector2.zero;
            _leftRect.anchorMax = new Vector2(0.5f, 1f);
            _leftRect.pivot = new Vector2(1f, 0.5f);
            _leftRect.offsetMin = Vector2.zero;
            _leftRect.offsetMax = Vector2.zero;

            // 右面板：覆盖右半屏，pivot 在左边缘（撕纸中线）
            _rightPanel = CreatePanel("RightTearPanel", canvasGo.transform);
            _rightRect = _rightPanel.rectTransform;
            _rightRect.anchorMin = new Vector2(0.5f, 0f);
            _rightRect.anchorMax = Vector2.one;
            _rightRect.pivot = new Vector2(0f, 0.5f);
            _rightRect.offsetMin = Vector2.zero;
            _rightRect.offsetMax = Vector2.zero;

            // 状态层随转场运行时一起建好：字体加载与组件创建都属于一次性
            // 装配成本，不能留到玩家点击那一帧。
            EnsureStatusLayer();

            var mistGo = new GameObject("BattlePreparationMist", typeof(RectTransform));
            mistGo.transform.SetParent(canvasGo.transform, false);
            RectTransform mistRect = (RectTransform)mistGo.transform;
            mistRect.anchorMin = Vector2.zero;
            mistRect.anchorMax = Vector2.one;
            mistRect.offsetMin = mistRect.offsetMax = Vector2.zero;
            mistRect.SetAsFirstSibling();
            _battleMist = mistGo.AddComponent<BattlePreparationMistView>();
            _battleMist.Initialize(_statusLabel != null ? _statusLabel.font : null);

            _state = CurtainState.Revealed;
        }

        /// <summary>
        /// 玩家点击即启动纸幕，下面只显示传入的缓存战斗背景与柔雾。
        /// 后续 Cover 不重置此阶段；只有 Ready 后的 Reveal 才允许透出真实场景。
        /// </summary>
        public void BeginBattlePreparation(Sprite background, Color tint)
        {
            _battlePreparationActive = true;
            _battleOpeningComplete = false;
            _coverWasAnimated = false;
            _animTimer = 0f;
            // A previous map reload may have left a caption such as
            // "正在前往下一层". Battle entry owns a separate visual language;
            // never carry navigation/loading copy into the mist transition.
            _statusCaption = string.Empty;
            HideStatus();
            if (_canvas == null || _battleMist == null)
            {
                _state = CurtainState.Covered;
                _battleOpeningComplete = true;
                return;
            }

            ApplyCoverSprite();
            if (_leftPanel.texture == null)
                ApplyFallbackCover();
            _battleMist.Begin(background, tint);
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
            _state = CurtainState.PreparingBattle;
            // Battle entry gets a slightly more readable opening than the
            // short map return: it is the player's first visual beat, while
            // hidden combat preparation continues behind the opaque fog.
            _currentRevealDuration = _profile != null
                ? Mathf.Clamp(_profile.revealDuration * 1.5f, 0.30f, 0.40f)
                : 0.34f;
            CachePanelWidth();
            ResetPanelPositions();
        }

        private static RawImage CreatePanel(string name, Transform parent)
        {
            var go = new GameObject(name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = true;
            return image;
        }

        /// <summary>设置下一次转场使用的地图背景；只保存资源引用，不复制纹理。</summary>
        public void SetCoverSprite(Sprite sprite, Color tint)
        {
            _coverSprite = sprite;
            _coverTint = tint;
            if (_state == CurtainState.Covered)
                ApplyCoverSprite();
        }

        /// <summary>
        /// 遮幕合上期间显示的文案。撕纸遮幕用的是地图背景图本身，
        /// 没有文案时玩家会以为还停在地图上；调用方按去向给出具体文案。
        /// </summary>
        public void SetCoverCaption(string caption)
        {
            if (!string.IsNullOrWhiteSpace(caption))
                _statusCaption = caption;
            RefreshStatusLabel();
        }

        private void EnsureStatusLayer()
        {
            if (_statusRoot != null || _canvas == null)
                return;

            var rootGo = new GameObject("TransitionStatus",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            rootGo.transform.SetParent(_canvas.transform, false);
            _statusRoot = (RectTransform)rootGo.transform;
            _statusRoot.anchorMin = Vector2.zero;
            _statusRoot.anchorMax = Vector2.one;
            _statusRoot.offsetMin = Vector2.zero;
            _statusRoot.offsetMax = Vector2.zero;
            Image scrim = rootGo.GetComponent<Image>();
            scrim.color = new Color(0.02f, 0.03f, 0.045f, 0.62f);
            scrim.raycastTarget = true;

            var labelGo = new GameObject("Caption",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelGo.transform.SetParent(_statusRoot, false);
            _statusLabel = labelGo.GetComponent<Text>();
            Font font = Resources.Load<Font>("Fonts/NotoSerifSC-Medium") ??
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _statusLabel.font = font;
            _statusLabel.fontSize = 34;
            _statusLabel.alignment = TextAnchor.MiddleCenter;
            _statusLabel.color = new Color(0.94f, 0.93f, 0.86f, 1f);
            _statusLabel.raycastTarget = false;
            _statusLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _statusLabel.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform labelRect = _statusLabel.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(0f, -24f);
            labelRect.sizeDelta = new Vector2(960f, 80f);

            RefreshStatusLabel();
            _statusRoot.gameObject.SetActive(false);
        }

        private void RefreshStatusLabel()
        {
            if (_statusLabel == null)
                return;
            _statusLabel.text = _statusDotCount > 0
                ? _statusCaption + new string('.', _statusDotCount)
                : _statusCaption;
        }

        private void ShowStatus()
        {
            // 试玩复审：暂时关闭“载入中/正在前往下一层”状态层，
            // 不改变纸幕、请求身份、输入阻塞或目的场景 Ready 合同。
            if (!LoadingStatusEnabled) { HideStatus(); return; }
            EnsureStatusLayer();
            if (_statusRoot == null)
                return;
            _statusDotTimer = 0f;
            _statusDotCount = 0;
            RefreshStatusLabel();
            _statusRoot.gameObject.SetActive(true);
            // 状态层必须盖在左右撕纸面板之上，否则会被地图背景图挡住。
            _statusRoot.SetAsLastSibling();
        }

        private void HideStatus()
        {
            if (_statusRoot != null)
                _statusRoot.gameObject.SetActive(false);
        }

        /// <summary>遮幕合上且无动画时，用省略号证明主线程仍在推进。</summary>
        private void UpdateStatus()
        {
            if (_statusRoot == null || !_statusRoot.gameObject.activeSelf)
                return;
            _statusDotTimer += Time.unscaledDeltaTime;
            if (_statusDotTimer < 0.45f)
                return;
            _statusDotTimer = 0f;
            _statusDotCount = (_statusDotCount + 1) % 4;
            RefreshStatusLabel();
        }

        public void Cover()
        {
            // 入战的纸幕从点击即开始；Core 随后的 Cover 不能把它合回去或显示 Loading。
            if (_battlePreparationActive)
                return;
            // 已在覆盖中或已覆盖——忽略（返回转场时合拢动画可能已在进行）
            if (_state == CurtainState.Covering || _state == CurtainState.Covered)
                return;

            if (_canvas == null)
            {
                _state = CurtainState.Covered;
                return;
            }

            ApplyCoverSprite();
            if (_leftPanel == null || _leftPanel.texture == null)
            {
                ApplyFallbackCover();
            }

            if (_leftPanel == null || _leftPanel.texture == null)
            {
                _state = CurtainState.Covered;
                return;
            }

            // 瞬间覆盖全屏，不播放合拢动画——地图画面本来就在，直接撕开
            _state = CurtainState.Covered;
            _coverWasAnimated = false;
            _animTimer = 0f;
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
            ResetPanelPositions();
            ShowStatus();
        }

        /// <summary>带合拢动画的覆盖：面板从屏幕两侧滑入至中线闭合。</summary>
        public void CoverAnimated()
        {
            if (_battlePreparationActive)
                SnapRevealed();
            if (_state == CurtainState.Covering || _state == CurtainState.Covered)
                return;

            if (_canvas == null)
            {
                _state = CurtainState.Covered;
                return;
            }

            ApplyCoverSprite();
            if (_leftPanel == null || _leftPanel.texture == null)
            {
                ApplyFallbackCover();
            }

            if (_leftPanel == null || _leftPanel.texture == null)
            {
                _state = CurtainState.Covered;
                return;
            }

            _state = CurtainState.Covering;
            _coverWasAnimated = true;
            _animTimer = 0f;
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
            CachePanelWidth();

            // 面板从屏幕外侧滑入
            _leftRect.anchoredPosition = new Vector2(1f - _cachedPanelWidth, 0f);
            _rightRect.anchoredPosition = new Vector2(-1f + _cachedPanelWidth, 0f);
            _leftRect.localRotation = Quaternion.identity;
            _rightRect.localRotation = Quaternion.identity;

            Debug.Log(
                $"[PaperTear] 合拢地图：" +
                $"sprite={_coverSprite?.name}，" +
                $"duration={(_profile != null && _profile.returnCoverDuration > 0f ? _profile.returnCoverDuration : 0.50f):0.###}s。");
            ShowStatus();
        }

        public void Reveal()
        {
            if (_battlePreparationActive)
            {
                _state = CurtainState.Revealing;
                HideStatus();
                if (_battleMist != null)
                    _battleMist.Reveal();
                else
                    SnapRevealed();
                return;
            }
            if (_leftPanel == null || _leftPanel.texture == null)
            {
                SnapRevealed();
                return;
            }

            // 返回转场：合拢后直接隐藏面板，不再播放撕开动画——
            // 合拢已经完成了"覆盖"，地图场景激活后面板直接消失即可。
            if (_coverWasAnimated)
            {
                Debug.Log(
                    $"[PaperTear] 返回转场：合拢后直接揭幕（无撕开动画）。" +
                    $"sprite={_coverSprite?.name}。");
                SnapRevealed();
                return;
            }

            if (_profile != null)
            {
                _currentRevealDuration =
                    Mathf.Max(0.01f, _profile.revealDuration);
            }
            else
            {
                _currentRevealDuration = 0.35f;
            }

            _state = CurtainState.Revealing;
            _animTimer = 0f;
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
            CachePanelWidth();
            HideStatus();

            _leftRect.anchoredPosition = new Vector2(1f, 0f);
            _rightRect.anchoredPosition = new Vector2(-1f, 0f);
            _leftRect.localRotation = Quaternion.identity;
            _rightRect.localRotation = Quaternion.identity;

            Debug.Log(
                $"[PaperTear] 撕开地图：" +
                $"sprite={_coverSprite?.name}，" +
                $"duration={_currentRevealDuration:0.###}s。");
        }

        public void SnapCovered()
        {
            ResetBattlePreparation();
            ApplyCoverSprite();
            _state = CurtainState.Covered;
            _coverWasAnimated = false;
            _animTimer = 0f;
            if (_canvas != null)
            {
                bool hasCover = _leftPanel != null &&
                                _leftPanel.texture != null;
                _canvasGroup.alpha = hasCover ? 1f : 0f;
                _canvasGroup.blocksRaycasts = hasCover;
            }
            ResetPanelPositions();
            ShowStatus();
        }

        public void SnapRevealed()
        {
            ResetBattlePreparation();
            _state = CurtainState.Revealed;
            _coverWasAnimated = false;
            _animTimer = 0f;
            HideStatus();
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
            }
            if (_leftPanel != null)
                _leftPanel.texture = null;
            if (_rightPanel != null)
                _rightPanel.texture = null;
        }

        private void ResetBattlePreparation()
        {
            _battlePreparationActive = false;
            _battleOpeningComplete = false;
            _battleMist?.Hide();
        }

        private void CachePanelWidth()
        {
            _cachedPanelWidth = _leftRect.rect.width;
            if (_cachedPanelWidth < 1f)
                _cachedPanelWidth = Screen.width * 0.5f;
        }

        private void ApplyFallbackCover()
        {
            if (_leftPanel == null || _rightPanel == null) return;

            if (_fallbackTexture == null)
            {
                const int size = 8;
                _fallbackTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "PaperTearFallback",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point
                };
                var dark = new Color(0.06f, 0.04f, 0.03f, 1f);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    _fallbackTexture.SetPixel(x, y, dark);
                _fallbackTexture.Apply();
            }

            _leftPanel.texture = _fallbackTexture;
            _leftPanel.uvRect = new Rect(0f, 0f, 0.5f, 1f);
            _leftPanel.color = Color.white;

            _rightPanel.texture = _fallbackTexture;
            _rightPanel.uvRect = new Rect(0.5f, 0f, 0.5f, 1f);
            _rightPanel.color = Color.white;

            Debug.Log("[PaperTear] 使用纯黑回退遮幕。");
        }

        private void ResetPanelPositions()
        {
            if (_leftRect != null)
            {
                _leftRect.anchoredPosition = new Vector2(1f, 0f);
                _leftRect.localRotation = Quaternion.identity;
            }
            if (_rightRect != null)
            {
                _rightRect.anchoredPosition = new Vector2(-1f, 0f);
                _rightRect.localRotation = Quaternion.identity;
            }
        }

        private void ApplyCoverSprite()
        {
            if (_leftPanel == null || _rightPanel == null) return;
            if (_coverSprite == null || _coverSprite.texture == null)
            {
                _leftPanel.texture = null;
                _rightPanel.texture = null;
                return;
            }

            Texture texture = _coverSprite.texture;
            Rect spriteRect = _coverSprite.textureRect;
            float uMin = spriteRect.x / texture.width;
            float uMax = (spriteRect.x + spriteRect.width) / texture.width;
            float vMin = spriteRect.y / texture.height;
            float vMax = (spriteRect.y + spriteRect.height) / texture.height;
            float uMid = (uMin + uMax) * 0.5f;

            // 左右面板 UV 微重叠 1px，消除中缝
            float uvOverlap = 0.5f / texture.width;

            _leftPanel.texture = texture;
            _leftPanel.uvRect = new Rect(
                uMin, vMin,
                uMid - uMin + uvOverlap, vMax - vMin);
            _leftPanel.color = _coverTint;

            _rightPanel.texture = texture;
            _rightPanel.uvRect = new Rect(
                uMid - uvOverlap, vMin,
                uMax - uMid + uvOverlap, vMax - vMin);
            _rightPanel.color = _coverTint;

            Debug.Log(
                $"[PaperTear] 已绑定地图背景：{_coverSprite.name}，" +
                "左右分半撕纸。");
        }

        private void Update()
        {
            if (_battlePreparationActive)
            {
                if (_state == CurtainState.PreparingBattle)
                    UpdateRevealing();
                else if (_state == CurtainState.Revealing &&
                         (_battleMist == null || _battleMist.IsRevealed))
                    SnapRevealed();
                return;
            }
            if (_state == CurtainState.Covering)
                UpdateCovering();
            else if (_state == CurtainState.Revealing)
                UpdateRevealing();
            else if (_state == CurtainState.Covered)
                UpdateStatus();
        }

        private void UpdateCovering()
        {
            _animTimer += Time.unscaledDeltaTime;
            // 返回合拢用 returnCoverDuration（若为 0 则回退 coverDuration）
            float duration;
            if (_profile != null)
            {
                duration = _profile.returnCoverDuration > 0f
                    ? _profile.returnCoverDuration
                    : _profile.coverDuration;
                duration = Mathf.Max(0.01f, duration);
            }
            else
            {
                duration = 0.50f;
            }
            float progress = Mathf.Clamp01(_animTimer / duration);
            // ease-in：慢起加速，与 Reveal 的 ease-out 互逆
            float eased = progress * progress;

            float startLeft = 1f - _cachedPanelWidth;
            float endLeft = 1f;
            float startRight = -1f + _cachedPanelWidth;
            float endRight = -1f;

            _leftRect.anchoredPosition = new Vector2(
                Mathf.Lerp(startLeft, endLeft, eased), 0f);
            _rightRect.anchoredPosition = new Vector2(
                Mathf.Lerp(startRight, endRight, eased), 0f);

            // 微旋转：从 3° 回到 0°（与 Reveal 反向）
            float rotation = 3f * (1f - eased);
            _leftRect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            _rightRect.localRotation = Quaternion.Euler(0f, 0f, -rotation);

            if (progress >= 1f)
            {
                _state = CurtainState.Covered;
                ResetPanelPositions();
            }
        }

        private void UpdateRevealing()
        {
            _animTimer += Time.unscaledDeltaTime;
            float duration = _currentRevealDuration;
            float progress = Mathf.Clamp01(_animTimer / duration);
            float eased = 1f - Mathf.Pow(1f - progress, 2f);

            float slideDistance = _cachedPanelWidth * eased;
            _leftRect.anchoredPosition = new Vector2(1f - slideDistance, 0f);
            _rightRect.anchoredPosition = new Vector2(-1f + slideDistance, 0f);

            // 微旋转模拟纸角翘起（最大 3°）
            float rotation = 3f * eased;
            _leftRect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            _rightRect.localRotation = Quaternion.Euler(0f, 0f, -rotation);

            if (progress >= 1f)
            {
                if (_battlePreparationActive)
                {
                    _battleOpeningComplete = true;
                    _state = CurtainState.Covered;
                }
                else
                    SnapRevealed();
            }
        }

        private void OnDestroy()
        {
            ResetBattlePreparation();
            if (_leftPanel != null)
                _leftPanel.texture = null;
            if (_rightPanel != null)
                _rightPanel.texture = null;
            if (_fallbackTexture != null)
            {
                Object.DestroyImmediate(_fallbackTexture);
                _fallbackTexture = null;
            }
        }
    }
}
