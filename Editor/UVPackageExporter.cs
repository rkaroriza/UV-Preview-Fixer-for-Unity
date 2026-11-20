#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UVTool.Editor
{
    public static class UVPackageExporter
    {
        public const string DefaultPackageFileName = "UVPreviewAndFixer_v1.0.0.unitypackage";

        public static string GetPackageRootPath()
        {
            // Dynamically locate the package root folder based on this script's location
            string[] guids = AssetDatabase.FindAssets("t:Script UVPackageExporter");
            if (guids != null && guids.Length > 0)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                string editorDir = Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(editorDir))
                {
                    string rootDir = Path.GetDirectoryName(editorDir)?.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(rootDir) && AssetDatabase.IsValidFolder(rootDir))
                    {
                        return rootDir;
                    }
                }
            }

            // Fallback checks
            if (AssetDatabase.IsValidFolder("Assets/UVPreviewAndFixer"))
                return "Assets/UVPreviewAndFixer";
            if (AssetDatabase.IsValidFolder("Assets/Plugins/UVPreviewAndFixer"))
                return "Assets/Plugins/UVPreviewAndFixer";

            return "Assets/UVPreviewAndFixer";
        }

        [MenuItem("Tools/UV Map Preview & Fixer/Export Unity Package...", false, 260)]
        public static void ExportPackageInteractive()
        {
            string rootPath = GetPackageRootPath();
            if (!AssetDatabase.IsValidFolder(rootPath))
            {
                EditorUtility.DisplayDialog("Error", $"Cannot find package root folder at '{rootPath}'.", "OK");
                return;
            }

            string savePath = EditorUtility.SaveFilePanel(
                "Export UV Preview & Fixer Unity Package",
                "",
                DefaultPackageFileName,
                "unitypackage"
            );

            if (string.IsNullOrEmpty(savePath)) return;

            ExportPackage(rootPath, savePath, true);
        }

        [MenuItem("Assets/Export UV Preview & Fixer Package...", false, 30)]
        public static void ExportFromContextMenu()
        {
            ExportPackageInteractive();
        }

        [MenuItem("Assets/Export UV Preview & Fixer Package...", true)]
        private static bool ValidateExportFromContextMenu()
        {
            if (Selection.activeObject == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(assetPath) && assetPath.Contains("UVPreviewAndFixer");
        }

        public static bool ExportPackage(string rootPath, string destinationPath, bool interactive = false)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Exporting Package", "Collecting assets...", 0.3f);

                AssetDatabase.ExportPackage(
                    rootPath,
                    destinationPath,
                    ExportPackageOptions.Recurse | (interactive ? ExportPackageOptions.Interactive : ExportPackageOptions.Default)
                );

                EditorUtility.ClearProgressBar();

                if (File.Exists(destinationPath))
                {
                    long bytes = new FileInfo(destinationPath).Length;
                    string sizeStr = $"{bytes / 1024f:F1} KB";
                    Debug.Log($"<color=green><b>[UV Preview & Fixer]</b> Successfully exported package to: {destinationPath} ({sizeStr})</color>");

                    if (interactive)
                    {
                        EditorUtility.DisplayDialog("Export Successful", $"Package exported successfully!\n\nLocation: {destinationPath}\nSize: {sizeStr}", "OK");
                        EditorUtility.RevealInFinder(destinationPath);
                    }
                    return true;
                }
                else
                {
                    Debug.LogError($"[UV Preview & Fixer] Package export finished but file was not found at '{destinationPath}'.");
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[UV Preview & Fixer] Failed to export package: {ex.Message}");
                if (interactive)
                {
                    EditorUtility.DisplayDialog("Export Failed", ex.Message, "OK");
                }
                return false;
            }
        }
    }
}
#endif
