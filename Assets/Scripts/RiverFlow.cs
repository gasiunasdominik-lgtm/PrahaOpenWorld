using UnityEngine;

// The Vltava flows north through Prague at about 0.5 m/s: two ripple layers drift with it at different speeds and
// angles, so the surface never looks like a repeating tile. Water UV: 1 unit = 10 m (MapBuilder: water.Ground).
public class RiverFlow : MonoBehaviour
{
    static readonly int Detail = Shader.PropertyToID("_DetailAlbedoMap");
    Material m;

    void Start() { var r = GetComponent<Renderer>(); if (r) m = r.material; } // own instance: never edit the asset in play

    void Update()
    {
        if (m == null) return;
        float t = Time.time;
        m.mainTextureOffset = new Vector2(0.004f * t, 0.05f * t);
        m.SetTextureOffset(Detail, new Vector2(-0.03f * t, 0.11f * t));
    }
}
