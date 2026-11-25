using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.UI.Maps
{
    /// <summary>
    /// Owns only Crystal overlay motion, flying Images and feedback lifecycle.
    /// Dungeon owns every clock, physical card and award. Source flights home
    /// towards the moving target for .4 seconds; completion asks Arrived, never
    /// infers a collision from a click. Source units are projected at 100 UI
    /// pixels/unit. Plane projection, punch waveform and restored flight restart
    /// are presentation adaptations, not a claim of saved VC world positions.
    /// All hierarchy/controls are authored in a persistent prefab; runtime only
    /// instantiates its card template. No Audio/Effects/Infinity access occurs.
    /// </summary>
    public sealed class CrystalThrowingView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _arena;
        [SerializeField] private Image _target;
        [SerializeField] private RectTransform _launch;
        [SerializeField] private Image _cardTemplate;
        [SerializeField] private CrystalThrowingInput _input;
        [SerializeField] private Text _timer;
        [SerializeField] private Text _status;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Text _pauseLabel;
        [SerializeField] private Button _closeButton;
        [SerializeField] private GameObject _problemRoot;
        [SerializeField] private Text _problemText;
        [SerializeField] private Button _retryButton;

        private const float PixelsPerUnit = 100f;
        private const float FlightTime = .4f;
        private const float RotationsPerSecond = 5f;
        private const float TargetCardScale = .4f;
        private const float JumpTime = .29f;
        private readonly Dictionary<int, Flight> _flights = new Dictionary<int, Flight>();
        private readonly List<int> _flightOrder = new List<int>();
        private readonly Dictionary<int, Sprite> _cardArtwork = new Dictionary<int, Sprite>();
        private System.Random _visualRandom;
        private CrystalRunOffer _offer;
        private Vector2 _targetPosition, _destination, _exitStart;
        private float _waitRemaining, _punchElapsed = JumpTime;
        private bool _waiting, _paused, _initialized, _buttonsBound, _problemOpen, _retrying;
        private int _generation;
        private Action _retry;

        private sealed class Flight
        {
            internal Image Image;
            internal Vector3 StartPosition;
            internal float Elapsed;
            internal bool YieldedFirstFrame;
        }

        public Func<int, bool> Arrived { get; set; }
        public event Action CloseRequested;
        public event Action PauseRequested;

        /// <summary>Call on the prefab before starting any durable domain action.</summary>
        public bool HasRequiredBindings(out string error)
        {
            error = null;
            if (_canvasGroup == null || _arena == null || _target == null || _launch == null ||
                _cardTemplate == null || _input == null || _timer == null || _status == null ||
                _pauseButton == null || _pauseLabel == null || _closeButton == null ||
                _problemRoot == null || _problemText == null || _retryButton == null)
            { error = "晶体投牌面板缺少预制体绑定。"; return false; }
            if (GetComponent<Canvas>() == null || GetComponent<GraphicRaycaster>() == null)
            { error = "晶体投牌面板缺少输入画布。"; return false; }
            if (_target.rectTransform.parent != _arena || _launch.parent != _arena ||
                _cardTemplate.rectTransform.parent != _arena || _cardTemplate.gameObject.activeSelf ||
                !_input.transform.IsChildOf(transform) || !_problemRoot.transform.IsChildOf(transform))
            { error = "晶体投牌面板层级与卡牌模板不符合合同。"; return false; }
            return true;
        }

        public void Initialize(Sprite targetArtwork, IReadOnlyDictionary<int, Sprite> cards, CrystalRunOffer offer)
        {
            if (!HasRequiredBindings(out string error)) throw new InvalidOperationException(error);
            if (targetArtwork == null) throw new ArgumentNullException(nameof(targetArtwork));
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            if (offer == null) throw new ArgumentNullException(nameof(offer));
            var captured = new Dictionary<int, Sprite>();
            foreach (var row in cards)
            {
                if (row.Key <= 0 || row.Value == null) throw new ArgumentException("Every physical card needs its own artwork.", nameof(cards));
                captured.Add(row.Key, row.Value);
            }
            foreach (int id in offer.FlyingCardInstanceIds)
                if (!captured.ContainsKey(id)) throw new ArgumentException("Saved flying card artwork is missing.", nameof(cards));
            Close();
            foreach (var row in captured) _cardArtwork.Add(row.Key, row.Value);
            BindButtons();
            _target.sprite = targetArtwork; _target.preserveAspect = true;
            _targetPosition = Vector2.zero; _target.rectTransform.anchoredPosition = _targetPosition;
            _target.rectTransform.localScale = Vector3.one;
            _target.rectTransform.localRotation = Quaternion.identity;
            _visualRandom = new System.Random(unchecked(GetInstanceID() * 397 ^ offer.NodeContentId.GetHashCode()));
            _waiting = false; _waitRemaining = 0; ChooseDestination();
            _punchElapsed = JumpTime; _initialized = true;
            _canvasGroup.alpha = 1; _canvasGroup.blocksRaycasts = true; _canvasGroup.interactable = true;
            _input.SynchronizeSavedHold(offer.HeldButtonCount);
            ApplyOffer(offer);
            // Exact saved unhit membership restarts from launch. No old impact
            // or past path is fabricated, and restoration cannot grant XP.
            foreach (int id in offer.FlyingCardInstanceIds) CreateFlight(id);
        }

        public void ReadInput(out bool pressed, out bool released, out bool resettingSavedHold)
        {
            pressed = false; released = false; resettingSavedHold = false;
            if (!_initialized || _paused || _problemOpen || _offer.Stage != CrystalThrowingStage.Throwing) return;
            _input.ReadInput(out pressed, out released, out resettingSavedHold);
        }

        /// <summary>Projects an immutable durable offer; never advances its clocks.</summary>
        public void ApplyOffer(CrystalRunOffer offer)
        {
            if (!_initialized) throw new InvalidOperationException("Crystal presentation was not initialized.");
            if (offer == null || (_offer != null && !string.Equals(_offer.NodeContentId, offer.NodeContentId, StringComparison.Ordinal)))
                throw new ArgumentException("Crystal offer identity changed.", nameof(offer));
            if (_offer != null && offer.AcceptedHitCount > _offer.AcceptedHitCount) _punchElapsed = 0;
            if (offer.Stage == CrystalThrowingStage.Ending && (_offer == null || _offer.Stage != CrystalThrowingStage.Ending))
                _exitStart = _targetPosition;
            _offer = offer;
            _input.ApplySavedHold(offer.HeldButtonCount);
            for (int i = _flightOrder.Count - 1; i >= 0; i--)
                if (!Contains(offer.FlyingCardInstanceIds, _flightOrder[i])) RemoveFlight(_flightOrder[i]);
            _timer.text = Mathf.Max(0, offer.EventDuration - offer.EventElapsed).ToString("0.0") + " 秒";
            _status.text = offer.Stage == CrystalThrowingStage.Preparing ? "准备投牌" :
                offer.Stage == CrystalThrowingStage.Ending ? "收回卡牌" :
                offer.Stage == CrystalThrowingStage.Returned ? "投牌结束" :
                "剩余 " + offer.RemainingCards + " 张 · 获得经验 " + offer.ActualExperience;
            RefreshInput();
        }

        public void PresentThrows(IReadOnlyList<CrystalCardThrow> throws)
        {
            if (!_initialized || throws == null) throw new ArgumentException("Committed throws are required.", nameof(throws));
            if (_offer.Stage != CrystalThrowingStage.Throwing) return;
            foreach (var item in throws)
            {
                if (!Contains(_offer.FlyingCardInstanceIds, item.RuntimeInstanceId))
                    throw new InvalidOperationException("A displayed throw is absent from the durable flying membership.");
                CreateFlight(item.RuntimeInstanceId);
            }
        }

        /// <summary>Host advances presentation only after its domain frame was durably saved.</summary>
        public void AdvancePresentation(float delta, bool paused)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (!_initialized || paused || _paused || _problemOpen || delta == 0) return;
            if (_offer.Stage == CrystalThrowingStage.Throwing) AdvanceTarget(delta);
            else if (_offer.Stage == CrystalThrowingStage.Ending)
            {
                // Source exit targets viewport x=1.3. This planar projection
                // uses the durable exit clock; no completion is awarded here.
                float t = _offer.ExitDuration > 0 ? Mathf.Clamp01(_offer.ExitElapsed / _offer.ExitDuration) : 1;
                _targetPosition = Vector2.Lerp(_exitStart, new Vector2(_arena.rect.width * .8f, _exitStart.y), t);
                _target.rectTransform.anchoredPosition = _targetPosition;
            }
            if (_offer.Stage != CrystalThrowingStage.Throwing) return;
            int generation = _generation;
            int[] current = _flightOrder.ToArray();
            foreach (int id in current)
            {
                if (!_flights.TryGetValue(id, out Flight flight)) continue;
                // Source executes Lerp at elapsed=0, yields NextFrame, then
                // adds delta and checks the strict elapsed<.4 loop. A newly
                // displayed throw must not spend its creation frame's delta.
                if (flight.YieldedFirstFrame) flight.Elapsed = Mathf.Min(FlightTime, flight.Elapsed + delta);
                if (flight.Elapsed < FlightTime)
                {
                    float t = Mathf.Clamp01(flight.Elapsed / FlightTime);
                    var rect = flight.Image.rectTransform;
                    rect.localPosition = Vector3.Lerp(flight.StartPosition, _target.rectTransform.localPosition, t);
                    rect.localRotation = Quaternion.AngleAxis(RotationsPerSecond * 360f * flight.Elapsed, Vector3.forward) * Quaternion.Euler(70, 0, 0);
                    rect.localScale = Vector3.one * Mathf.Lerp(1, TargetCardScale, t);
                    flight.YieldedFirstFrame = true;
                    continue;
                }
                bool accepted;
                try { accepted = Arrived != null && Arrived(id); }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Crystal] 命中提交未完成：" + exception.Message);
                    ShowProblem(exception.Message, null); accepted = false;
                }
                if (!_initialized || generation != _generation) return;
                if (!accepted) { SetPaused(true); return; }
                RemoveFlight(id);
                if (_paused || _problemOpen) return; // Accepted hit may open natural rewards synchronously.
            }
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (!_initialized) return;
            _pauseLabel.text = paused ? "继续" : "暂停";
            RefreshInput();
        }

        /// <summary>Diagnostic detail stays in logs; only retry is offered, never a fake return/completion.</summary>
        public void ShowProblem(string detail, Action retry)
        {
            if (!_initialized) return;
            Debug.LogWarning("[Crystal] 投牌表现暂停：" + detail);
            _retry = retry; _problemOpen = true;
            SetPaused(true);
            _problemText.text = "当前动作未完成，请重试。";
            _retryButton.interactable = retry != null;
            _problemRoot.SetActive(true);
        }

        public void Close()
        {
            ++_generation; _initialized = false;
            if (_input != null) _input.SetInputEnabled(false);
            for (int i = _flightOrder.Count - 1; i >= 0; i--) RemoveFlight(_flightOrder[i]);
            _cardArtwork.Clear(); _offer = null; _retry = null; _problemOpen = false; _retrying = false;
            _paused = false;
            if (_problemRoot != null) _problemRoot.SetActive(false);
            if (_canvasGroup != null) { _canvasGroup.interactable = false; _canvasGroup.blocksRaycasts = false; }
        }

        private void CreateFlight(int id)
        {
            if (_flights.ContainsKey(id)) return;
            if (!_cardArtwork.TryGetValue(id, out Sprite artwork) || artwork == null)
                throw new InvalidOperationException("A physical throw has no captured card artwork.");
            Image image = Instantiate(_cardTemplate, _arena, false);
            image.name = "CrystalCard." + id;
            image.sprite = artwork; image.preserveAspect = true; image.raycastTarget = false;
            image.rectTransform.localPosition = _launch.localPosition;
            image.rectTransform.localRotation = Quaternion.Euler(70, 0, 0);
            image.rectTransform.localScale = Vector3.one;
            image.gameObject.SetActive(true);
            _flights.Add(id, new Flight { Image = image, StartPosition = image.rectTransform.localPosition });
            _flightOrder.Add(id);
        }

        private void RemoveFlight(int id)
        {
            if (!_flights.TryGetValue(id, out Flight flight)) return;
            _flights.Remove(id); _flightOrder.Remove(id);
            if (flight.Image != null) { flight.Image.gameObject.SetActive(false); Destroy(flight.Image.gameObject); }
        }

        private void AdvanceTarget(float delta)
        {
            if (_waiting)
            {
                _waitRemaining -= delta;
                if (_waitRemaining <= 0) { _waiting = false; ChooseDestination(); }
            }
            else
            {
                float speed = (4f + _offer.AcceptedHitCount) * PixelsPerUnit;
                _targetPosition = Vector2.MoveTowards(_targetPosition, _destination, speed * delta);
                if ((_targetPosition - _destination).sqrMagnitude < .0025f * PixelsPerUnit * PixelsPerUnit)
                { _waiting = true; _waitRemaining = (float)_visualRandom.NextDouble() * .1f; }
            }
            _punchElapsed = Mathf.Min(JumpTime, _punchElapsed + delta);
            float t = _punchElapsed / JumpTime;
            // Same .7-unit/.29-second hit reaction parameters; the overlay's
            // decaying waveform is a 2D adaptation of source DOPunchPosition.
            float punch = t < 1 ? Mathf.Sin(t * Mathf.PI * 4) * (1 - t) * .7f * PixelsPerUnit : 0;
            _target.rectTransform.anchoredPosition = _targetPosition + new Vector2(0, punch);
        }

        private void ChooseDestination()
        {
            // Visual RNG is private and does not consume source/Run award RNG.
            _destination = new Vector2(((float)_visualRandom.NextDouble() * 2 - 1) * 4.05f,
                ((float)_visualRandom.NextDouble() * 2 - 1) * 1.1f) * PixelsPerUnit;
        }

        private void RefreshInput()
        {
            _input.SetInputEnabled(_initialized && !_paused && !_problemOpen && _offer != null && _offer.Stage == CrystalThrowingStage.Throwing);
        }

        private void BindButtons()
        {
            if (_buttonsBound) return;
            _pauseButton.onClick.AddListener(OnPauseRequested);
            _closeButton.onClick.AddListener(OnCloseRequested);
            _retryButton.onClick.AddListener(OnRetry);
            _buttonsBound = true;
        }

        private void OnPauseRequested() { if (_initialized && !_problemOpen) PauseRequested?.Invoke(); }
        private void OnCloseRequested() { if (_initialized && !_problemOpen) CloseRequested?.Invoke(); }
        private void OnRetry()
        {
            if (!_initialized || !_problemOpen || _retry == null || _retrying) return;
            Action retry = _retry; int generation = _generation;
            _retry = null; _problemOpen = false; _retrying = true;
            _problemRoot.SetActive(false);
            try { retry(); }
            catch (Exception exception) { if (_initialized && generation == _generation) ShowProblem(exception.Message, retry); }
            finally { if (generation == _generation) _retrying = false; }
        }

        private static bool Contains(IReadOnlyList<int> ids, int id)
        { for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return true; return false; }
        private void OnDisable() => Close();
        private void OnDestroy()
        {
            Close();
            if (!_buttonsBound) return;
            _pauseButton.onClick.RemoveListener(OnPauseRequested);
            _closeButton.onClick.RemoveListener(OnCloseRequested);
            _retryButton.onClick.RemoveListener(OnRetry);
        }
    }
}
