using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 真实战斗的世界空间表现组件。
    /// 负责背景渲染，敌人区域委托给 EnemyAreaView。
    /// </summary>
    public sealed class BattlefieldView : MonoBehaviour
    {
        private SpriteRenderer _backgroundRenderer;

        public string BackgroundSpriteName =>
            _backgroundRenderer != null && _backgroundRenderer.sprite != null
                ? _backgroundRenderer.sprite.name
                : string.Empty;
        public EnemyAreaView EnemyArea { get; private set; }

        public static BattlefieldView Create(
            Transform parent,
            Camera camera,
            CombatController combat)
        {
            return Create(
                parent,
                camera,
                combat,
                null,
                Color.white,
                false);
        }

        public static BattlefieldView Create(
            Transform parent,
            Camera camera,
            CombatController combat,
            Sprite battleBackground,
            Color backgroundTint)
        {
            return Create(
                parent,
                camera,
                combat,
                battleBackground,
                backgroundTint,
                false);
        }

        public static BattlefieldView Create(
            Transform parent,
            Camera camera,
            CombatController combat,
            Sprite battleBackground,
            Color backgroundTint,
            bool battleTopFogEnabled,
            Color? battleGridColor = null)
        {
            var root = new GameObject("Battlefield");
            root.transform.SetParent(parent, false);
            BattlefieldView view = root.AddComponent<BattlefieldView>();
            view.BuildBackground(camera, battleBackground, backgroundTint);
            view.EnemyArea = EnemyAreaView.Create(
                root.transform,
                combat,
                battleTopFogEnabled,
                battleGridColor);
            view.EnemyArea.ConfigureBossAttackCloseup(camera, view._backgroundRenderer);
            return view;
        }

        private void BuildBackground(Camera camera, Sprite battleBackground, Color backgroundTint)
        {
            var background = new GameObject(
                "BattleBackground",
                typeof(SpriteRenderer));
            background.transform.SetParent(transform, false);
            background.transform.localPosition = new Vector3(0f, 0f, 8f);
            _backgroundRenderer = background.GetComponent<SpriteRenderer>();
            _backgroundRenderer.sortingOrder = -20;

            // Use injected background; fall back to RuntimeArtLoader if null
            if (battleBackground != null)
            {
                _backgroundRenderer.sprite = battleBackground;
                _backgroundRenderer.color = backgroundTint;
            }
            else
            {
                _backgroundRenderer.sprite = RuntimeArtLoader.GetBattleBackground();
                if (_backgroundRenderer.sprite == null)
                {
                    _backgroundRenderer.color = new Color(0.08f, 0.06f, 0.05f);
                    Debug.LogError("[BattlefieldView] 战斗背景 sprite 为 null，使用纯色回退。");
                    return;
                }
            }

            float halfHeight =
                camera != null && camera.orthographic
                    ? camera.orthographicSize
                    : 5.4f;
            float aspect = camera != null ? camera.aspect : 16f / 9f;
            float viewHeight = halfHeight * 2f;
            float viewWidth = viewHeight * aspect;
            Vector2 spriteSize = _backgroundRenderer.sprite.bounds.size;
            // Combat composition is authored against a 16:9 logical stage.
            // Keep the vertical stage fixed and let the camera reveal more or
            // less width as the aspect ratio changes.  Do not use a generic
            // Cover scale here: on ultrawide displays it enlarges the sprite
            // until the top/bottom of the authored battlefield are cropped.
            float scale = viewHeight / Mathf.Max(0.01f, spriteSize.y);
            background.transform.localScale =
                new Vector3(scale, scale, 1f);

            float coveredWidth = spriteSize.x * scale;
            // 容差用相对比例：背景是 1672x941，比精确 16:9 窄约 0.05%（约 0.5px），
            // 属导出精度问题，不该按"需要扩图"处理；真正需要扩图的是明显窄于
            // 16:9 的图（例如 4:3 图配 16:9 舞台差 25%）。原来的绝对 0.01 世界
            // 单位容差在 16:9 舞台上刚好被这 0.5px 顶破，导致每次战斗都报一次。
            const float CoverageTolerance = 0.002f;
            if (coveredWidth < viewWidth * (1f - CoverageTolerance))
            {
                Debug.LogWarning(
                    "[BattlefieldView] 战斗背景宽度不足以覆盖当前画幅；"
                    + "请为该楼层制作包含16:9核心区的宽版扩图。"
                    + $" sprite={coveredWidth:F2}, view={viewWidth:F2}");
            }
        }
    }
}
