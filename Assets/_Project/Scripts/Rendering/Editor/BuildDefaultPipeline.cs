using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine.Rendering;

namespace Spellmite
{
    // Unity filters shader variants with the Graphics default pipeline asset too, not only with the quality-level assets
    // of the build target. With PC_RPAsset as the default, its "keep only" filters (Forward+, SSAO, additional light
    // shadows) stripped every Simple Lit / Lit / Unlit forward variant Mobile_RPAsset needs, so WebGL drew no level.
    // Unity reads the default before any build callback runs, so it is kept in sync with the active build target:
    // WebGL -> Mobile_RPAsset, Windows -> PC_RPAsset.
    public class BuildDefaultPipeline : IActiveBuildTargetChanged
    {
        public int callbackOrder => 0;

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) => Sync(newTarget);

        // Also after a pull, when GraphicsSettings.asset may come from a teammate on another platform
        [InitializeOnLoadMethod]
        private static void SyncOnLoad() => EditorApplication.delayCall += () => Sync(EditorUserBuildSettings.activeBuildTarget);

        private static void Sync(BuildTarget target)
        {
            var assets = new List<RenderPipelineAsset>();
            if (!target.TryGetRenderPipelineAssets(assets) || assets[0] == GraphicsSettings.defaultRenderPipeline)
                return;

            GraphicsSettings.defaultRenderPipeline = assets[0];
            AssetDatabase.SaveAssets();
        }
    }
}
