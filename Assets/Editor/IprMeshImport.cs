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

// Realistic surfaces (Tools/make_textures.py): *_n are OpenGL normal maps; everything tiles and gets mipmaps
public class SurfaceTexImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("Resources/Tex/")) return;
        var ti = (TextureImporter)assetImporter;
        if (System.IO.Path.GetFileNameWithoutExtension(assetPath).EndsWith("_n")) ti.textureType = TextureImporterType.NormalMap;
        ti.wrapMode = UnityEngine.TextureWrapMode.Repeat;
        ti.mipmapEnabled = true; ti.anisoLevel = 8;
        ti.maxTextureSize = 1024;
        ti.textureCompression = TextureImporterCompression.Compressed;
    }
}
