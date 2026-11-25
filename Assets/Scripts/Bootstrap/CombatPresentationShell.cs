using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Effects.Map;
using WanjieScroll.UI;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns the floor-scoped, reusable battle presentation hierarchy. Preparing
    /// the shell creates no CombatController, encounter, turn or reward state.
    /// Encounter coordinators and their subscriptions remain scene-bootstrap
    /// owned and must be released before binding another combat to this shell.
    /// This module owns the static view instances and their between-battle reset;
    /// each view still owns its own animation, actor and callback cancellation.
    /// </summary>
    public sealed class CombatPresentationShell
    {
        private readonly Transform _owner;
        private readonly Camera _camera;
        private readonly BattleHudPresentationProfile _hudProfile;
        private readonly CardView _cardPrefab;
        private readonly CardPresentationProfile _cardProfile;
        private readonly CrawlerResidentLayoutProfile _crawlerLayout;
        private bool _prepared;
        private CombatController _boundCombat;

        public Canvas Canvas { get; private set; }
        public Transform WorldRoot { get; private set; }
        public BattlefieldView Battlefield { get; private set; }
        public BattleHudView Hud { get; private set; }
        public HandView Hand { get; private set; }
        public PlayPileView PlayPile { get; private set; }
        public BattleBoardHudLayout BoardHudLayout { get; private set; }
        public CrawlerResidentPresenter Crawlers { get; private set; }

        public CombatPresentationShell(
            Transform owner,
            Camera camera,
            Canvas canvas,
            Transform worldRoot,
            BattleHudPresentationProfile hud,
            CardView cardPrefab,
            CardPresentationProfile cards,
            CrawlerResidentLayoutProfile crawlers)
        {
            _owner = owner != null ? owner :
                throw new ArgumentNullException(nameof(owner));
            _camera = camera != null ? camera :
                throw new ArgumentNullException(nameof(camera));
            Canvas = canvas;
            WorldRoot = worldRoot;
            _hudProfile = hud;
            _cardPrefab = cardPrefab;
            _cardProfile = cards;
            _crawlerLayout = crawlers;
        }

        /// <summary>Builds the floor's fixed board and UI once, without a run.</summary>
        public void Prepare(MapPreviewProfile map)
        {
            if (_prepared)
                return;

            if (WorldRoot == null)
            {
                var world = new GameObject("WorldSpace");
                world.transform.SetParent(_owner, false);
                WorldRoot = world.transform;
            }
            PrepareCanvas();
            Battlefield = BattlefieldView.Create(
                WorldRoot,
                _camera,
                null,
                map != null ? map.battleBackground : null,
                map != null ? map.battleBackgroundTint : Color.white,
                map != null && map.battleTopFogEnabled,
                map != null && map.overrideBattleGridColor
                    ? (Color?)map.battleGridColor : null);
            Battlefield.EnemyArea.PrepareEmptyBoard();
            Battlefield.EnemyArea.ConfigureTooltipCanvas(Canvas);
            BoardHudLayout = BattleBoardHudLayout.Create(
                Canvas, Battlefield.EnemyArea, _camera);
            Crawlers = CrawlerResidentPresenter.Create(
                Canvas.transform, _crawlerLayout);
            BoardHudLayout.AttachCrawler(Crawlers, _crawlerLayout);
            Hud = BattleHudFactory.Create(Canvas.transform, _hudProfile);
            if (Hud == null)
                throw new InvalidOperationException("无法创建共用战斗 HUD。");
            Hand = BattleHandFactory.Create(
                Canvas.transform, _cardPrefab, _cardProfile);
            Hand.deckCountText = Hud.deckCountText;
            Hand.discardCountText = Hud.discardCountText;
            PlayPile = PlayPileView.Create(Canvas.transform);
            BoardHudLayout.AttachPile(PlayPile);
            Hand.playPileView = PlayPile;
            Hand.SetInteractionEnabled(false);
            _prepared = true;
        }

        public void BindEncounter(
            CombatController combat,
            IEnumerable<string> crawlerContentIds)
        {
            if (!_prepared)
                throw new InvalidOperationException("战斗表现框架尚未准备。");
            if (combat == null)
                throw new ArgumentNullException(nameof(combat));
            if (_boundCombat != null)
                throw new InvalidOperationException("上一场战斗表现尚未释放。");

            Battlefield.EnemyArea.Bind(combat);
            Crawlers.Prepare(crawlerContentIds);
            Crawlers.Bind(combat);
            _boundCombat = combat;
        }

        /// <summary>
        /// Removes encounter identity and late callbacks while retaining the
        /// board, HUD, hand slots and layout for the next battle on this floor.
        /// </summary>
        public void ReleaseEncounter()
        {
            if (!_prepared)
                return;

            // Scene teardown may destroy child views before the bootstrap.
            // Unity's overloaded null check is required; ?. would still invoke
            // methods on a managed wrapper whose native component is gone.
            if (Hand != null)
            {
                Hand.SetInteractionEnabled(false);
                Hand.Unbind();
                Hand.SetCardClickCallback(null);
                Hand.SetHoverCallback(null, null);
                Hand.SetPresentationPaused(false);
            }
            if (PlayPile != null)
            {
                PlayPile.ClearPile();
                PlayPile.ConfigureInspection(null);
            }
            if (Crawlers != null)
                Crawlers.ReleaseEncounter();
            if (Battlefield != null && Battlefield.EnemyArea != null)
                Battlefield.EnemyArea.ReleaseEncounter();
            if (Hud != null)
                Hud.ResetEncounterPresentation();
            _boundCombat = null;
        }

        private void PrepareCanvas()
        {
            if (Canvas == null)
            {
                var root = new GameObject("Canvas", typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                root.transform.SetParent(_owner, false);
                Canvas = root.GetComponent<Canvas>();
            }

            // Authored scene and reused floor canvases share the same fit rule.
            var scaler = Canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = Canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.worldCamera = _camera;
            Canvas.planeDistance = 100f;
            if (Canvas.sortingOrder == 0)
                Canvas.sortingOrder = 100;
            var raycaster = Canvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
                raycaster = Canvas.gameObject.AddComponent<GraphicRaycaster>();
            raycaster.ignoreReversedGraphics = false;
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
        }
    }
}
