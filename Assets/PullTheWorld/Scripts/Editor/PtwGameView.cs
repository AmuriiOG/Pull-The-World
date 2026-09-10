using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// Registers a fixed 1080x1920 portrait size in the Game view dropdown.
    ///
    /// Worth doing because "the captures look better than the editor" usually is not a lighting
    /// problem at all: the Game view defaults to Free Aspect, which renders at whatever the docked
    /// window happens to be (often ~400px wide and the wrong shape). At that size MSAA and SMAA
    /// have almost nothing to work with and every edge crawls. The automated captures render a
    /// true 1080x1920 frame, so they look sharper for reasons that have nothing to do with the art.
    ///
    /// Uses reflection because Unity never exposed GameViewSizes publicly. Fully guarded - if the
    /// internals move in a future Unity version the build simply carries on without the preset.
    /// </summary>
    public static class PtwGameView
    {
        const string SizeName = "PTW Portrait 1080x1920";

        [MenuItem("Pull The World/Add 1080x1920 Game View Size", priority = 40)]
        public static void AddPortraitSize()
        {
            if (TryAdd()) Debug.Log($"PTW: added Game view size '{SizeName}'. Pick it in the Game view dropdown.");
        }

        public static bool TryAdd()
        {
            try
            {
                var editorAsm = typeof(UnityEditor.Editor).Assembly;
                var sizesType = editorAsm.GetType("UnityEditor.GameViewSizes");
                var sizeType = editorAsm.GetType("UnityEditor.GameViewSize");
                var sizeTypeEnum = editorAsm.GetType("UnityEditor.GameViewSizeType");
                if (sizesType == null || sizeType == null || sizeTypeEnum == null) return false;

                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var instance = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                                        ?.GetValue(null);
                if (instance == null) return false;

                var group = sizesType.GetProperty("currentGroup", BindingFlags.Public | BindingFlags.Instance)
                                     ?.GetValue(instance);
                if (group == null) return false;

                var groupType = group.GetType();

                // Skip if it is already registered.
                var total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
                var getSize = groupType.GetMethod("GetGameViewSize", new[] { typeof(int) });
                var baseTextProp = sizeType.GetProperty("baseText");
                for (int i = 0; i < total; i++)
                {
                    var existing = getSize.Invoke(group, new object[] { i });
                    if (existing != null && (string)baseTextProp.GetValue(existing) == SizeName) return false;
                }

                var ctor = sizeType.GetConstructor(new[] { sizeTypeEnum, typeof(int), typeof(int), typeof(string) });
                if (ctor == null) return false;
                var fixedRes = Enum.Parse(sizeTypeEnum, "FixedResolution");
                var newSize = ctor.Invoke(new[] { fixedRes, (object)1080, 1920, SizeName });

                groupType.GetMethod("AddCustomSize").Invoke(group, new[] { newSize });
                sizesType.GetMethod("SaveToHDD")?.Invoke(instance, null);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("PTW: could not register the Game view size - " + e.Message);
                return false;
            }
        }
    }
}
