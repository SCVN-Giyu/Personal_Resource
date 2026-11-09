using System;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // One tab of the Luna Optimizer window. The hub serializes the view (so [SerializeField] state such as column
    // layouts survives reloads), enables it the first time its tab is shown and draws it below the tab bar.
    [Serializable]
    public abstract class OptimizerView
    {
        [NonSerialized] EditorWindow host;
        [NonSerialized] Action repaint;
        [NonSerialized] bool enabled;

        public abstract string Title { get; }

        // Unsaved changes shown as a badge on the tab and saved / discarded from the hub header.
        public virtual int PendingCount
        {
            get { return 0; }
        }

        public virtual void SavePending()
        {
        }

        public virtual void DiscardPending()
        {
        }

        public bool IsEnabled
        {
            get { return enabled; }
        }

        // repaint: how the host redraws this view (a UI Toolkit host marks its IMGUIContainer dirty).
        public void Attach(EditorWindow window, Action repaintView = null)
        {
            host = window;
            repaint = repaintView;
            if (enabled)
                return;
            enabled = true;
            OnEnable();
        }

        public void Detach()
        {
            if (!enabled)
                return;
            enabled = false;
            OnDisable();
        }

        public virtual void OnEnable()
        {
        }

        public virtual void OnDisable()
        {
        }

        // A UI Toolkit tab returns its root element here; an IMGUI tab returns null and draws in OnGUI.
        public virtual UnityEngine.UIElements.VisualElement CreateUI()
        {
            return null;
        }

        public virtual void OnGUI()
        {
        }

        protected void Repaint()
        {
            if (repaint != null)
                repaint();
            else if (host != null)
                host.Repaint();
        }

        protected void ShowNotification(GUIContent content)
        {
            if (host != null)
                host.ShowNotification(content);
        }
    }
}
