using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 通用拖尾模块。管理 TrailRenderer 的创建、配置、位置偏移和清理。
    /// 从 ProjectileController 抽出，所有投射物复用。
    /// </summary>
    public class TrailModule
    {
        private TrailRenderer _trail;
        private Transform _trailAnchor;

        /// <summary>配置或创建拖尾</summary>
        public void Configure(Transform host, bool enable, Color color, float width, float time, Vector3 anchorOffset)
        {
            if (!enable)
            {
                Disable();
                return;
            }

            _trail = EnsureTrail(host);
            _trail.startColor = color;
            _trail.endColor = new Color(color.r, color.g, color.b, 0f);

            // 中粗两端细的"剑气"宽度曲线：0 → peak(25%) → 0
            _trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.25f, 1f),
                new Keyframe(1f, 0f)
            );
            _trail.widthMultiplier = width;
            _trail.time = time;
            _trail.enabled = true;

            if (_trailAnchor != null)
                _trailAnchor.localPosition = anchorOffset;
        }

        /// <summary>飞行开始时清空旧拖尾</summary>
        public void Clear()
        {
            if (_trail != null) _trail.Clear();
        }

        /// <summary>禁用拖尾</summary>
        public void Disable()
        {
            if (_trail != null)
                _trail.enabled = false;
        }

        private TrailRenderer EnsureTrail(Transform host)
        {
            const string childName = "TrailAnchor";
            _trailAnchor = host.Find(childName);
            if (_trailAnchor == null)
            {
                var childGo = new GameObject(childName);
                childGo.transform.SetParent(host, false);
                _trailAnchor = childGo.transform;
            }

            var trail = _trailAnchor.GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = _trailAnchor.gameObject.AddComponent<TrailRenderer>();

                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    // Additive 混合：让拖尾发光而非半透明覆盖
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    trail.material = mat;
                }

                trail.sortingOrder = 19;
            }
            return trail;
        }
    }
}
