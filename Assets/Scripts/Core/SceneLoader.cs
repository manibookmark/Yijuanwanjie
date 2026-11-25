using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 场景加载器。在 <see cref="SceneNavigator"/> 之上增加 SceneContext 参数传递能力。
    /// <para>
    /// 使用方式：
    /// <code>
    /// // 带参数
    /// SceneLoader.GoTo(SceneNames.Combat, new SceneContextBuilder()
    ///     .Set("volumeId", "xiyou"));
    ///
    /// // 不带参数（等价于 SceneNavigator.GoToCombat）
    /// SceneLoader.GoTo(SceneNames.MainMenu);
    /// </code>
    /// </para>
    /// <para>不修改 <see cref="SceneNavigator"/>，新旧并行。</para>
    /// </summary>
    public static class SceneLoader
    {
        /// <summary>加载场景，可选携带上下文参数。</summary>
        public static void GoTo(string sceneName, SceneContextBuilder context = null)
        {
            if (context != null)
                context.Apply();
            SceneManager.LoadScene(sceneName);
        }

        /// <summary>异步加载场景，可选携带上下文参数。</summary>
        public static void GoToAsync(string sceneName, SceneContextBuilder context = null,
            Action<float> onProgress = null, Action onLoaded = null)
        {
            if (context != null)
                context.Apply();

            var op = SceneManager.LoadSceneAsync(sceneName);
            if (op == null)
            {
                Debug.LogError($"[SceneLoader] 无法加载场景 '{sceneName}'，请检查 BuildSettings");
                return;
            }

            if (onProgress != null || onLoaded != null)
            {
                var runner = new GameObject("[SceneLoader]");
                var helper = runner.AddComponent<SceneLoaderCoroutine>();
                helper.Run(op, onProgress, onLoaded);
            }
        }

        /// <summary>快速导航方法（与 SceneNavigator 对齐）。</summary>
        public static void GoToLoading(SceneContextBuilder ctx = null) => GoTo(SceneNames.Loading, ctx);
        public static void GoToMainMenu(SceneContextBuilder ctx = null) => GoTo(SceneNames.MainMenu, ctx);
        public static void GoToHub(SceneContextBuilder ctx = null) => GoTo(SceneNames.Hub, ctx);
        public static void GoToLevelSelect(SceneContextBuilder ctx = null) => GoTo(SceneNames.LevelSelect, ctx);
        public static void GoToRouteSelect(SceneContextBuilder ctx = null) => GoTo(SceneNames.RouteSelect, ctx);
        public static void GoToCombat(SceneContextBuilder ctx = null) => GoTo(SceneNames.Combat, ctx);
        public static void GoToDungeon(SceneContextBuilder ctx = null) => GoTo(SceneNames.Dungeon, ctx);
        public static void GoToDaozang(SceneContextBuilder ctx = null) => GoTo(SceneNames.Daozang, ctx);
        public static void GoToOpeningCG(SceneContextBuilder ctx = null) => GoTo(SceneNames.OpeningCG, ctx);
        public static void GoToLevelMap(SceneContextBuilder ctx = null) => GoTo(SceneNames.LevelMap, ctx);
    }

    /// <summary>辅助协程（内部使用）。</summary>
    internal sealed class SceneLoaderCoroutine : MonoBehaviour
    {
        internal void Run(
            AsyncOperation op,
            Action<float> onProgress,
            Action onLoaded)
        {
            StartCoroutine(CoroutineRunner());

            System.Collections.IEnumerator CoroutineRunner()
            {
                while (!op.isDone)
                {
                    onProgress?.Invoke(op.progress);
                    yield return null;
                }
                onLoaded?.Invoke();
                Destroy(gameObject);
            }
        }
    }
}
