using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 敌人鼠标命中与 Tooltip 生命周期。只读取战斗快照，
    /// 不参与阵型、伤害或意图计算。
    /// </summary>
    internal sealed class EnemyHoverPresenter : MonoBehaviour
    {
        private IReadOnlyList<EnemyActorView> _actors;
        private CombatController _combat;
        private Canvas _tooltipCanvas;
        private CardTooltipView _tooltip;
        private int _hoveredEnemyIndex = -1;
        private string _displayedTooltipText;
        private int _displayedCurrentHealth = int.MinValue;
        private int _displayedBaseHealth = int.MinValue;
        private int _displayedArmor = int.MinValue;
        private bool _dirty = true;

        public void Bind(
            CombatController combat,
            IReadOnlyList<EnemyActorView> actors)
        {
            Unbind();
            _combat = combat;
            _actors = actors;
            _dirty = true;
        }

        public void Unbind()
        {
            Hide();
            _combat = null;
            _actors = null;
        }

        public void MarkDirty()
        {
            _dirty = true;
        }

        /// <summary>
        /// 由场景组装层注入稳定的战斗 HUD Canvas。Tooltip 不得遍历全局
        /// Canvas 猜测宿主，否则会被挂到透明但仍启用的常驻转场层。
        /// </summary>
        public void SetTooltipCanvas(Canvas canvas)
        {
            if (_tooltipCanvas == canvas) return;
            ReleaseTooltip();
            _tooltipCanvas = canvas;
            _dirty = true;
        }

        public string GetTooltipText(int enemyIndex)
        {
            if (_combat == null || enemyIndex < 0 ||
                enemyIndex >= _combat.Enemies.Count)
            {
                return string.Empty;
            }

            EnemyStats enemy = _combat.Enemies[enemyIndex];
            if (enemy == null) return string.Empty;
            string behavior = "idle";
            List<EnemyIntent> intents =
                _combat.State?.EnemyAi?.GenerateIntents();
            if (intents != null && enemyIndex < intents.Count)
            {
                EnemyIntent intent = intents[enemyIndex];
                if (!string.IsNullOrWhiteSpace(intent.Description))
                {
                    behavior = intent.Description;
                }
                else
                {
                    behavior = intent.Type switch
                    {
                        EnemyIntentType.Attack => $"attack {intent.Amount}",
                        EnemyIntentType.Defend => $"defend +{intent.Amount}",
                        EnemyIntentType.Buff => "buff",
                        EnemyIntentType.Debuff => "debuff",
                        EnemyIntentType.Summon => "summon",
                        EnemyIntentType.Special => "special",
                        EnemyIntentType.Wait => "wait",
                        _ => string.Empty
                    };
                }
            }

            string health =
                $"{Mathf.Max(0, enemy.currentHealth)}/{enemy.baseHealth}";
            if (enemy.armor > 0)
                health += $"（{enemy.armor}）";

            var lines = new List<string>
            {
                $"<b>{enemy.name}</b>",
                health
            };
            if (!string.IsNullOrWhiteSpace(behavior))
                lines.Add(behavior);
            if (!string.IsNullOrWhiteSpace(enemy.shortDescription))
                lines.Add($"<i>{enemy.shortDescription.Trim()}</i>");
            return string.Join("\n", lines);
        }

        public void Hide()
        {
            _tooltip?.Hide();
            _hoveredEnemyIndex = -1;
            _displayedTooltipText = null;
            _displayedCurrentHealth = int.MinValue;
            _displayedBaseHealth = int.MinValue;
            _displayedArmor = int.MinValue;
            _dirty = true;
        }

        private void Update()
        {
            if (_combat == null || _actors == null) return;
            if (TopmostHoverGate.IsPointerOverBlockingUi(Input.mousePosition))
            {
                Hide();
                return;
            }
            Camera camera = Camera.main;
            if (camera == null)
            {
                SetHoveredEnemy(-1);
                return;
            }

            Vector3 mouseWorld = camera.ScreenToWorldPoint(Input.mousePosition);
            int bestIndex = -1;
            int bestSortingOrder = int.MinValue;
            for (int i = 0; i < _actors.Count; i++)
            {
                SpriteRenderer renderer = _actors[i]?.Renderer;
                if (renderer == null || renderer.sprite == null ||
                    !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                Vector3 sample = new Vector3(
                    mouseWorld.x,
                    mouseWorld.y,
                    bounds.center.z);
                if (!bounds.Contains(sample) ||
                    renderer.sortingOrder < bestSortingOrder)
                {
                    continue;
                }

                bestIndex = i;
                bestSortingOrder = renderer.sortingOrder;
            }

            SetHoveredEnemy(bestIndex);
        }

        private void SetHoveredEnemy(int enemyIndex)
        {
            if (_combat == null || enemyIndex < 0 ||
                enemyIndex >= _combat.Enemies.Count)
            {
                Hide();
                return;
            }

            EnemyStats enemy = _combat.Enemies[enemyIndex];
            if (_hoveredEnemyIndex == enemyIndex &&
                !_dirty && enemy != null &&
                _displayedCurrentHealth == enemy.currentHealth &&
                _displayedBaseHealth == enemy.baseHealth &&
                _displayedArmor == enemy.armor)
            {
                return;
            }

            string text = GetTooltipText(enemyIndex);
            if (string.IsNullOrEmpty(text))
            {
                Hide();
                return;
            }

            EnsureTooltip();
            if (_tooltip == null) return;
            if (_hoveredEnemyIndex != enemyIndex ||
                !string.Equals(
                    _displayedTooltipText,
                    text,
                    System.StringComparison.Ordinal))
            {
                _tooltip.ShowText(text);
            }
            _hoveredEnemyIndex = enemyIndex;
            _displayedTooltipText = text;
            _displayedCurrentHealth = enemy.currentHealth;
            _displayedBaseHealth = enemy.baseHealth;
            _displayedArmor = enemy.armor;
            _dirty = false;
        }

        private void EnsureTooltip()
        {
            if (_tooltip != null) return;
            if (_tooltipCanvas == null ||
                !_tooltipCanvas.enabled ||
                !_tooltipCanvas.gameObject.activeInHierarchy)
                return;

            var tooltipObject = new GameObject(
                "EnemyTooltip",
                typeof(RectTransform));
            tooltipObject.transform.SetParent(_tooltipCanvas.transform, false);
            tooltipObject.transform.SetAsLastSibling();
            _tooltip = tooltipObject.AddComponent<CardTooltipView>();
        }

        private void ReleaseTooltip()
        {
            if (_tooltip == null) return;
            GameObject tooltipObject = _tooltip.gameObject;
            _tooltip = null;
            if (Application.isPlaying)
                Destroy(tooltipObject);
            else
                DestroyImmediate(tooltipObject);
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            Unbind();
            ReleaseTooltip();
            _tooltipCanvas = null;
        }
    }
}
