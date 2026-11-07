using UnityEditor;

/// <summary>
/// Put in Assets/Editor/. Makes weapon meshes readable so GameBootstrap can detect which end of the gun is the barrel.
/// </summary>
public class ModelImportSettings : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (assetPath.Contains("/Resources/Weapons/"))
        {
            ModelImporter importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
        }
    }
}
