using System;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.Dungeon;

namespace WanjieScroll.UI.Hub
{
    /// <summary>拥有天机浏览草稿；静态版式由 Prefab 提供，只有确认才写入活动存档。</summary>
    public sealed class TianjiArcanaPanel : MonoBehaviour
    {
        public const string ResourcePath = "UI/Hub/TianjiArcanaPanel";
        [Serializable] public sealed class Choice
        {
            public ArcanaConfig config;
            public Button button;
            public Text state;
            public Image marker;
        }
        [SerializeField] private RectTransform _artboard;
        [SerializeField] private Choice[] _choices;
        [SerializeField] private Button _returnButton, _confirmButton, _clearButton;
        [SerializeField] private Text _title, _description, _condition, _current, _feedback, _count, _seal;
        private MetaProgression _meta;
        private GameSaveService _save;
        private ArcanaConfig _draft;
        private bool _blocked;
        public event Action CloseRequested;
        public Button ReturnButton => _returnButton;

        private void Awake()
        {
            _returnButton.onClick.AddListener(RequestClose);
            _confirmButton.onClick.AddListener(Confirm);
            _clearButton.onClick.AddListener(() => Select(null));
            foreach (Choice choice in _choices)
            {
                Choice captured = choice;
                choice.button.onClick.AddListener(() => Select(captured.config));
            }
            Hide();
        }

        public void Show()
        {
            // Resolve the active slot at every opening, never retain a service across slot changes.
            _save = GameSaveService.CreateDefault();
            _meta = new MetaProgression();
            bool loaded = _save.TryLoad(_meta, out SaveGameData data, out string error);
            _blocked = !loaded || HasUnfinishedRun(data);
            _draft = Array.Find(_choices, c => c.config.contentId == _meta.equippedArcana)?.config;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Fit();
            Refresh();
            _feedback.text = !loaded ? "存档读取失败，请返回后重试。" : _blocked
                ? "行程尚未结束，结算后可更换天机。" : string.Empty;
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning(error, this);
        }

        private static bool HasUnfinishedRun(SaveGameData data) =>
            DungeonRunSessionStore.Current != null ||
            !string.IsNullOrWhiteSpace(data?.activeDungeonRunPayload);

        public void Hide() => gameObject.SetActive(false);
        private void RequestClose() => CloseRequested?.Invoke();
        private void Update() { if (Input.GetKeyDown(KeyCode.Escape)) RequestClose(); }
        private void OnRectTransformDimensionsChange() => Fit();
        private void Fit()
        {
            if (_artboard == null) return;
            Rect bounds = ((RectTransform)transform).rect;
            _artboard.localScale = Vector3.one * Mathf.Max(.01f, Mathf.Min(bounds.width / 1920f, bounds.height / 1080f));
        }

        private void Select(ArcanaConfig config)
        {
            _draft = config;
            Refresh();
            if (!_blocked) _feedback.text = string.Empty;
        }

        private void Refresh()
        {
            int count = 0;
            foreach (Choice choice in _choices)
            {
                bool unlocked = _meta.IsArcanaUnlocked(choice.config.contentId);
                if (unlocked) count++;
                choice.marker.enabled = choice.config == _draft;
                choice.state.text = !unlocked ? "未解锁" : choice.config.contentId == _meta.equippedArcana ? "已择定" : "已解锁";
            }
            _count.text = $"天机录  {count} / {_choices.Length}";
            _title.text = _draft != null ? _draft.arcanaName : "不携带天机";
            _description.text = _draft != null ? _draft.description : "以原本的力量踏入长卷。\n确认后，下一次入卷不携带天机。";
            bool available = _draft == null || _meta.IsArcanaUnlocked(_draft.contentId);
            _condition.text = _draft == null ? "可随时在行程开始前重新选择。" :
                (available ? "已解锁 · " : "尚未解锁 · ") + _draft.unlockCondition;
            _seal.text = _draft == null ? "无" : _draft.effect == ArcanaEffect.BaishuZhoutian ? "周" :
                _draft.effect == ArcanaEffect.ExperimentalMedicine ? "生" : "御";
            Choice equipped = Array.Find(_choices, c => c.config.contentId == _meta.equippedArcana);
            _current.text = "当前所择  ·  " + (equipped != null ? equipped.config.arcanaName :
                string.IsNullOrEmpty(_meta.equippedArcana) ? "无" : "未收录天机");
            _confirmButton.interactable = !_blocked && _meta.IsArcanaFacilityUnlocked && available &&
                !string.Equals(_draft?.contentId ?? string.Empty, _meta.equippedArcana ?? string.Empty, StringComparison.Ordinal);
            _clearButton.interactable = _draft != null;
        }

        private void Confirm()
        {
            // Read into a new account: failed saves never mutate the displayed account or active Run.
            var candidate = new MetaProgression();
            if (!_save.TryLoad(candidate, out SaveGameData data, out _) || HasUnfinishedRun(data))
            {
                _blocked = true;
                Refresh();
                _feedback.text = "暂时无法更换，请结束行程或重新打开面板。";
                return;
            }
            if (!candidate.IsArcanaFacilityUnlocked ||
                (_draft != null && !candidate.EquipArcana(_draft)))
            {
                _feedback.text = "天机尚未解锁。";
                return;
            }
            if (_draft == null)
            {
                candidate.equippedArcana = null;
                candidate.hasEquippedArcanaEffect = false;
                candidate.equippedArcanaEffect = ArcanaEffect.Custom;
            }
            try { _save.Save(candidate); }
            catch (Exception error)
            {
                _feedback.text = "保存未成功，原携带天机未改变。请重试。";
                Debug.LogException(error, this);
                return;
            }
            _meta = candidate;
            Refresh();
            _feedback.text = "已保存 · 下次入卷生效";
        }
    }
}
