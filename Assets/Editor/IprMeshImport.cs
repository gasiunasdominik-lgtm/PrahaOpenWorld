using UnityEditor;

// Photoreal mesh textures (Tools/fetch_ipr_mesh.py) arrive as 9613² JPGs at 5 cm/px: 4096 keeps ~12 cm/px,
// which is what the game camera resolves, at a fraction of the memory.
public class IprMeshImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("Resources/IprMesh/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.maxTextureSize = 4096;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.mipmapEnabled = true;
    }
}
