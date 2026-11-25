using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace WanjieScroll.EditorTools
{
    public static class PublicProjectSetup
    {
        private const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string FallbackFontPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        public static void RestoreEngineResources()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run this setup method in batch mode.");
            if (AssetDatabase.LoadMainAssetAtPath(FallbackFontPath) != null)
            {
                EditorApplication.Exit(0);
                return;
            }

            PackageInfo package = PackageInfo.FindForAssembly(typeof(TMPro.TMP_Settings).Assembly);
            if (package == null || package.name != "com.unity.textmeshpro")
                throw new InvalidOperationException("TextMesh Pro is missing from the package registry.");

            string archive = Path.Combine(package.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            byte[] settings = File.ReadAllBytes(SettingsPath);
            double deadline = EditorApplication.timeSinceStartup + 45;
            EditorApplication.update += CompleteWhenImported;
            AssetDatabase.ImportPackage(archive, interactive: false);

            void CompleteWhenImported()
            {
                bool imported = File.Exists(FallbackFontPath) && !EditorApplication.isUpdating;
                if (!imported && EditorApplication.timeSinceStartup < deadline)
                    return;
                EditorApplication.update -= CompleteWhenImported;
                try
                {
                    File.WriteAllBytes(SettingsPath, settings);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    if (AssetDatabase.LoadMainAssetAtPath(FallbackFontPath) == null)
                        throw new InvalidOperationException("TextMesh Pro Essential Resources did not import.");
                    Debug.Log("[PublicProjectSetup] Restored official TextMesh Pro Essentials.");
                    EditorApplication.Exit(0);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    EditorApplication.Exit(1);
                }
            }
        }
    }
}
