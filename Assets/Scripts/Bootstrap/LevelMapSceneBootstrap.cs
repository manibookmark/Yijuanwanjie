using UnityEngine;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// LevelMap 场景装配入口。
    /// MapPresentation 自建相机和 Canvas，CharacterPresentationCoordinator 自建
    /// EventSystem 和 Canvas，因此此处只需挂载 LevelMapBootstrap 即可。
    /// </summary>
    public class LevelMapSceneBootstrap : MonoBehaviour
    {
        private void Start()
        {
            gameObject.AddComponent<LevelMapBootstrap>();
        }
    }
}
