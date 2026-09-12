using UnityEngine;

// Primitive cubes map every face to UV 0..1, so a shared textured material would stretch one tile
// across a 56m wall and a 1m crate alike. This sets the renderer's _BaseMap_ST per object from its
// world-space size (via a MaterialPropertyBlock, so the shared .mat asset stays untouched):
// flat things (slabs, floors) tile by X/Z, upright things (walls) by their long edge and height.
[ExecuteAlways]
public class AutoTile : MonoBehaviour
{
    [SerializeField] private float metersPerTile = 2f;
    // Plane primitives are 10 units at scale 1 (cubes are 1) — set to 10 for a Plane.
    [SerializeField] private float unitScale = 1f;

    private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

    private void OnEnable() => Apply();

    private void Apply()
    {
        Renderer r = GetComponent<Renderer>();
        if (r == null) return;

        Vector3 s = transform.lossyScale * unitScale;
        float u, v;
        if (s.y <= s.x && s.y <= s.z) { u = s.x; v = s.z; }
        else { u = Mathf.Max(s.x, s.z); v = s.y; }

        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetVector(BaseMapST, new Vector4(u / metersPerTile, v / metersPerTile, 0f, 0f));
        r.SetPropertyBlock(block);
    }
}
