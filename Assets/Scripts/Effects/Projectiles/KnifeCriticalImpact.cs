using System.Collections;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>Owns the gold-white critical fragments and contact pulse.
    /// Callers must supply an actual critical receipt; no chance is rolled here.</summary>
    public sealed class KnifeCriticalImpact : MonoBehaviour
    {
        private static Material _material;
        public static GameObject Play(Transform parent, Vector3 position)
        {
            var root = new GameObject("KnifeCriticalReceipt");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var effect = root.AddComponent<KnifeCriticalImpact>();
            effect.StartCoroutine(effect.Animate());
            return root;
        }
        private IEnumerator Animate()
        {
            if (_material == null) _material = new Material(Shader.Find("Sprites/Default"))
                { hideFlags = HideFlags.HideAndDontSave, mainTexture = Texture2D.whiteTexture };
            var shards = new LineRenderer[10];
            for (int i = 0; i < shards.Length; i++)
            {
                var child = new GameObject("CriticalShard"); child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.sharedMaterial = _material; line.useWorldSpace = false;
                line.positionCount = 2; line.sortingOrder = 38;
                line.startWidth = 0.026f; line.endWidth = 0.004f;
                shards[i] = line;
            }
            float elapsed = 0f;
            while (elapsed < 0.20f)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / 0.2f);
                for (int i = 0; i < shards.Length; i++)
                {
                    float angle = i * Mathf.PI * 2f / shards.Length;
                    Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 0.72f, 0f);
                    float radius = Mathf.Lerp(0.025f, 0.62f, t);
                    shards[i].SetPosition(0, direction * radius);
                    shards[i].SetPosition(1, direction * (radius + 0.15f * (1f - t)));
                    Color c = Color.Lerp(new Color(1f, 0.99f, 0.85f), new Color(1f, 0.70f, 0.19f), t);
                    c.a = 1f - t;
                    shards[i].startColor = shards[i].endColor = c;
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
