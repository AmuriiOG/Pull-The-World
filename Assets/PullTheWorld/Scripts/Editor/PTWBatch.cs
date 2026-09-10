using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    public static class PTWBatch
    {
        public static void Ping()
        {
            Debug.Log("PTW_BATCH_OK unity=" + Application.unityVersion + " srp=" +
                      (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null
                          ? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline.name : "builtin"));
            EditorApplication.Exit(0);
        }
    }
}
