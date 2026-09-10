using UnityEditor;

public class CharacterModelPostprocessor : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (!assetPath.Contains("Assets/Art/Characters")) return;

        var modelImporter = (ModelImporter)assetImporter;
        modelImporter.animationType = ModelImporterAnimationType.Human;
    }
}
