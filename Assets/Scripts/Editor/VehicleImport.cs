using UnityEditor;

// Vehicle FBX from Tools/make_vehicles.py: readable (parked rows merge their low mesh at runtime), metres, no animation.
class VehicleImport : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.Contains("Resources/Vehicles")) return;
        var m = (ModelImporter)assetImporter;
        m.isReadable = true; m.importAnimation = false; m.animationType = ModelImporterAnimationType.None;
        m.globalScale = 1f; m.useFileScale = true; m.importCameras = false; m.importLights = false;
        m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
    }
}
