using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Persistence;

namespace WanjieScroll.UI.Components
{
    /// <summary>
    /// 首页存档卷宗。唯一拥有槽位选择、动作切换与五秒删除确认等交互状态，
    /// 存档读写和场景流转通过回调交还给首页装配器。
    /// </summary>
    public sealed class SaveArchivePanelView : MonoBehaviour
    {
        private const float DeleteConfirmTimeoutSeconds = 5f;

        private readonly List<SaveSlotCardView> _cards = new List<SaveSlotCardView>();

        private JadePanelView _shell;
        private Text _errorText;
        private Button _primaryButton;
        private Button _deleteButton;
        private Button _backButton;
        private Text _primaryLabel;
        private Text _deleteLabel;
        private int _selectedSlot;
        private bool _deleteArmed;
        private float _deleteArmedUntil;

        private Action<int> _newGameRequested;
        private Action<int> _loadRequested;
        private Action<int> _deleteRequested;

        public int SelectedSlot => _selectedSlot;

        public void Initialize(
            Action<int> onNewGame,
            Action<int> onLoad,
            Action<int> onDelete,
            Action onClose)
        {
            _newGameRequested = onNewGame;
            _loadRequested = onLoad;
            _deleteRequested = onDelete;

            _shell = gameObject.AddComponent<JadePanelView>();
            _shell.Initialize("行迹存档", new Vector2(1200f, 730f));

            BuildCards();
            BuildFooter(onClose);
            BuildErrorText();
        }

        public void Refresh(
            IReadOnlyList<SaveSlotSummary> summaries,
            int preferredSlot,
            int activeSlot)
        {
            ClearError();
            ResetDeleteConfirmation();

            for (int i = 0; i < _cards.Count; i++)
            {
                SaveSlotSummary summary = FindSummary(summaries, _cards[i].Slot);
                _cards[i].Bind(summary, _cards[i].Slot == activeSlot);
            }

            int selection = IsKnownSlot(preferredSlot) ? preferredSlot : ResolveDefaultSlot(activeSlot);
            SelectSlot(selection);

            SaveSlotCardView selected = FindCard(_selectedSlot);
            if (selected != null && isActiveAndEnabled && gameObject.activeInHierarchy)
                selected.Button.Select();
        }

        public void Show()
        {
            SaveSlotCardView selected = FindCard(_selectedSlot);
            _shell.Show(selected != null ? selected.Button : _backButton);
        }

        public void Hide()
        {
            ResetDeleteConfirmation();
            _shell.Hide();
        }

        public void ShowError(string message)
        {
            if (_errorText == null)
                return;
            _errorText.text = string.IsNullOrWhiteSpace(message)
                ? "存档读取失败"
                : message;
            _errorText.gameObject.SetActive(true);
        }

        public void ClearError()
        {
            if (_errorText == null)
                return;
            _errorText.text = string.Empty;
            _errorText.gameObject.SetActive(false);
        }

        /// <summary>若正在等待删除确认，取消并消费一次 ESC。</summary>
        public bool CancelPendingDelete()
        {
            if (!_deleteArmed)
                return false;
            ResetDeleteConfirmation();
            return true;
        }

        private void Update()
        {
            if (_deleteArmed && Time.unscaledTime > _deleteArmedUntil)
                ResetDeleteConfirmation();
        }

        private void OnDisable()
        {
            ResetDeleteConfirmation();
        }

        private void BuildCards()
        {
            var cardsRoot = new GameObject("ArchiveCards", typeof(RectTransform));
            cardsRoot.transform.SetParent(_shell.ContentRoot, false);
            RectTransform root = cardsRoot.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 1f);
            root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = new Vector2(1044f, 396f);
            root.anchoredPosition = new Vector2(0f, -16f);

            const float spacing = 354f;
            for (int slot = 1; slot <= 3; slot++)
            {
                var cardObject = new GameObject($"SaveSlotCard{slot}", typeof(RectTransform));
                cardObject.transform.SetParent(root, false);
                SaveSlotCardView card = cardObject.AddComponent<SaveSlotCardView>();
                card.Initialize(slot, new Vector2((slot - 2) * spacing, 0f), SelectSlot);
                _cards.Add(card);
            }

            RefreshNavigation();
        }

        private void BuildFooter(Action onClose)
        {
            var footer = new GameObject("ArchiveActions", typeof(RectTransform));
            footer.transform.SetParent(_shell.ContentRoot, false);
            RectTransform footerRect = footer.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0.5f, 0f);
            footerRect.anchorMax = new Vector2(0.5f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.sizeDelta = new Vector2(548f, 74f);
            footerRect.anchoredPosition = Vector2.zero;

            _primaryButton = JadePanelView.CreateTextButton(
                "PrimaryAction", footer.transform, "载入此卷",
                new Vector2(190f, 50f), true);
            RectTransform primaryRect = _primaryButton.GetComponent<RectTransform>();
            primaryRect.anchorMin = new Vector2(0.5f, 0.5f);
            primaryRect.anchorMax = new Vector2(0.5f, 0.5f);
            primaryRect.pivot = new Vector2(0.5f, 0.5f);
            primaryRect.anchoredPosition = new Vector2(-160f, 0f);
            _primaryLabel = _primaryButton.GetComponentInChildren<Text>();
            _primaryButton.onClick.AddListener(OnPrimaryClicked);

            _deleteButton = JadePanelView.CreateTextButton(
                "DeleteAction", footer.transform, "删除存档",
                new Vector2(164f, 50f), false, true);
            RectTransform deleteRect = _deleteButton.GetComponent<RectTransform>();
            deleteRect.anchorMin = new Vector2(0.5f, 0.5f);
            deleteRect.anchorMax = new Vector2(0.5f, 0.5f);
            deleteRect.pivot = new Vector2(0.5f, 0.5f);
            deleteRect.anchoredPosition = new Vector2(29f, 0f);
            _deleteLabel = _deleteButton.GetComponentInChildren<Text>();
            _deleteButton.onClick.AddListener(OnDeleteClicked);

            _backButton = JadePanelView.CreateTextButton(
                "BackAction", footer.transform, "返回",
                new Vector2(132f, 50f), false);
            RectTransform backRect = _backButton.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0.5f, 0.5f);
            backRect.anchorMax = new Vector2(0.5f, 0.5f);
            backRect.pivot = new Vector2(0.5f, 0.5f);
            backRect.anchoredPosition = new Vector2(189f, 0f);
            _backButton.onClick.AddListener(() => onClose?.Invoke());
        }

        private void BuildErrorText()
        {
            _errorText = JadePanelView.CreateText(
                "ArchiveError", _shell.ContentRoot, string.Empty, 16,
                JadePanelView.Danger, TextAnchor.MiddleCenter, true);
            RectTransform rect = _errorText.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(760f, 30f);
            rect.anchoredPosition = new Vector2(0f, -424f);
            _errorText.gameObject.SetActive(false);
        }

        private void SelectSlot(int slot)
        {
            if (!IsKnownSlot(slot))
                return;
            _selectedSlot = slot;
            ResetDeleteConfirmation();

            for (int i = 0; i < _cards.Count; i++)
                _cards[i].SetSelected(_cards[i].Slot == slot);

            RefreshActions();
        }

        private void RefreshActions()
        {
            SaveSlotCardView selected = FindCard(_selectedSlot);
            SaveSlotSummary summary = selected?.Summary;
            bool exists = summary != null && summary.exists;
            bool valid = exists && !summary.corrupt;

            _primaryButton.interactable = selected != null && (valid || !exists);
            _primaryLabel.text = valid
                ? "载入此卷"
                : summary != null && summary.corrupt ? "需先删除" : "开启新卷";
            _deleteButton.gameObject.SetActive(exists);
            LayoutActionButtons(exists);
            RefreshNavigation();
        }

        private void LayoutActionButtons(bool showDelete)
        {
            if (showDelete)
            {
                _primaryButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(-160f, 0f);
                _deleteButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(29f, 0f);
                _backButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(189f, 0f);
                return;
            }

            _primaryButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(-72f, 0f);
            _backButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(101f, 0f);
        }

        private void OnPrimaryClicked()
        {
            SaveSlotSummary summary = FindCard(_selectedSlot)?.Summary;
            if (summary == null || summary.corrupt)
                return;

            ResetDeleteConfirmation();
            if (summary.exists && !summary.corrupt)
                _loadRequested?.Invoke(_selectedSlot);
            else
                _newGameRequested?.Invoke(_selectedSlot);
        }

        private void OnDeleteClicked()
        {
            if (_selectedSlot == 0)
                return;
            if (!_deleteArmed)
            {
                SetDeleteArmed(true);
                return;
            }

            int slot = _selectedSlot;
            ResetDeleteConfirmation();
            _deleteRequested?.Invoke(slot);
        }

        private void SetDeleteArmed(bool armed)
        {
            _deleteArmed = armed;
            _deleteArmedUntil = armed
                ? Time.unscaledTime + DeleteConfirmTimeoutSeconds
                : 0f;
            if (_deleteLabel != null)
                _deleteLabel.text = armed ? "再次点击确认" : "删除存档";
        }

        private void ResetDeleteConfirmation()
        {
            bool wasArmed = _deleteArmed;
            SetDeleteArmed(false);
            if (wasArmed && isActiveAndEnabled)
                RefreshActions();
        }

        private void RefreshNavigation()
        {
            if (_primaryButton == null || _deleteButton == null || _backButton == null)
                return;

            SaveSlotCardView selected = FindCard(_selectedSlot);
            Selectable firstAction = _primaryButton.interactable
                ? (Selectable)_primaryButton
                : _deleteButton.gameObject.activeSelf
                    ? _deleteButton
                    : _backButton;

            for (int i = 0; i < _cards.Count; i++)
            {
                Navigation navigation = _cards[i].Button.navigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnLeft = i > 0
                    ? _cards[i - 1].Button
                    : _cards[_cards.Count - 1].Button;
                navigation.selectOnRight = i < _cards.Count - 1
                    ? _cards[i + 1].Button
                    : _cards[0].Button;
                navigation.selectOnDown = firstAction;
                navigation.selectOnUp = _cards[i].Button;
                _cards[i].Button.navigation = navigation;
            }

            Selectable selectedCard = selected != null ? selected.Button : _cards[0].Button;
            Navigation primaryNavigation = _primaryButton.navigation;
            primaryNavigation.mode = Navigation.Mode.Explicit;
            primaryNavigation.selectOnUp = selectedCard;
            primaryNavigation.selectOnLeft = _backButton;
            primaryNavigation.selectOnRight = _deleteButton.gameObject.activeSelf
                ? _deleteButton
                : _backButton;
            _primaryButton.navigation = primaryNavigation;

            Navigation deleteNavigation = _deleteButton.navigation;
            deleteNavigation.mode = Navigation.Mode.Explicit;
            deleteNavigation.selectOnUp = selectedCard;
            deleteNavigation.selectOnLeft = _primaryButton.interactable
                ? _primaryButton
                : _backButton;
            deleteNavigation.selectOnRight = _backButton;
            _deleteButton.navigation = deleteNavigation;

            Navigation backNavigation = _backButton.navigation;
            backNavigation.mode = Navigation.Mode.Explicit;
            backNavigation.selectOnUp = selectedCard;
            backNavigation.selectOnLeft = _deleteButton.gameObject.activeSelf
                ? _deleteButton
                : _primaryButton;
            backNavigation.selectOnRight = _primaryButton.interactable
                ? _primaryButton
                : _deleteButton;
            _backButton.navigation = backNavigation;
        }

        private int ResolveDefaultSlot(int activeSlot)
        {
            SaveSlotCardView active = FindCard(activeSlot);
            if (active != null && active.Summary != null &&
                active.Summary.exists && !active.Summary.corrupt)
                return activeSlot;

            for (int i = 0; i < _cards.Count; i++)
            {
                SaveSlotSummary summary = _cards[i].Summary;
                if (summary != null && summary.exists && !summary.corrupt)
                    return _cards[i].Slot;
            }
            return _cards.Count > 0 ? _cards[0].Slot : 0;
        }

        private bool IsKnownSlot(int slot)
        {
            return FindCard(slot) != null;
        }

        private SaveSlotCardView FindCard(int slot)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Slot == slot)
                    return _cards[i];
            }
            return null;
        }

        private static SaveSlotSummary FindSummary(
            IReadOnlyList<SaveSlotSummary> summaries,
            int slot)
        {
            if (summaries != null)
            {
                for (int i = 0; i < summaries.Count; i++)
                {
                    if (summaries[i] != null && summaries[i].slot == slot)
                        return summaries[i];
                }
            }
            return new SaveSlotSummary { slot = slot };
        }

    }
}
