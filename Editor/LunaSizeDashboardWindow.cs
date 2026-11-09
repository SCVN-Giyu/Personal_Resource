using UnityEditor;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Replaced by the Overview tab of the Luna Optimizer window. Kept so the old menu item and saved window layouts
    // still work: opening it opens the hub on that tab instead.
    public class LunaSizeDashboardWindow : EditorWindow
    {
        [MenuItem("Supercent/Luna Size Dashboard")]
        static void Open()
        {
            LunaOptimizerWindow.OpenTab(LunaOptimizerWindow.Tab.Overview);
        }

        void OnEnable()
        {
            EditorApplication.delayCall += () =>
            {
                LunaOptimizerWindow.OpenTab(LunaOptimizerWindow.Tab.Overview);
                if (this != null)
                    Close();
            };
        }
    }
}
