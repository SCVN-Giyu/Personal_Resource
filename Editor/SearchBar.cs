using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Search box for a tab's table: icon, placeholder, clear button, Esc clears, and "N of M" while filtering.
    // The tab focuses it on Ctrl/Cmd+F (RegisterShortcut).
    public class SearchBar : VisualElement
    {
        readonly TextField field;
        readonly Label placeholder;
        readonly Button clear;
        readonly Label count;

        public SearchBar(string value, string hint, Action<string> changed)
        {
            AddToClassList("lo-searchbar");
            var box = new VisualElement();
            box.AddToClassList("lo-search-box");
            var icon = OptimizerUI.Icon("d_Search Icon");
            if (icon != null)
            {
                var image = new Image { image = icon, pickingMode = PickingMode.Ignore };
                image.AddToClassList("lo-search-icon");
                box.Add(image);
            }
            field = new TextField { value = value ?? string.Empty };
            field.AddToClassList("lo-search-input");
            box.Add(field);
            placeholder = new Label(hint) { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("lo-search-placeholder");
            box.Add(placeholder);
            clear = new Button(() =>
            {
                field.value = string.Empty;
                field.Focus();
            }) { text = "×", tooltip = "Clear (Esc)" };
            clear.AddToClassList("lo-search-clear");
            box.Add(clear);
            Add(box);
            count = new Label();
            count.AddToClassList("lo-search-count");
            Add(count);

            field.RegisterValueChangedCallback(change =>
            {
                Refresh();
                changed(change.newValue ?? string.Empty);
            });
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape && field.value.Length > 0)
                {
                    field.value = string.Empty;
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            Refresh();
        }

        public void FocusInput()
        {
            field.Focus();
            field.SelectAll();
        }

        // "12 of 127" while a search is typed, nothing otherwise.
        public void SetCount(int shown, int total)
        {
            count.text = field.value.Length > 0 ? shown + " of " + total : string.Empty;
            count.EnableInClassList("lo-search-count--none", field.value.Length > 0 && shown == 0);
        }

        // Ctrl/Cmd+F anywhere in the tab focuses the search.
        public void RegisterShortcut(VisualElement tabRoot)
        {
            tabRoot.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.actionKey && evt.keyCode == KeyCode.F)
                {
                    FocusInput();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
        }

        void Refresh()
        {
            var empty = string.IsNullOrEmpty(field.value);
            placeholder.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            clear.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
