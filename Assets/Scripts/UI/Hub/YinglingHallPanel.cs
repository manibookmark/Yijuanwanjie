using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// 群英殿的选择与确认界面。Prefab 拥有静态版式；名册独占搜索/滚动，
    /// 演出视图独占图集/帧时钟，本面板只拥有当前浏览对象与待确认队伍。
    /// 本轮替换未验收原型，不再在运行时创建整张面板或复制英灵内容。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class YinglingHallPanel : MonoBehaviour
    {
        public const string ResourcePath = "UI/Hub/YinglingHallPanel";
        // 群英殿是独立的 UI 弹窗：以固定参考画板承载内容，按视口
        // 等比适配；不要把背景图的 16:9 核心裁切规则套到这里。
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        [SerializeField] private CanvasGroup _rootGroup;
        [SerializeField] private RectTransform _artboard;
        [SerializeField] private YinglingRosterView _roster;
        [SerializeField] private YinglingArrivalView _arrival;
        [SerializeField] private Text _name;
        [SerializeField] private Text _title;
        [SerializeField] private Text _volumeLabel;
        [SerializeField] private Text _playstyle;
        [SerializeField] private Text _enterEffect;
        [SerializeField] private Text _triggerEffect;
        [SerializeField] private GameObject _abilities;
        [SerializeField] private Text _unlockHint;
        [SerializeField] private Text _stamp;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Text _confirmLabel;
        [SerializeField] private Button _returnButton;
        [SerializeField] private Text _singleDeployment;
        [SerializeField] private RectTransform _teamRoot;
        [SerializeField] private Button _slotTemplate;
        [SerializeField] private Text _feedback;

        private readonly List<Button> _slotButtons = new List<Button>();
        private readonly List<string> _workingSlots = new List<string>();
        private List<YinglingHallEntry> _entries = new List<YinglingHallEntry>();
        private MetaProgression _meta;
        private GameSaveService _saveService;
        private YinglingHallEntry _selected;
        private int _activeSlot;

        public event Action CloseRequested;
        public event Action ArrivalVoiceStopRequested;
        public event Action<string> ArrivalVoiceRequested;
        public Button ReturnButton => _returnButton;
        public bool IsVisible => gameObject.activeSelf && _rootGroup.alpha > 0f;

        private void Awake()
        {
            _returnButton.onClick.AddListener(RequestClose);
            _confirmButton.onClick.AddListener(ConfirmDeployment);
            _roster.Selected += SelectHero;
            Hide();
        }

        private void OnDestroy()
        {
            if (_roster != null) _roster.Selected -= SelectHero;
        }

        private void Update()
        {
            if (IsVisible && Input.GetKeyDown(KeyCode.Escape))
                RequestClose();
        }

        public void Initialize(MetaProgression meta, GameSaveService saveService)
        {
            _meta = meta ?? throw new ArgumentNullException(nameof(meta));
            _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));
        }

        public void Show()
        {
            if (_meta == null || _saveService == null) return;
            // 打开时重新读注入的活动存档，避免覆盖玩家在其它设施中新获得的进度。
            var latest = new MetaProgression();
            if (_saveService.TryLoad(latest, out _)) _meta = latest;
            _entries = YinglingHallEntry.Load(_meta);
            RestoreDeployment();
            _activeSlot = 0;
            _selected = FindEntry(_workingSlots[0]) ?? _entries.Find(entry => entry.Unlocked)
                ?? (_entries.Count > 0 ? _entries[0] : null);
            gameObject.SetActive(true);
            FitArtboard();
            transform.SetAsLastSibling();
            _rootGroup.alpha = 1f;
            _rootGroup.interactable = true;
            _rootGroup.blocksRaycasts = true;
            _feedback.text = string.Empty;
            _roster.SetEntries(_entries, _selected?.ContentId, _workingSlots);
            RefreshSelection(false);
            RefreshDeployment();
        }

        public void Hide()
        {
            ArrivalVoiceStopRequested?.Invoke();
            _rootGroup.alpha = 0f;
            _rootGroup.interactable = false;
            _rootGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
        }

        private void RequestClose() => CloseRequested?.Invoke();

        private void OnRectTransformDimensionsChange() => FitArtboard();

        private void FitArtboard()
        {
            if (_artboard == null) return;
            RectTransform root = transform as RectTransform;
            if (root == null) return;
            Rect bounds = root.rect;
            if (bounds.width <= 0f || bounds.height <= 0f) return;
            float scale = Mathf.Min(
                bounds.width / ReferenceWidth,
                bounds.height / ReferenceHeight);
            _artboard.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        }

        private int SlotCapacity
        {
            get
            {
                int level = 0;
                if (_meta?.powerUpLevels != null)
                {
                    if (!_meta.powerUpLevels.TryGetValue("crawler-slot", out level))
                        _meta.powerUpLevels.TryGetValue("御灵诀", out level);
                }
                return Mathf.Clamp(1 + level, 1, 3);
            }
        }

        private void RestoreDeployment()
        {
            _workingSlots.Clear();
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < SlotCapacity; i++)
            {
                string id = _meta.equippedCrawlers != null && i < _meta.equippedCrawlers.Count
                    ? _meta.equippedCrawlers[i] : string.Empty;
                if (FindEntry(id)?.Unlocked != true || !used.Add(id)) id = string.Empty;
                _workingSlots.Add(id);
            }
            if (string.IsNullOrEmpty(_workingSlots[0]))
            {
                YinglingHallEntry first = FindEntry(GameSession.Current.SelectedCrawlerId);
                if (first?.Unlocked != true) first = _entries.Find(entry => entry.Unlocked);
                if (first == null) return;
                int duplicate = _workingSlots.IndexOf(first.ContentId);
                if (duplicate > 0) _workingSlots[duplicate] = string.Empty;
                _workingSlots[0] = first.ContentId;
            }
        }

        private YinglingHallEntry FindEntry(string id) =>
            string.IsNullOrEmpty(id) ? null : _entries.Find(entry => entry.ContentId == id);

        private void SelectHero(YinglingHallEntry entry)
        {
            if (entry == null || entry.ContentId == _selected?.ContentId) return;
            _selected = entry;
            _feedback.text = string.Empty;
            RefreshSelection(true);
        }

        private void RefreshSelection(bool playArrival)
        {
            bool unlocked = _selected?.Unlocked == true;
            _name.text = _selected == null ? "群英待聚" : unlocked ? _selected.DisplayName : "未结缘英灵";
            _title.text = unlocked ? _selected.Title : "等待相逢";
            _volumeLabel.text = _selected?.VolumeName ?? string.Empty;
            _playstyle.text = unlocked ? _selected.Playstyle : string.Empty;
            _abilities.SetActive(unlocked);
            _enterEffect.text = unlocked ? _selected.EnterSummary : string.Empty;
            _triggerEffect.text = unlocked ? _selected.TriggerSummary : string.Empty;
            _unlockHint.gameObject.SetActive(!unlocked);
            _unlockHint.text = _selected?.UnlockHint ?? "继续探索万界，与更多英灵结缘。";
            // 演出切换与语音切换保持同一顺序：先截断上一位，再播放当前登场语音。
            ArrivalVoiceStopRequested?.Invoke();
            _arrival.Present(unlocked ? _selected.ArrivalProfile : null, playArrival);
            if (playArrival && unlocked &&
                !string.IsNullOrWhiteSpace(_selected.OpeningVoiceCueId))
                ArrivalVoiceRequested?.Invoke(_selected.OpeningVoiceCueId);
            _roster.SetSelection(_selected?.ContentId, _workingSlots);
            RefreshAction();
        }

        private void RefreshAction()
        {
            bool unlocked = _selected?.Unlocked == true;
            bool assigned = unlocked && _workingSlots.Contains(_selected.ContentId);
            bool inActiveSlot = unlocked && _workingSlots[_activeSlot] == _selected.ContentId;
            _stamp.gameObject.SetActive(assigned);
            _stamp.text = "出战";
            _confirmButton.interactable = unlocked && !inActiveSlot;
            _confirmLabel.text = !unlocked ? "尚未结缘" : inActiveSlot ? "已出战" : "确认出战";
        }

        private void RefreshDeployment()
        {
            bool multiple = SlotCapacity > 1;
            _teamRoot.gameObject.SetActive(multiple);
            _singleDeployment.gameObject.SetActive(!multiple);
            _singleDeployment.text = "当前出战  ·  " + (FindEntry(_workingSlots[0])?.DisplayName ?? "尚未选择");
            if (!multiple) return;

            while (_slotButtons.Count < SlotCapacity)
            {
                int index = _slotButtons.Count;
                Button button = Instantiate(_slotTemplate, _teamRoot, false);
                button.name = "DeploymentSlot" + (index + 1);
                button.onClick.AddListener(() => SelectSlot(index));
                _slotButtons.Add(button);
            }
            for (int i = 0; i < _slotButtons.Count; i++)
            {
                Button button = _slotButtons[i];
                button.gameObject.SetActive(i < SlotCapacity);
                if (i >= SlotCapacity) continue;
                ((RectTransform)button.transform).anchoredPosition = new Vector2(i * 208f, 0f);
                Text label = button.transform.Find("Label").GetComponent<Text>();
                label.text = (i + 1) + "  " + (FindEntry(_workingSlots[i])?.DisplayName ?? "选择英灵");
                button.transform.Find("Selected").gameObject.SetActive(i == _activeSlot);
            }
        }

        private void SelectSlot(int index)
        {
            _activeSlot = index;
            _feedback.text = string.Empty;
            YinglingHallEntry assigned = FindEntry(_workingSlots[index]);
            if (assigned != null && assigned != _selected)
            {
                _selected = assigned;
                RefreshSelection(false);
            }
            else RefreshAction();
            RefreshDeployment();
        }

        private void ConfirmDeployment()
        {
            if (_selected?.Unlocked != true) return;
            var candidate = new List<string>(_workingSlots);
            int existing = candidate.IndexOf(_selected.ContentId);
            if (existing == _activeSlot) return;
            if (existing >= 0) candidate[existing] = candidate[_activeSlot];
            candidate[_activeSlot] = _selected.ContentId;
            // 保留主位：把主位英灵移至一个空的后排位置时，其主位仍由它出战。
            if (string.IsNullOrEmpty(candidate[0]))
            {
                candidate[0] = candidate[_activeSlot];
                candidate[_activeSlot] = string.Empty;
                _activeSlot = 0;
            }
            List<string> previous = _meta.equippedCrawlers;
            string previousPrimary = GameSession.Current.SelectedCrawlerId;
            try
            {
                _meta.equippedCrawlers = candidate;
                GameSession.Current.SelectedCrawlerId = candidate[0];
                _saveService.Save(_meta);
            }
            catch (Exception error)
            {
                _meta.equippedCrawlers = previous;
                GameSession.Current.SelectedCrawlerId = previousPrimary;
                _feedback.text = "保存未成功，请重试。";
                Debug.LogException(error, this);
                return;
            }
            _workingSlots.Clear();
            _workingSlots.AddRange(candidate);
            ArrivalVoiceStopRequested?.Invoke();
            _arrival.HoldLastFrame();
            _feedback.text = _selected.DisplayName + "已出战";
            _roster.SetSelection(_selected.ContentId, _workingSlots);
            RefreshDeployment();
            RefreshAction();
        }
    }
}
