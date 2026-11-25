using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// 群英殿名册的可滚动、可筛选列表。
    ///
    /// 名册只拥有筛选、搜索、滚动和可见行池；当前英灵详情、登场演出和
    /// 出战提交由父面板负责。行池按视口高度建立，英灵数量增加时不会为
    /// 每个条目创建一个永久 GameObject。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class YinglingRosterView : MonoBehaviour
    {
        [Header("列表结构")]
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private RectTransform _content;
        [SerializeField] private Button _rowTemplate;

        [Header("筛选控件")]
        [SerializeField] private InputField _search;
        [SerializeField] private Dropdown _volume;

        [Header("状态文本")]
        [SerializeField] private Text _empty;

        [SerializeField] private float _rowHeight = 82f;

        private readonly List<YinglingHallEntry> _entries =
            new List<YinglingHallEntry>();
        private readonly List<YinglingHallEntry> _visibleEntries =
            new List<YinglingHallEntry>();
        private readonly List<RowView> _rowPool = new List<RowView>();
        private readonly List<string> _volumeIds = new List<string>();
        private readonly HashSet<string> _deployedIds =
            new HashSet<string>(StringComparer.Ordinal);

        private string _selectedId;
        private string _selectedVolumeId = string.Empty;
        private string _query = string.Empty;
        private bool _initialized;
        private bool _listenersBound;

        private sealed class RowView
        {
            public GameObject Object;
            public RectTransform Rect;
            public Button Button;
            public Text Name;
            public Text Caption;
            public Text State;
            public Image Marker;
            public YinglingHallEntry Entry;
            public int VisibleIndex = -1;
        }

        /// <summary>点击一行后立即通知父面板；播放演出不由名册锁输入。</summary>
        public event Action<YinglingHallEntry> Selected;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            UnbindListeners();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!_initialized)
                return;
            EnsureRowPool();
            RefreshVisibleRows();
        }

        /// <summary>
        /// 替换名册快照并显示全部可见条目。selectedId 只影响行标记，
        /// deployed 只影响行状态；筛选重建会回到列表顶部。
        /// </summary>
        public void SetEntries(
            IReadOnlyList<YinglingHallEntry> entries,
            string selectedId,
            IReadOnlyList<string> deployed)
        {
            EnsureInitialized();
            _entries.Clear();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    YinglingHallEntry entry = entries[i];
                    if (entry != null)
                        _entries.Add(entry);
                }
            }

            _selectedId = selectedId ?? string.Empty;
            ReplaceDeployed(deployed);
            _selectedVolumeId = string.Empty;
            SetSearchTextWithoutNotify(string.Empty);
            RebuildVolumeOptions();
            RebuildFilteredRows(true);
            RevealSelectedRow();
        }

        /// <summary>
        /// 只更新当前选择和出战标记，不重建筛选、不跳回列表顶部。
        /// </summary>
        public void SetSelection(
            string selectedId,
            IReadOnlyList<string> deployed)
        {
            EnsureInitialized();
            _selectedId = selectedId ?? string.Empty;
            ReplaceDeployed(deployed);
            RefreshVisibleRows();
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            if (_rowTemplate != null)
                _rowTemplate.gameObject.SetActive(false);
            BindListeners();
        }

        private void BindListeners()
        {
            if (_listenersBound)
                return;
            _listenersBound = true;

            if (_scroll != null)
                _scroll.onValueChanged.AddListener(OnScrollChanged);
            if (_search != null)
                _search.onValueChanged.AddListener(OnSearchChanged);
            if (_volume != null)
                _volume.onValueChanged.AddListener(OnVolumeChanged);
        }

        private void UnbindListeners()
        {
            if (!_listenersBound)
                return;
            _listenersBound = false;

            if (_scroll != null)
                _scroll.onValueChanged.RemoveListener(OnScrollChanged);
            if (_search != null)
                _search.onValueChanged.RemoveListener(OnSearchChanged);
            if (_volume != null)
                _volume.onValueChanged.RemoveListener(OnVolumeChanged);
        }

        private void OnSearchChanged(string value)
        {
            _query = value ?? string.Empty;
            RebuildFilteredRows(true);
        }

        private void OnVolumeChanged(int value)
        {
            if (value < 0 || value >= _volumeIds.Count)
                _selectedVolumeId = string.Empty;
            else
                _selectedVolumeId = _volumeIds[value];
            RebuildFilteredRows(true);
        }

        private void OnScrollChanged(Vector2 _)
        {
            RefreshVisibleRows();
        }

        private void RebuildVolumeOptions()
        {
            if (_volume == null)
                return;

            string previous = _selectedVolumeId;
            _volumeIds.Clear();
            _volumeIds.Add(string.Empty);
            var labels = new List<string> { "全部卷" };
            var knownIds = new HashSet<string>(StringComparer.Ordinal);
            knownIds.Add(string.Empty);

            for (int i = 0; i < _entries.Count; i++)
            {
                YinglingHallEntry entry = _entries[i];
                string id = entry.VolumeId ?? string.Empty;
                if (!knownIds.Add(id))
                    continue;
                _volumeIds.Add(id);
                labels.Add(string.IsNullOrWhiteSpace(entry.VolumeName)
                    ? id
                    : entry.VolumeName);
            }

            _volume.ClearOptions();
            var options = new List<Dropdown.OptionData>(labels.Count);
            for (int i = 0; i < labels.Count; i++)
                options.Add(new Dropdown.OptionData(labels[i]));
            _volume.AddOptions(options);

            int selected = _volumeIds.IndexOf(previous);
            if (selected < 0)
                selected = 0;
            _selectedVolumeId = _volumeIds[selected];
            _volume.SetValueWithoutNotify(selected);
            _volume.RefreshShownValue();
        }

        private void RebuildFilteredRows(bool resetScroll)
        {
            _visibleEntries.Clear();
            string query = _query.Trim();
            for (int i = 0; i < _entries.Count; i++)
            {
                YinglingHallEntry entry = _entries[i];
                if (entry == null)
                    continue;
                if (!string.IsNullOrEmpty(_selectedVolumeId) &&
                    !string.Equals(
                        entry.VolumeId,
                        _selectedVolumeId,
                        StringComparison.Ordinal))
                    continue;
                if (!MatchesQuery(entry, query))
                    continue;
                _visibleEntries.Add(entry);
            }

            if (_content != null)
            {
                float height = Mathf.Max(0f, _visibleEntries.Count *
                    Mathf.Max(1f, _rowHeight));
                _content.sizeDelta = new Vector2(
                    _content.sizeDelta.x,
                    height);
                if (resetScroll && _scroll != null)
                    _scroll.verticalNormalizedPosition = 1f;
            }

            EnsureRowPool();
            if (_empty != null)
            {
                _empty.text = !string.IsNullOrWhiteSpace(_query)
                    ? "没有找到匹配的英灵\n试试其他名字、卷名或玩法。"
                    : "此卷暂无可展示的英灵";
                _empty.gameObject.SetActive(_visibleEntries.Count == 0);
            }
            RefreshVisibleRows();
        }

        private bool MatchesQuery(YinglingHallEntry entry, string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;

            if (!entry.Unlocked)
                return Contains(entry.VolumeName, query) ||
                       Contains(entry.UnlockHint, query);

            return Contains(entry.DisplayName, query) ||
                   Contains(entry.Title, query) ||
                   Contains(entry.VolumeName, query) ||
                   Contains(entry.Playstyle, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RevealSelectedRow()
        {
            int index = _visibleEntries.FindIndex(entry => entry.ContentId == _selectedId);
            RectTransform viewport = ResolveViewport();
            if (index < 0 || viewport == null || _scroll == null) return;
            float excess = Mathf.Max(0f, _content.sizeDelta.y - viewport.rect.height);
            _scroll.verticalNormalizedPosition = excess > 0f
                ? 1f - Mathf.Clamp01(index * _rowHeight / excess) : 1f;
            RefreshVisibleRows();
        }

        private RectTransform ResolveViewport()
        {
            if (_scroll == null)
                return null;
            return _scroll.viewport != null
                ? _scroll.viewport
                : _scroll.GetComponent<RectTransform>();
        }

        private void EnsureRowPool()
        {
            if (_rowTemplate == null || _content == null)
                return;

            RectTransform viewport = ResolveViewport();
            float viewportHeight = viewport == null
                ? 0f
                : Mathf.Max(viewport.rect.height, viewport.sizeDelta.y);
            int needed = Mathf.CeilToInt(
                viewportHeight / Mathf.Max(1f, _rowHeight)) + 2;
            needed = Mathf.Max(2, needed);

            while (_rowPool.Count < needed)
                _rowPool.Add(CreateRow(_rowPool.Count));
            while (_rowPool.Count > needed)
            {
                int last = _rowPool.Count - 1;
                if (_rowPool[last].Object != null)
                    Destroy(_rowPool[last].Object);
                _rowPool.RemoveAt(last);
            }
        }

        private RowView CreateRow(int poolIndex)
        {
            Button button = Instantiate(_rowTemplate, _content, false);
            button.name = $"YinglingRow_{poolIndex:00}";
            button.gameObject.SetActive(false);
            button.onClick.RemoveAllListeners();

            RowView row = new RowView
            {
                Object = button.gameObject,
                Rect = button.GetComponent<RectTransform>(),
                Button = button,
                Name = FindText(button.transform, "Name"),
                Caption = FindText(button.transform, "Caption"),
                State = FindText(button.transform, "State"),
                Marker = FindImage(button.transform, "Marker")
            };
            button.onClick.AddListener(() => SelectRow(row));
            return row;
        }

        private static Text FindText(Transform root, string childName)
        {
            Transform child = root.Find(childName);
            return child == null ? null : child.GetComponent<Text>();
        }

        private static Image FindImage(Transform root, string childName)
        {
            Transform child = root.Find(childName);
            return child == null ? null : child.GetComponent<Image>();
        }

        private void RefreshVisibleRows()
        {
            if (_rowPool.Count == 0 || _content == null)
                return;

            float rowHeight = Mathf.Max(1f, _rowHeight);
            RectTransform viewport = ResolveViewport();
            float viewportHeight = viewport == null
                ? 0f
                : Mathf.Max(viewport.rect.height, viewport.sizeDelta.y);
            float maxScroll = Mathf.Max(
                0f,
                _content.rect.height - viewportHeight);
            float normalized = _scroll == null
                ? 1f
                : Mathf.Clamp01(_scroll.verticalNormalizedPosition);
            float scrollTop = maxScroll * (1f - normalized);
            int firstIndex = Mathf.Max(0, Mathf.FloorToInt(scrollTop / rowHeight));
            for (int i = 0; i < _rowPool.Count; i++)
            {
                int visibleIndex = firstIndex + i;
                RowView row = _rowPool[i];
                if (visibleIndex >= _visibleEntries.Count)
                {
                    row.Entry = null;
                    row.VisibleIndex = -1;
                    row.Object.SetActive(false);
                    continue;
                }

                row.Entry = _visibleEntries[visibleIndex];
                row.VisibleIndex = visibleIndex;
                row.Rect.anchorMin = new Vector2(0.5f, 1f);
                row.Rect.anchorMax = new Vector2(0.5f, 1f);
                row.Rect.pivot = new Vector2(0.5f, 1f);
                row.Rect.anchoredPosition = new Vector2(
                    0f,
                    -visibleIndex * rowHeight);
                float width = Mathf.Max(
                    _content.rect.width,
                    viewport == null ? 0f : viewport.rect.width);
                row.Rect.sizeDelta = new Vector2(
                    width,
                    rowHeight);
                BindRow(row, row.Entry);
                row.Object.SetActive(true);
            }
        }

        private void BindRow(RowView row, YinglingHallEntry entry)
        {
            bool selected = string.Equals(
                _selectedId,
                entry.ContentId,
                StringComparison.Ordinal);
            bool deployed = _deployedIds.Contains(entry.ContentId);

            if (row.Name != null)
            {
                row.Name.text = entry.Unlocked ? entry.DisplayName : "未结缘英灵";
                row.Name.color = selected ? new Color(0.62f, 0.275f, 0.208f)
                    : new Color(0.208f, 0.184f, 0.161f);
            }
            row.Button.GetComponent<Image>().color = selected
                ? new Color(0.62f, 0.275f, 0.208f, 0.075f)
                : new Color(0.40f, 0.33f, 0.24f, 0.025f);
            if (row.Caption != null)
            {
                row.Caption.text = entry.Unlocked
                    ? BuildOwnedCaption(entry)
                    : BuildLockedCaption(entry);
            }
            if (row.State != null)
                row.State.text = deployed ? "出战" : "查看";
            if (row.Marker != null)
            {
                row.Marker.gameObject.SetActive(selected);
                row.Marker.enabled = selected;
            }
        }

        private static string BuildOwnedCaption(YinglingHallEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Playstyle))
                return entry.VolumeName ?? string.Empty;
            return string.IsNullOrWhiteSpace(entry.VolumeName)
                ? entry.Playstyle
                : $"{entry.VolumeName}  ·  {entry.Playstyle}";
        }

        private static string BuildLockedCaption(YinglingHallEntry entry)
        {
            string volume = string.IsNullOrWhiteSpace(entry.VolumeName)
                ? string.Empty
                : entry.VolumeName + "  ·  ";
            return volume + "解锁：" + (entry.UnlockHint ?? "继续探索");
        }

        private void SelectRow(RowView row)
        {
            YinglingHallEntry entry = row?.Entry;
            if (entry == null)
                return;
            if (string.Equals(
                    _selectedId,
                    entry.ContentId,
                    StringComparison.Ordinal))
            {
                return;
            }

            _selectedId = entry.ContentId;
            RefreshVisibleRows();
            Selected?.Invoke(entry);
        }

        private void ReplaceDeployed(IReadOnlyList<string> deployed)
        {
            _deployedIds.Clear();
            if (deployed == null)
                return;
            for (int i = 0; i < deployed.Count; i++)
            {
                string id = deployed[i];
                if (!string.IsNullOrWhiteSpace(id))
                    _deployedIds.Add(id);
            }
        }

        private void SetSearchTextWithoutNotify(string value)
        {
            _query = value ?? string.Empty;
            if (_search != null)
                _search.SetTextWithoutNotify(_query);
        }
    }
}
