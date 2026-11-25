using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 一个敌势条目。StatusId 是稳定关联键，显示只由图标与数值决定。
    /// </summary>
    public struct EnemyStatusEntry
    {
        public EnemyStatusEntry(string statusId, Sprite icon, int value)
        {
            if (string.IsNullOrWhiteSpace(statusId))
                throw new ArgumentException(
                    "敌势条目必须提供稳定 statusId。",
                    nameof(statusId));
            if (icon == null)
                throw new ArgumentNullException(nameof(icon));

            StatusId = statusId;
            Icon = icon;
            Value = value;
        }

        public string StatusId { get; }
        public Sprite Icon { get; }
        public int Value { get; }
    }

    /// <summary>
    /// 世界空间敌势组件。条目数量决定整体宽度。
    /// </summary>
    [ExecuteAlways]
    public sealed class EnemyStatusView : MonoBehaviour
    {
        public const string AttackStatusId = "enemy.attack";
        public const string DefenseStatusId = "enemy.defense";

        private const string ResourceRoot =
            "Sprites/UI/Battle/EnemyStatus/";
        private const string PrefabResourcePath =
            "UI/Battle/EnemyStatus";

        [Header("条目布局（世界单位）")]
        [SerializeField, Min(0.2f)]
        private float minimumItemWidth = 1.2f;
        [SerializeField, Min(0f)]
        private float itemGap = 0.26f;
        [SerializeField, Min(0.05f)]
        private float iconHeight = 0.42f;
        [SerializeField, Min(0f)]
        private float iconTextGap = 0.12f;
        [SerializeField, Min(0.05f)]
        private float digitAdvance = 0.21f;
        [SerializeField]
        private float contentY = 0.06f;

        [Header("数字")]
        [SerializeField, Min(16)]
        private int valueFontSize = 128;
        [SerializeField, Min(0.005f)]
        private float valueCharacterSize = 0.038f;
        [SerializeField]
        private Color inkColor =
            new Color(0.16f, 0.12f, 0.08f, 1f);
        [SerializeField]
        private Color paperOutlineColor =
            new Color(0.93f, 0.84f, 0.67f, 0.96f);

        [Header("渲染层级")]
        [SerializeField]
        private int contentSortingOrder = 36;

        private static readonly Vector2[] OutlineOffsets =
        {
            new Vector2(-0.014f, 0f),
            new Vector2(0.014f, 0f),
            new Vector2(0f, -0.014f),
            new Vector2(0f, 0.014f)
        };

        private sealed class EntryVisual
        {
            public GameObject Root;
            public SpriteRenderer Icon;
            public TextMesh Value;
            public TextMesh[] ValueOutline;
        }

        private readonly List<EntryVisual> _entryVisuals =
            new List<EntryVisual>();
        private readonly List<float> _entryWidths =
            new List<float>();

        private Font _font;
        private bool _built;

        public int VisibleEntryCount { get; private set; }
        public float ContentWidth { get; private set; }

        public static EnemyStatusView Create(Transform parent)
        {
            EnemyStatusView prefab =
                Resources.Load<EnemyStatusView>(PrefabResourcePath);
            EnemyStatusView view;
            if (prefab != null)
            {
                view = Instantiate(prefab, parent, false);
                view.name = "EnemyStatus";
            }
            else
            {
                var root = new GameObject(
                    "EnemyStatus",
                    typeof(EnemyStatusView));
                root.transform.SetParent(parent, false);
                view = root.GetComponent<EnemyStatusView>();
            }
            view.Build();
            return view;
        }

        public static Sprite LoadDefaultIcon(string statusId)
        {
            string relativePath;
            switch (statusId)
            {
                case AttackStatusId:
                    relativePath = "attack";
                    break;
                case DefenseStatusId:
                    relativePath = "defense";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(statusId),
                        statusId,
                        "没有为该敌势状态配置默认图标。");
            }

            return RequireSprite(ResourceRoot + relativePath);
        }

        public void SetEntries(IReadOnlyList<EnemyStatusEntry> entries)
        {
            if (!_built)
                Build();

            int count = entries != null ? entries.Count : 0;
            EnsureEntryCapacity(count);
            _entryWidths.Clear();

            for (int i = 0; i < _entryVisuals.Count; i++)
            {
                EntryVisual visual = _entryVisuals[i];
                bool visible = i < count;
                visual.Root.SetActive(visible);
                if (!visible) continue;

                EnemyStatusEntry entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.StatusId))
                {
                    throw new ArgumentException(
                        $"第 {i} 个敌势条目缺少稳定 statusId。",
                        nameof(entries));
                }
                if (entry.Icon == null)
                {
                    throw new ArgumentException(
                        $"敌势条目 {entry.StatusId} 缺少图标。",
                        nameof(entries));
                }

                visual.Root.name = $"StatusItem_{entry.StatusId}";
                visual.Icon.sprite = entry.Icon;
                ScaleToHeight(
                    visual.Icon.transform,
                    entry.Icon,
                    iconHeight);
                visual.Value.text = entry.Value.ToString();
                foreach (TextMesh outline in visual.ValueOutline)
                    outline.text = visual.Value.text;

                float entryWidth = MeasureEntryWidth(entry.Value);
                _entryWidths.Add(entryWidth);
                LayoutEntryContent(visual, entryWidth);
            }

            VisibleEntryCount = count;
            gameObject.SetActive(count > 0);
            if (count == 0)
            {
                ContentWidth = 0f;
                return;
            }

            ContentWidth = LayoutEntries(count);
        }

        private void Build(bool force = false)
        {
            if (_built && !force)
                return;

            ClearGeneratedChildren();
            _entryVisuals.Clear();
            _entryWidths.Clear();

            _font = Resources.Load<Font>("Fonts/NotoSerifSC-Heavy");
            if (_font == null)
            {
                _font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            }

            if (!Application.isPlaying)
                MarkChildrenEditorOnly();
            _built = true;
            gameObject.SetActive(false);
        }

        [ContextMenu("刷新两项预览")]
        private void RebuildPreview()
        {
            Build(true);
            SetEntries(new[]
            {
                new EnemyStatusEntry(
                    AttackStatusId,
                    LoadDefaultIcon(AttackStatusId),
                    5),
                new EnemyStatusEntry(
                    DefenseStatusId,
                    LoadDefaultIcon(DefenseStatusId),
                    10)
            });
        }

        [ContextMenu("清除预览")]
        private void ClearPreview()
        {
            ClearGeneratedChildren();
            _entryVisuals.Clear();
            _entryWidths.Clear();
            _built = false;
            VisibleEntryCount = 0;
            ContentWidth = 0f;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying && !_built)
                RebuildPreview();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying && _built)
            {
                UnityEditor.EditorApplication.delayCall -= DelayedRebuild;
                UnityEditor.EditorApplication.delayCall += DelayedRebuild;
            }
        }

        private void DelayedRebuild()
        {
            if (this == null || !_built || Application.isPlaying) return;
            RebuildPreview();
        }
#endif

        private void ClearGeneratedChildren()
        {
            while (transform.childCount > 0)
            {
                Transform child = transform.GetChild(0);
                child.SetParent(null, false);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private void MarkChildrenEditorOnly()
        {
            for (int i = 0; i < transform.childCount; i++)
                MarkEditorOnlyRecursive(transform.GetChild(i));
        }

        private static void MarkEditorOnlyRecursive(Transform t)
        {
            t.gameObject.hideFlags = HideFlags.DontSaveInEditor;
            for (int i = 0; i < t.childCount; i++)
                MarkEditorOnlyRecursive(t.GetChild(i));
        }

        private void EnsureEntryCapacity(int count)
        {
            while (_entryVisuals.Count < count)
                _entryVisuals.Add(CreateEntryVisual(_entryVisuals.Count));
        }

        private EntryVisual CreateEntryVisual(int index)
        {
            var root = new GameObject($"StatusItem_{index}");
            root.transform.SetParent(transform, false);

            var iconObject = new GameObject(
                "Icon",
                typeof(SpriteRenderer));
            iconObject.transform.SetParent(root.transform, false);
            SpriteRenderer icon =
                iconObject.GetComponent<SpriteRenderer>();
            icon.sortingOrder = contentSortingOrder;

            var outlines = new TextMesh[OutlineOffsets.Length];
            for (int i = 0; i < outlines.Length; i++)
            {
                outlines[i] = CreateValueText(
                    root.transform,
                    $"ValueOutline_{i}",
                    paperOutlineColor,
                    contentSortingOrder - 1);
            }

            TextMesh value = CreateValueText(
                root.transform,
                "Value",
                inkColor,
                contentSortingOrder);
            if (_font != null && _font.dynamic)
            {
                _font.RequestCharactersInTexture(
                    "0123456789-",
                    valueFontSize,
                    FontStyle.Bold);
            }
            root.SetActive(false);

            return new EntryVisual
            {
                Root = root,
                Icon = icon,
                Value = value,
                ValueOutline = outlines
            };
        }

        private TextMesh CreateValueText(
            Transform parent,
            string objectName,
            Color color,
            int sortingOrder)
        {
            var valueObject = new GameObject(
                objectName,
                typeof(TextMesh));
            valueObject.transform.SetParent(parent, false);
            TextMesh value = valueObject.GetComponent<TextMesh>();
            value.font = _font;
            value.fontSize = valueFontSize;
            value.characterSize = valueCharacterSize;
            value.fontStyle = FontStyle.Bold;
            value.anchor = TextAnchor.MiddleLeft;
            value.alignment = TextAlignment.Left;
            value.richText = false;
            value.color = color;

            MeshRenderer valueRenderer =
                valueObject.GetComponent<MeshRenderer>();
            valueRenderer.sortingOrder = sortingOrder;
            if (_font != null && _font.material != null)
                valueRenderer.sharedMaterial = _font.material;
            return value;
        }

        private void LayoutEntryContent(
            EntryVisual visual,
            float entryWidth)
        {
            float iconX = -entryWidth * 0.5f + iconHeight * 0.5f;
            float valueX = iconX + iconHeight * 0.5f + iconTextGap;
            visual.Icon.transform.localPosition =
                new Vector3(iconX, contentY, 0f);
            visual.Value.transform.localPosition =
                new Vector3(valueX, contentY, 0f);
            for (int i = 0; i < visual.ValueOutline.Length; i++)
            {
                Vector2 offset = OutlineOffsets[i];
                visual.ValueOutline[i].transform.localPosition =
                    new Vector3(
                        valueX + offset.x,
                        contentY + offset.y,
                        0f);
            }
        }

        private float LayoutEntries(int count)
        {
            float contentWidth = itemGap * Mathf.Max(0, count - 1);
            for (int i = 0; i < count; i++)
                contentWidth += _entryWidths[i];

            float cursor = -contentWidth * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float width = _entryWidths[i];
                _entryVisuals[i].Root.transform.localPosition =
                    new Vector3(cursor + width * 0.5f, 0f, 0f);
                cursor += width + itemGap;
            }
            return contentWidth;
        }

        private float MeasureEntryWidth(int value)
        {
            long magnitude = Math.Abs((long)value);
            int digitCount = magnitude == 0
                ? 1
                : Mathf.FloorToInt(Mathf.Log10(magnitude)) + 1;
            if (value < 0)
                digitCount++;

            float measured =
                iconHeight +
                iconTextGap +
                digitCount * digitAdvance +
                0.1f;
            return Mathf.Max(minimumItemWidth, measured);
        }

        private static float ScaleToHeight(
            Transform target,
            Sprite sprite,
            float height)
        {
            if (target == null || sprite == null ||
                sprite.bounds.size.y <= 0f)
            {
                return 0f;
            }

            float scale = height / sprite.bounds.size.y;
            target.localScale = new Vector3(scale, scale, 1f);
            return sprite.bounds.size.x * scale;
        }

        private static Sprite RequireSprite(string resourcePath)
        {
            Sprite sprite = RuntimeArtLoader.LoadSprite(resourcePath);
            if (sprite == null)
            {
                throw new InvalidOperationException(
                    $"敌势区缺少运行素材：{resourcePath}");
            }
            return sprite;
        }
    }
}
