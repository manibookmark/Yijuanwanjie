using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using WanjieScroll.Core;
using WanjieScroll.UI.Components;

namespace WanjieScroll.UI.Controllers
{
    /// <summary>
    /// Loading scene controller.
    /// Shows the shared loading indicator over the background and then goes to MainMenu.
    /// </summary>
    public class LoadingController : SceneBootstrapBase
    {
        [Header("Optional: Inspector assign, fallback to RuntimeArtLoader")]
        [SerializeField] private Sprite _backgroundSprite;

        private const float LoadTimeoutSeconds = 12f;

        private bool _startedLoading;

        protected override void OnSceneEnter()
        {
            CreateBackground();
            SceneLoadingIndicator.Show();
            StartCoroutine(LoadNextScene());
        }

        private void CreateBackground()
        {
            Sprite bg = _backgroundSprite != null
                ? _backgroundSprite
                : RuntimeArtLoader.GetMainMenuBackground();

            if (bg != null)
                UIElementFactory.CreateBackground(Canvas.transform, bg, Color.white);
            else
                UIElementFactory.CreateBackground(Canvas.transform, color: new Color(0.08f, 0.06f, 0.05f));
        }

        private IEnumerator LoadNextScene()
        {
            if (_startedLoading)
                yield break;

            _startedLoading = true;

            var op = SceneManager.LoadSceneAsync(SceneNames.MainMenu);
            if (op == null)
            {
                Debug.LogError($"[Loading] Cannot load scene '{SceneNames.MainMenu}', check BuildSettings.");
                yield break;
            }

            // No forced wait; load proceeds immediately.
            op.allowSceneActivation = false;

            // If it's already near ready, activate directly.
            if (op.progress >= 0.9f)
            {
                op.allowSceneActivation = true;
                yield break;
            }

            float timeout = 0f;
            while (op.progress < 0.9f)
            {
                timeout += Time.unscaledDeltaTime;
                if (timeout >= LoadTimeoutSeconds)
                {
                    Debug.LogError($"[Loading] Scene load timeout: {SceneNames.MainMenu}, wait {LoadTimeoutSeconds:0}s.");
                    op.allowSceneActivation = true;
                    yield break;
                }

                yield return null;
            }

            op.allowSceneActivation = true;
        }
    }
}
