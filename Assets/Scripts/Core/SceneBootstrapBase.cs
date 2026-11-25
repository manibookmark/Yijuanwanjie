using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 场景装配基类。幂等地创建 EventSystem、Canvas 和（可选）Camera，
    /// 子类只需实现 <see cref="OnSceneEnter"/> 即可获得标准 UI 基础设施。
    /// <para>Canvas 默认 ScreenSpaceOverlay + 1920×1080 ScaleWithScreenSize。</para>
    /// </summary>
    public abstract class SceneBootstrapBase : MonoBehaviour
    {
        protected Canvas Canvas { get; private set; }
        protected EventSystem EventSystem { get; private set; }

        protected virtual void Awake()
        {
            EventSystem = EnsureEventSystem(transform);
            Canvas = EnsureCanvas(transform);
        }

        protected virtual void Start()
        {
            OnSceneEnter();
        }

        protected virtual void OnDestroy()
        {
            OnSceneExit();
        }

        /// <summary>场景进入时装配 UI 和业务逻辑。</summary>
        protected abstract void OnSceneEnter();

        /// <summary>场景退出时清理事件、协程和临时对象。</summary>
        protected virtual void OnSceneExit() { }

        // ──────────────────────────────────────────────
        //  幂等工具方法（子类和外部均可调用）
        // ──────────────────────────────────────────────

        protected static EventSystem EnsureEventSystem(Transform owner)
        {
            var existing = FindObjectOfType<EventSystem>();
            if (existing != null)
                return existing;

            var go = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            go.transform.SetParent(owner, false);
            return go.GetComponent<EventSystem>();
        }

        protected static Canvas EnsureCanvas(
            Transform owner,
            RenderMode mode = RenderMode.ScreenSpaceOverlay)
        {
            var canvas = owner.GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                var go = new GameObject(
                    "Canvas",
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                go.transform.SetParent(owner, false);
                canvas = go.GetComponent<Canvas>();
                canvas.renderMode = mode;
            }

            // Reused/authored roots follow the same reference fit as new roots.
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            return canvas;
        }

        protected static Camera EnsureCamera(
            Transform owner,
            float orthoSize = 5.4f)
        {
            var cam = owner.GetComponentInChildren<Camera>();
            if (cam != null)
                return cam;

            // Authored cameras may be another root in this same scene. Borrow
            // them without reparenting, reconfiguring or adding a listener; an
            // additive scene or persistent camera belongs to a different owner.
            Camera sceneCamera = null;
            foreach (GameObject root in owner.gameObject.scene.GetRootGameObjects())
            {
                foreach (Camera candidate in root.GetComponentsInChildren<Camera>())
                {
                    if (!candidate.isActiveAndEnabled)
                        continue;
                    if (candidate.CompareTag("MainCamera"))
                        return candidate;
                    if (sceneCamera == null)
                        sceneCamera = candidate;
                }
            }
            if (sceneCamera != null)
                return sceneCamera;

            var go = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
            go.transform.SetParent(owner, false);
            cam = go.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.backgroundColor = new Color(0.08f, 0.06f, 0.05f);
            go.tag = "MainCamera";
            return cam;
        }
    }
}
