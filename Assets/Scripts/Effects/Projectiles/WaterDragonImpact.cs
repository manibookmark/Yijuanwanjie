using System.Collections;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>Two short jade water ripples at an authority-recorded contact;
    /// this transient component owns their expansion and destruction.</summary>
    public sealed class WaterDragonImpact : MonoBehaviour
    {
        private static Material _material;
        public static GameObject Play(Transform parent, Vector3 position)
        {
            var root = new GameObject("WaterDragonHitRipples");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var effect = root.AddComponent<WaterDragonImpact>();
            effect.StartCoroutine(effect.Animate());
            return root;
        }
        private IEnumerator Animate()
        {
            if (_material == null) _material = new Material(Shader.Find("Sprites/Default"))
                { hideFlags = HideFlags.HideAndDontSave, mainTexture = Texture2D.whiteTexture };
            var rings = new LineRenderer[2];
            for (int i = 0; i < rings.Length; i++)
            {
                var child = new GameObject("WaterCircle"); child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.sharedMaterial = _material; line.useWorldSpace = false;
                line.positionCount = 32; line.loop = true; line.sortingOrder = 35;
                line.startWidth = line.endWidth = 0.025f - i * 0.005f;
                for (int j = 0; j < 32; j++)
                {
                    float angle = j * Mathf.PI * 2f / 32f;
                    line.SetPosition(j, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 0.65f, 0f));
                }
                rings[i] = line;
            }
            float elapsed = 0f;
            while (elapsed < 0.26f)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / 0.26f);
                for (int i = 0; i < rings.Length; i++)
                {
                    float delayed = Mathf.Clamp01(t - i * 0.13f);
                    rings[i].transform.localScale = Vector3.one * Mathf.Lerp(0.08f, 0.62f, delayed);
                    var color = new Color(0.57f, 1f, 0.88f, (1f - t) * 0.85f);
                    rings[i].startColor = rings[i].endColor = color;
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
