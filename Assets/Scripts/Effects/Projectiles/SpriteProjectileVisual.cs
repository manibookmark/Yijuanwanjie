using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Sprite 图片模式的投射物外观。
    /// 通用模式，用 VfxProfile.projectileSprite 渲染。
    /// </summary>
    public class SpriteProjectileVisual : IProjectileVisual
    {
        private SpriteRenderer _renderer;
        private Sprite _sprite;
        private float _scale;

        // 静态注册
        static SpriteProjectileVisual()
        {
            ProjectileVisualFactory.Register("Sprite", () => new SpriteProjectileVisual());
        }

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            _renderer = host.GetComponent<SpriteRenderer>();
            _renderer.sortingOrder = 20;

            _sprite = profile != null && profile.projectileSprite != null
                ? profile.projectileSprite : null;
            _scale = scale;

            if (_sprite != null)
            {
                _renderer.sprite = _sprite;
                _renderer.color = Color.white;
                _renderer.flipX = false;
            }
            host.transform.localScale = Vector3.one * _scale;
        }

        public void OnFlightStart()
        {
            if (_renderer != null)
                _renderer.enabled = true;
        }

        public void OnFlightEnd()
        {
            if (_renderer != null)
                _renderer.enabled = false;
        }

        public Vector3 GetTrailAnchorOffset()
        {
            if (_sprite != null)
                return new Vector3(-_sprite.bounds.size.x * 0.45f * _scale, 0f, 0f);
            return new Vector3(-0.05f, 0f, 0f);
        }

        public void Cleanup() { }
    }
}
