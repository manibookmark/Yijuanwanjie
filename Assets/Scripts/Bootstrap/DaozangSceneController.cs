using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.UI.Daozang;
using WanjieScroll.UI.Components;

namespace WanjieScroll.UI.Daozang
{
    /// <summary>
    /// 道藏场景装配入口。
    /// 创建 Canvas、EventSystem（基类处理），加载 MetaProgression，
    /// 构建 DaozangController + 加载 PresentationProfile，
    /// 交由 DaozangFactory 装配 UI。
    /// </summary>
    public sealed class DaozangSceneController : SceneBootstrapBase
    {
        private DaozangView _daozangView;
        private DaozangController _controller;
        private DaozangPresentationProfile _profile;
        private MetaProgression _meta;
        private GameSaveService _saveService;
        private Button _backButton;
        private bool _returnRequested;

        protected override void OnSceneEnter()
        {
            LoadMetaProgression();
            LoadPresentationProfile();

            _controller = new DaozangController(_meta, _saveService);
            _daozangView = DaozangFactory.Create(
                Canvas.transform, _controller, _profile);
            _backButton = CreateDaozangBackButton();
            _daozangView?.BindUtilityTooltip(
                _backButton, "返回仙域\n快捷键：Esc");
        }

        private Button CreateDaozangBackButton()
        {
            Button button = UIElementFactory.CreateButton(
                "BackToHubButton",
                Canvas.transform,
                RuntimeArtLoader.LoadSprite(
                    "Sprites/UI/Daozang/utility-icons/back-xianyu"));
            button.onClick.AddListener(HandleBackRequested);

            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            Button overview = _daozangView != null
                ? _daozangView.overviewButton
                : null;
            RectTransform overviewRect = overview != null
                ? overview.GetComponent<RectTransform>()
                : null;
            rect.sizeDelta = overviewRect != null
                ? overviewRect.sizeDelta
                : new Vector2(67f, 67f);
            rect.anchoredPosition = overviewRect != null
                ? new Vector2(-overviewRect.anchoredPosition.x,
                    overviewRect.anchoredPosition.y)
                : new Vector2(71f, -60f);

            Image image = button.GetComponent<Image>();
            image.color = Color.white;
            image.preserveAspect = true;
            if (overview != null)
            {
                button.transition = overview.transition;
                button.colors = overview.colors;
            }
            return button;
        }

        private void LoadMetaProgression()
        {
            _meta = new MetaProgression();
            _saveService = GameSaveService.CreateDefault();

            if (_saveService.TryLoad(_meta, out string error))
            {
                GameSession.Current.PlayerLingyu = _meta.lingyu;
                return;
            }

            Debug.LogWarning("[Daozang] 未能读取账号，使用未解锁的新账号状态：" + error);
            // A failed load must never let the empty fallback overwrite the account.
            _meta = new MetaProgression();
            _saveService = null;
            GameSession.Current.PlayerLingyu = _meta.lingyu;
        }

        private void LoadPresentationProfile()
        {
            _profile = Resources.Load<DaozangPresentationProfile>(
                DaozangPresentationProfile.DefaultResourcePath);

            if (_profile == null)
            {
                Debug.LogWarning("[Daozang] PresentationProfile not found, creating runtime default");
                _profile = ScriptableObject.CreateInstance<DaozangPresentationProfile>();
            }
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            HandleBackRequested();
        }

        private void HandleBackRequested()
        {
            if (_daozangView != null && _daozangView.TryCloseTopmostPanel())
                return;
            ReturnToHub();
        }

        private void ReturnToHub()
        {
            if (_returnRequested)
                return;
            _returnRequested = true;
            SceneLoader.GoToHub();
        }

        protected override void OnSceneExit()
        {
            // Controller is plain C#, no explicit cleanup needed.
            // View is a MonoBehaviour child of Canvas, destroyed by Unity.
            _backButton = null;
        }
    }
}
