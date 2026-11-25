using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.UI.Battle;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 单次敌人动作的视觉节拍。编队模块负责提供已解除渐显冲突的渲染器，
    /// 本模块只负责动作协程和 impact/complete 边界。
    /// 挂有 BossSequenceAnimator 的 BOSS 走序列帧分支（意图驱动），
    /// 其余敌人沿用位移协程。
    /// </summary>
    internal sealed class EnemyActionPresenter : MonoBehaviour
    {
        private const float AttackAnticipationDuration = 0.07f;
        private const float AttackLungeDuration = 0.10f;
        private const float AttackReturnDuration = 0.11f;

        private readonly HashSet<int> _activeEnemyIndices =
            new HashSet<int>();
        private readonly Dictionary<int, ActionSnapshot> _snapshots =
            new Dictionary<int, ActionSnapshot>();
        private readonly HashSet<int> _activeBossIndices =
            new HashSet<int>();
        private readonly Dictionary<int, PresentedAction> _actions =
            new Dictionary<int, PresentedAction>();
        private long _nextGeneration;
        private bool _cancelling;
        public event Action<EnemyActionPresentationSignal> ActionPresentationSignaled;
        private EnemyAreaPresentationProfile _presentation;
        private Func<int, SpriteRenderer> _acquireRenderer;

        public bool IsActive =>
            _activeEnemyIndices.Count > 0 || _activeBossIndices.Count > 0;

        public void Bind(
            EnemyAreaPresentationProfile presentation,
            Func<int, SpriteRenderer> acquireRenderer)
        {
            CancelPresentations();
            _presentation = presentation;
            _acquireRenderer = acquireRenderer;
        }

        public void Unbind()
        {
            CancelPresentations();
            _presentation = null;
            _acquireRenderer = null;
        }

        public bool PlayAttackMotion(int enemyIndex, Action onImpact, Action onComplete)
            => PlayAttackMotion(enemyIndex, null, onImpact, onComplete);

        public bool PlayAttackMotion(int enemyIndex, string actionId,
            Action onImpact, Action onComplete, Action onInterrupted = null)
        {
            if (_cancelling || !isActiveAndEnabled)
            {
                onInterrupted?.Invoke();
                return true;
            }
            RetireExisting(enemyIndex);
            if (_actions.ContainsKey(enemyIndex))
            {
                onInterrupted?.Invoke();
                return true;
            }
            if (TryPlayBossAction(enemyIndex, BossSequenceAnimator.BossAction.Attack,
                actionId, onImpact, onComplete, onInterrupted)) return true;
            SpriteRenderer renderer = AcquireRenderer(enemyIndex);
            if (renderer == null)
            {
                onImpact?.Invoke();
                onComplete?.Invoke();
                return false;
            }
            PresentedAction session = BeginAction(enemyIndex, actionId, null, false,
                onImpact, onComplete, onInterrupted);
            if (IsCurrent(session))
            {
                Coroutine started = StartCoroutine(AttackMotion(enemyIndex, renderer,
                    () => DeliverImpact(session), () => CompleteAction(session),
                    () => IsCurrent(session)));
                if (IsCurrent(session)) session.Routine = started;
            }
            return true;
        }

        public bool PlayUtilityMotion(int enemyIndex, Action onImpact, Action onComplete)
            => PlayUtilityMotion(enemyIndex, null, onImpact, onComplete);

        public bool PlayUtilityMotion(int enemyIndex, string actionId,
            Action onImpact, Action onComplete, Action onInterrupted = null)
        {
            if (_cancelling || !isActiveAndEnabled)
            {
                onInterrupted?.Invoke();
                return true;
            }
            RetireExisting(enemyIndex);
            if (_actions.ContainsKey(enemyIndex))
            {
                onInterrupted?.Invoke();
                return true;
            }
            if (TryPlayBossAction(enemyIndex, BossSequenceAnimator.BossAction.Defend,
                actionId, onImpact, onComplete, onInterrupted)) return true;
            SpriteRenderer renderer = AcquireRenderer(enemyIndex);
            if (renderer == null)
            {
                onImpact?.Invoke();
                onComplete?.Invoke();
                return false;
            }
            PresentedAction session = BeginAction(enemyIndex, actionId, null, false,
                onImpact, onComplete, onInterrupted);
            if (IsCurrent(session))
            {
                Coroutine started = StartCoroutine(UtilityMotion(enemyIndex, renderer,
                    () => DeliverImpact(session), () => CompleteAction(session),
                    () => IsCurrent(session)));
                if (IsCurrent(session)) session.Routine = started;
            }
            return true;
        }

        public void CancelPresentations()
        {
            // Snapshot first: cancellation callbacks remove their own records and may re-enter.
            if (_cancelling) return;
            _cancelling = true;
            try
            {
                var old = new List<PresentedAction>(_actions.Values);
                foreach (PresentedAction session in old) CancelAction(session);
            }
            finally { _cancelling = false; }
            // Each owned routine is stopped by CancelAction; never stop a replacement started
            // re-entrantly by an interrupted listener with a global StopAllCoroutines.
        }

        private sealed class PresentedAction
        {
            public EnemyActionPresentationLifetime Lifetime;
            public BossSequenceAnimator Animator;
            public BossAttackCloseupPresenter Closeup;
            public Coroutine Routine;
            public Action Impact;
            public Action Complete;
            public Action Interrupted;
        }

        private PresentedAction BeginAction(int index, string actionId,
            BossSequenceAnimator animator, bool hasRelease,
            Action impact, Action complete, Action interrupted)
        {
            var session = new PresentedAction
            {
                Lifetime = new EnemyActionPresentationLifetime(
                    ++_nextGeneration, index, actionId, hasRelease),
                Animator = animator, Impact = impact, Complete = complete, Interrupted = interrupted
            };
            _actions.Add(index, session);
            Publish(session, EnemyActionPresentationSignalKind.Started);
            return session;
        }

        private bool IsCurrent(PresentedAction session)
            => session != null && !session.Lifetime.IsTerminal &&
               _actions.TryGetValue(session.Lifetime.EnemyIndex, out PresentedAction current) &&
               ReferenceEquals(current, session);

        private void RetireExisting(int index)
        {
            if (_actions.TryGetValue(index, out PresentedAction previous)) CancelAction(previous);
        }

        private bool TryPlayBossAction(int enemyIndex, BossSequenceAnimator.BossAction action,
            string actionId, Action impact, Action complete, Action interrupted)
        {
            SpriteRenderer renderer = SafeAcquireRenderer(enemyIndex);
            BossAttackCloseupPresenter closeup = renderer != null
                ? renderer.GetComponent<BossAttackCloseupPresenter>() : null;
            if (action == BossSequenceAnimator.BossAction.Attack &&
                closeup != null && closeup.CanPlay(actionId))
            {
                BossSequenceAnimator idleAnimator = renderer.GetComponent<BossSequenceAnimator>();
                PresentedAction closeupSession = BeginAction(enemyIndex, actionId,
                    idleAnimator, false, impact, complete, interrupted);
                closeupSession.Closeup = closeup;
                if (!IsCurrent(closeupSession)) return true;
                _activeBossIndices.Add(enemyIndex);
                idleAnimator?.StopAll();
                if (!closeup.TryPlay(actionId, () => DeliverImpact(closeupSession),
                    () => CompleteAction(closeupSession), () => CancelAction(closeupSession)))
                    CancelAction(closeupSession);
                return true;
            }
            BossSequenceAnimator animator = FindBossAnimatorByIndex(enemyIndex);
            if (animator == null || !animator.CanPlayAction(action, actionId)) return false;
            PresentedAction session = BeginAction(enemyIndex, actionId, animator,
                animator.HasSpellReleaseMarker(action, actionId), impact, complete, interrupted);
            if (!IsCurrent(session)) return true;
            _activeBossIndices.Add(enemyIndex);
            animator.PlayAction(action, actionId, () => DeliverImpact(session),
                () => CompleteAction(session), () => CancelAction(session),
                world => DeliverRelease(session, world));
            return true;
        }

        private void DeliverImpact(PresentedAction session)
        {
            if (!IsCurrent(session)) return;
            Vector3? anchor = null;
            if (session.Closeup != null && session.Closeup.TryGetImpactAnchorWorld(out Vector3 world))
                anchor = world;
            if (!session.Lifetime.TryCreateImpactSignal(anchor, out EnemyActionPresentationSignal signal)) return;
            try
            {
                session.Impact?.Invoke();
                if (IsCurrent(session)) Publish(signal);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                CancelAction(session);
            }
        }

        private void DeliverRelease(PresentedAction session, Vector3? world)
        {
            if (!IsCurrent(session) || !session.Lifetime.TryDeliverRelease()) return;
            Publish(session, EnemyActionPresentationSignalKind.SpellRelease, world);
        }

        private void CompleteAction(PresentedAction session)
        {
            if (!IsCurrent(session) || !session.Lifetime.TryComplete()) return;
            int index = session.Lifetime.EnemyIndex;
            _actions.Remove(index);
            _activeBossIndices.Remove(index);
            _activeEnemyIndices.Remove(index);
            _snapshots.Remove(index);
            session.Routine = null;
            if (session.Closeup != null && session.Animator != null &&
                session.Animator.isActiveAndEnabled) session.Animator.EnterIdle();
            Publish(session, EnemyActionPresentationSignalKind.Completed);
            session.Complete?.Invoke();
        }

        private void CancelAction(PresentedAction session)
        {
            if (!IsCurrent(session) || !session.Lifetime.TryCancel()) return;
            int index = session.Lifetime.EnemyIndex;
            _actions.Remove(index);
            _activeBossIndices.Remove(index);
            _activeEnemyIndices.Remove(index);
            Coroutine previous = session.Routine;
            session.Routine = null;
            if (previous != null) StopCoroutine(previous);
            if (_snapshots.TryGetValue(index, out ActionSnapshot snapshot)) snapshot.Restore();
            _snapshots.Remove(index);
            session.Closeup?.Cancel();
            session.Animator?.StopAll();
            Publish(session, EnemyActionPresentationSignalKind.Cancelled);
            try { session.Interrupted?.Invoke(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private void Publish(PresentedAction session, EnemyActionPresentationSignalKind kind,
            Vector3? anchor = null)
        {
            EnemyActionPresentationLifetime state = session.Lifetime;
            var signal = new EnemyActionPresentationSignal(kind, state.Generation, state.EnemyIndex,
                state.ActionId, state.WillEmitSpellRelease, anchor.HasValue,
                anchor.GetValueOrDefault());
            Publish(signal);
        }

        private void Publish(EnemyActionPresentationSignal signal)
        {
            Action<EnemyActionPresentationSignal> listeners = ActionPresentationSignaled;
            if (listeners == null) return;
            foreach (Action<EnemyActionPresentationSignal> listener in listeners.GetInvocationList())
            {
                try { listener(signal); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }

        private BossSequenceAnimator FindBossAnimatorByIndex(int enemyIndex)
        {
            SpriteRenderer renderer = SafeAcquireRenderer(enemyIndex);
            return renderer != null
                ? renderer.GetComponent<BossSequenceAnimator>()
                : null;
        }

        private SpriteRenderer SafeAcquireRenderer(int enemyIndex)
        {
            try
            {
                return _acquireRenderer?.Invoke(enemyIndex);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private SpriteRenderer AcquireRenderer(int enemyIndex)
        {
            if (_activeEnemyIndices.Contains(enemyIndex))
                return null;
            return _acquireRenderer?.Invoke(enemyIndex);
        }

        private IEnumerator AttackMotion(
            int enemyIndex,
            SpriteRenderer renderer,
            Action onImpact,
            Action onComplete,
            Func<bool> isCurrent)
        {
            _activeEnemyIndices.Add(enemyIndex);
            Transform target = renderer.transform;
            Vector3 originPosition = target.localPosition;
            Vector3 originScale = target.localScale;
            Color originColor = renderer.color;
            _snapshots[enemyIndex] = new ActionSnapshot(
                target,
                renderer,
                originPosition,
                originScale,
                originColor);
            float cellDepth = _presentation != null
                ? Mathf.Abs(
                    _presentation.gridBackY - _presentation.gridFrontY) /
                  Mathf.Max(1, _presentation.VisibleRows)
                : 1.35f;
            Vector3 presentationPosition =
                transform.InverseTransformPoint(target.position);
            Vector3 windupPosition = originPosition +
                ConvertPresentationDeltaToTargetLocal(
                    target,
                    new Vector3(0f, cellDepth * 0.08f, 0f));
            Vector3 contactPosition = originPosition +
                ConvertPresentationDeltaToTargetLocal(
                    target,
                    new Vector3(
                        -presentationPosition.x * 0.08f,
                        -cellDepth * 0.46f,
                        0f));
            Vector3 windupScale = Vector3.Scale(
                originScale,
                new Vector3(1.06f, 0.92f, 1f));
            Vector3 contactScale = Vector3.Scale(
                originScale,
                new Vector3(0.96f, 1.08f, 1f));

            yield return AnimateTransform(
                target,
                originPosition,
                windupPosition,
                originScale,
                windupScale,
                AttackAnticipationDuration,
                false);
            yield return AnimateTransform(
                target,
                windupPosition,
                contactPosition,
                windupScale,
                contactScale,
                AttackLungeDuration,
                true);

            onImpact?.Invoke();
            if (isCurrent?.Invoke() == false) yield break;
            // Core 刷新可能在命中帧恢复阵型位置，回弹前重新落到接触点。
            if (target != null)
            {
                target.localPosition = contactPosition;
                target.localScale = contactScale;
            }
            if (renderer != null)
            {
                renderer.color = new Color(
                    1f,
                    0.76f,
                    0.58f,
                    originColor.a);
            }

            yield return AnimateTransform(
                target,
                contactPosition,
                originPosition,
                contactScale,
                originScale,
                AttackReturnDuration,
                true);

            if (renderer != null)
                renderer.color = originColor;
            _snapshots.Remove(enemyIndex);
            _activeEnemyIndices.Remove(enemyIndex);
            onComplete?.Invoke();
        }

        private IEnumerator UtilityMotion(
            int enemyIndex,
            SpriteRenderer renderer,
            Action onImpact,
            Action onComplete,
            Func<bool> isCurrent)
        {
            _activeEnemyIndices.Add(enemyIndex);
            Transform target = renderer.transform;
            Vector3 originPosition = target.localPosition;
            Vector3 originScale = target.localScale;
            Color originColor = renderer.color;
            _snapshots[enemyIndex] = new ActionSnapshot(
                target,
                renderer,
                originPosition,
                originScale,
                originColor);
            Vector3 pulseScale = originScale * 1.08f;
            pulseScale.z = originScale.z;

            yield return AnimateTransform(
                target,
                originPosition,
                originPosition,
                originScale,
                pulseScale,
                0.08f,
                true);
            onImpact?.Invoke();
            if (isCurrent?.Invoke() == false) yield break;
            if (renderer != null)
            {
                renderer.color = new Color(
                    0.72f,
                    0.92f,
                    1f,
                    originColor.a);
            }
            yield return AnimateTransform(
                target,
                originPosition,
                originPosition,
                pulseScale,
                originScale,
                0.12f,
                true);

            if (renderer != null)
                renderer.color = originColor;
            _snapshots.Remove(enemyIndex);
            _activeEnemyIndices.Remove(enemyIndex);
            onComplete?.Invoke();
        }

        private readonly struct ActionSnapshot
        {
            private readonly Transform _target;
            private readonly SpriteRenderer _renderer;
            private readonly Vector3 _position;
            private readonly Vector3 _scale;
            private readonly Color _color;

            public ActionSnapshot(
                Transform target,
                SpriteRenderer renderer,
                Vector3 position,
                Vector3 scale,
                Color color)
            {
                _target = target;
                _renderer = renderer;
                _position = position;
                _scale = scale;
                _color = color;
            }

            public void Restore()
            {
                if (_target != null)
                {
                    _target.localPosition = _position;
                    _target.localScale = _scale;
                }
                if (_renderer != null)
                    _renderer.color = _color;
            }
        }

        /// <summary>
        /// 动作根位于已缩放的编队根之下，而动作配置使用 EnemyArea 坐标。
        /// 在这里集中完成向量换算，保证不同体型的敌人拥有相同的屏幕位移，
        /// 同时不让动作模块读取 FormationRoot 的层级细节。
        /// </summary>
        private Vector3 ConvertPresentationDeltaToTargetLocal(
            Transform target,
            Vector3 presentationDelta)
        {
            Vector3 worldDelta = transform.TransformVector(presentationDelta);
            Transform parent = target != null ? target.parent : null;
            return parent != null
                ? parent.InverseTransformVector(worldDelta)
                : worldDelta;
        }

        private static IEnumerator AnimateTransform(
            Transform target,
            Vector3 fromPosition,
            Vector3 toPosition,
            Vector3 fromScale,
            Vector3 toScale,
            float duration,
            bool easeOut)
        {
            float elapsed = 0f;
            while (target != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = easeOut
                    ? 1f - Mathf.Pow(1f - t, 3f)
                    : t * t;
                target.localPosition = Vector3.LerpUnclamped(
                    fromPosition,
                    toPosition,
                    eased);
                target.localScale = Vector3.LerpUnclamped(
                    fromScale,
                    toScale,
                    eased);
                yield return null;
            }

            if (target == null) yield break;
            target.localPosition = toPosition;
            target.localScale = toScale;
        }

        private void OnDisable()
        {
            CancelPresentations();
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
