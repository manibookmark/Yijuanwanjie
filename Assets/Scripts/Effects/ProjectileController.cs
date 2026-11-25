using System.Collections;
using UnityEngine;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 通用投射物飞行控制。只管飞行、旋转和回收。
    /// 外观委托给 IProjectileVisual，拖尾委托给 TrailModule。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class ProjectileController : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private TrailModule _trail = new TrailModule();
        private IProjectileVisual _visual;
        private bool _flying;
        private int _flightGeneration;
        private System.IDisposable _completionWork;
        private AttackEffectCompletion _completionTicket;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sortingOrder = 20;
        }

        /// <summary>
        /// 设置投射物外观（在 CreateProjectile 后调用）
        /// </summary>
        public void SetVisual(IProjectileVisual visual)
        {
            _visual?.Cleanup();
            _visual = visual;
        }

        /// <summary>
        /// 配置拖尾
        /// </summary>
        public void ConfigureTrail(bool enable, Color color, float width, float time)
        {
            Vector3 offset = _visual != null
                ? _visual.GetTrailAnchorOffset()
                : new Vector3(-0.05f, 0f, 0f);
            _trail.Configure(transform, enable, color, width, time, offset);
        }

        /// <summary>
        /// 飞向目标位置
        /// </summary>
        public IEnumerator Fly(Vector3 start, Vector3 end, float duration,
            bool arc = false, float arcHeight = 1f, System.Action onArrive = null,
            AttackEffectCompletion completion = null)
        {
            _completionWork?.Dispose();
            System.IDisposable work = completion?.HoldWork();
            _completionWork = work;
            _completionTicket = completion;
            int generation = ++_flightGeneration;
            try
            {
                _flying = true;
                if (completion != null && completion.IsCancelled)
                {
                    ReturnFlightUse(generation, work);
                    yield break;
                }
                Vector2 dir = (end - start);
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                transform.position = start;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);

                _trail.Clear();
                if (_visual is IProjectileLaunchVisual launchVisual)
                {
                    yield return launchVisual.PrepareLaunch();
                    // 协程由 VFX manager 承载：池回收的 OnDisable 会取消本次发射。
                    if (this == null || generation != _flightGeneration || !_flying || !gameObject.activeInHierarchy)
                        yield break;
                }
                if (completion != null && completion.IsCancelled)
                {
                    ReturnFlightUse(generation, work);
                    yield break;
                }
                _visual?.OnFlightStart();

                float timer = 0f;

                while (generation == _flightGeneration && timer < duration &&
                    (completion == null || !completion.IsCancelled))
                {
                    timer += Time.deltaTime;
                    float t = timer / duration;

                    if (arc)
                    {
                        Vector3 linear = Vector3.Lerp(start, end, t);
                        float parabola = arcHeight * 4f * t * (1f - t);
                        transform.position = linear + new Vector3(0f, parabola, 0f);
                        transform.rotation = Quaternion.Euler(0, 0, angle);
                    }
                    else
                    {
                        transform.position = Vector3.Lerp(start, end, t);
                        transform.rotation = Quaternion.Euler(0, 0, angle);
                    }

                    yield return null;
                }

                if (this == null || generation != _flightGeneration || !gameObject.activeInHierarchy)
                    yield break;
                if (completion != null && completion.IsCancelled)
                {
                    ReturnFlightUse(generation, work);
                    yield break;
                }
                transform.position = end;
                _flying = false;

                onArrive?.Invoke();
                if (this == null || generation != _flightGeneration || !gameObject.activeInHierarchy)
                    yield break;
                _visual?.OnFlightEnd();

                if (_trail != null)
                    yield return new WaitForSeconds(0.2f);

                if (this == null || generation != _flightGeneration || !gameObject.activeInHierarchy)
                    yield break;
                ReturnFlightUse(generation, work);
            }
            finally
            {
                work?.Dispose();
                if (ReferenceEquals(_completionWork, work))
                {
                    _completionWork = null;
                    _completionTicket = null;
                }
            }
        }

        private void ReturnFlightUse(int generation, System.IDisposable work)
        {
            if (this == null || generation != _flightGeneration || !gameObject.activeInHierarchy) return;
            _flying = false;
            // A later reuse of the pooled GameObject cannot extend this use.
            work?.Dispose();
            if (ReferenceEquals(_completionWork, work))
            {
                _completionWork = null;
                _completionTicket = null;
            }
            var manager = VisualEffectManager.Instance;
            if (manager != null) manager.ReturnProjectile(gameObject);
            else Destroy(gameObject);
        }

        public void SetTrailColor(Color color)
        {
            // 颜色已通过 ConfigureTrail 设置，这里仅兼容旧调用
        }

        public bool IsFlying => _flying;

        void OnDisable()
        {
            ++_flightGeneration;
            // An externally disabled in-flight pooled object did not arrive.
            // Cancel its source ticket; the bridge retires that source without
            // fabricating a contact callback. Normal pool return is not flying.
            if (_flying) _completionTicket?.Cancel();
            _flying = false;
            _completionWork?.Dispose();
            _completionWork = null;
            _completionTicket = null;
        }

        private void OnDestroy()
        {
            if (_flying) _completionTicket?.Cancel();
            _completionWork?.Dispose();
            _completionWork = null;
            _completionTicket = null;
            _visual?.Cleanup();
        }
    }
}
