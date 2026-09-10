using System.IO;
using UnityEditor;

namespace PullTheWorld.EditorTools
{
    public static class PtwPaths
    {
        /// <summary>Create an Assets-relative folder chain if any part of it is missing.</summary>
        public static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            string[] parts = assetPath.Replace('\\', '/').Split('/');
            string current = parts[0];              // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static void DeleteFolderContents(string assetPath, string extension)
        {
            if (!AssetDatabase.IsValidFolder(assetPath)) return;
            string abs = Path.GetFullPath(assetPath);
            foreach (var file in Directory.GetFiles(abs, "*" + extension))
            {
                string rel = "Assets" + Path.GetFullPath(file).Substring(
                    Path.GetFullPath("Assets").Length).Replace('\\', '/');
                AssetDatabase.DeleteAsset(rel);
            }
        }
    }
}
