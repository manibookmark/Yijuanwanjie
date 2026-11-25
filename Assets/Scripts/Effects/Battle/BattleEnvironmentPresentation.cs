using UnityEngine;
using WanjieScroll.Effects.Map;

namespace WanjieScroll.Effects.Battle
{
    /// <summary>
    /// 战斗环境表现。根据楼层节点配置构建可选前景和低强度雾层。
    /// 调用方（Bootstrap）注入节点配置与相机；UI 不引用 Effects。
    /// 节点无配置时不生成额外对象。
    /// </summary>
    public static class BattleEnvironmentPresentation
    {
        /// <summary>
        /// 在指定父物体下构建战斗环境点缀。
        /// </summary>
        /// <param name="parent">环境对象父级（战斗世界层）。</param>
        /// <param name="node">楼层节点配置。可为空。</param>
        /// <param name="camera">用于对齐前景到屏幕的相机。</param>
        /// <returns>是否生成了额外对象。</returns>
        public static bool Build(Transform parent, MapPreviewNode node, Camera camera)
        {
            if (node == null) return false;

            bool builtAnything = false;

            if (node.battleForeground != null)
            {
                BuildForeground(parent, node, camera);
                builtAnything = true;
            }

            if (node.battleFogIntensity > 0.001f)
            {
                BuildFog(parent, node, camera);
                builtAnything = true;
            }

            return builtAnything;
        }

        private static void BuildForeground(Transform parent, MapPreviewNode node, Camera camera)
        {
            var go = new GameObject("BattleForeground", typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);

            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = node.battleForeground;
            renderer.color = node.battleForegroundTint;
            renderer.sortingOrder = 10;

            go.transform.localPosition = new Vector3(
                node.battleForegroundOffset.x,
                node.battleForegroundOffset.y,
                7f);

            float scale = node.battleForegroundScale;
            go.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private static void BuildFog(Transform parent, MapPreviewNode node, Camera camera)
        {
            var go = new GameObject("BattleFog", typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);

            var renderer = go.GetComponent<SpriteRenderer>();
            // Reuse ink_dot sprite from VFX resources as fog overlay
            renderer.sprite = Resources.Load<Sprite>("Sprites/VFX/ink_dot");
            if (renderer.sprite == null)
            {
                Object.Destroy(go);
                return;
            }
            renderer.color = new Color(0.06f, 0.05f, 0.04f,
                Mathf.Clamp01(node.battleFogIntensity));
            renderer.sortingOrder = 15;

            // Scale to fill viewport
            float halfHeight = camera != null && camera.orthographic
                ? camera.orthographicSize : 5.4f;
            float aspect = camera != null ? camera.aspect : 16f / 9f;
            float viewHeight = halfHeight * 2f;
            float viewWidth = viewHeight * aspect;
            Vector2 spriteSize = renderer.sprite.bounds.size;
            float sx = viewWidth / Mathf.Max(0.01f, spriteSize.x);
            float sy = viewHeight / Mathf.Max(0.01f, spriteSize.y);
            float s = Mathf.Max(sx, sy);
            go.transform.localScale = new Vector3(s, s, 1f);
            go.transform.localPosition = new Vector3(0f, 0f, 6f);
        }
    }
}