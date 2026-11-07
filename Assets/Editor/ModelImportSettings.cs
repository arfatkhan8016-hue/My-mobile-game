using UnityEditor;

/// <summary>
/// Put in Assets/Editor/. Import settings applied automatically when the cloud build imports the models:
/// - Weapons: meshes readable (GameBootstrap detects the barrel direction) and no material import.
/// - Characters / Enemies: no material import and no animation import. Their textures are supplied as PNGs
///   from code, so the big embedded FBX textures are skipped (faster build, smaller APK).
/// - Cars / Trees / Sky: no animation import.
/// </summary>
public class ModelImportSettings : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        ModelImporter importer = (ModelImporter)assetImporter;

        if (assetPath.Contains("/Resources/Weapons/"))
        {
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
        else if (assetPath.Contains("/Resources/Characters/") || assetPath.Contains("/Resources/Enemies/"))
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
        }
        else if (assetPath.Contains("/Resources/Cars/") || assetPath.Contains("/Resources/Trees/") || assetPath.Contains("/Resources/Sky/"))
        {
            importer.importAnimation = false;
        }
    }
}
