using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    public enum LunaSettingsSection
    {
        Textures,
        Sounds,
        Modules,
        AssetExcludes,
        Scenes,
    }

    // The Luna settings section each tool save changed, as it was before and after. Reverting puts a section back
    // through Luna's loaded settings and is itself recorded, so it can be undone too.
    public static class LunaSaveHistory
    {
        const string HISTORY_PATH = "Library/LunaTextureOptimizer/save-history.json";
        const int MAX_RECORDS_PER_SECTION = 10;

        [Serializable]
        public class Record
        {
            public LunaSettingsSection section;
            public string savedAt;
            public string label;
            public string detail;
            public string before;
            public string after;

            public DateTime SavedAt
            {
                get { return DateTime.Parse(savedAt, null, System.Globalization.DateTimeStyles.RoundtripKind); }
            }
        }

        [Serializable]
        class Records
        {
            public List<Record> items = new List<Record>();
        }

        // Newest first.
        public static List<Record> For(LunaSettingsSection section, string projectRoot)
        {
            return Load(projectRoot).items.Where(r => r.section == section).OrderByDescending(r => r.savedAt).ToList();
        }

        public static void Add(string projectRoot, LunaSettingsSection section, string label, string detail, string before, string after)
        {
            var records = Load(projectRoot);
            records.items.Add(new Record
            {
                section = section,
                savedAt = DateTime.Now.ToString("o"),
                label = label,
                detail = detail,
                before = before,
                after = after,
            });
            var dropped = records.items.Where(r => r.section == section).OrderByDescending(r => r.savedAt).Skip(MAX_RECORDS_PER_SECTION).ToList();
            records.items.RemoveAll(dropped.Contains);

            var path = Path.Combine(projectRoot, HISTORY_PATH);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(records));
        }

        static Records Load(string projectRoot)
        {
            var path = Path.Combine(projectRoot, HISTORY_PATH);
            var records = File.Exists(path) ? JsonUtility.FromJson<Records>(File.ReadAllText(path)) : null;
            return records != null && records.items != null ? records : new Records();
        }

        // Toolbar dropdown: one item per save, newest first. Picking one puts the section back to before that save.
        public static void ShowRevertMenu(Rect buttonRect, LunaSettingsSection section, string projectRoot, Action reverted)
        {
            var records = For(section, projectRoot);
            var menu = new GenericMenu();
            if (records.Count == 0)
                menu.AddDisabledItem(new GUIContent("No saves recorded yet"));
            for (int i = 0; i < records.Count; i++)
            {
                var index = i;
                var record = records[i];
                menu.AddItem(new GUIContent("Undo " + record.SavedAt.ToString("MM-dd HH:mm:ss") + "  " + record.label.Replace("/", "∕")), false,
                    () => EditorApplication.delayCall += () => ConfirmRevert(records, index, projectRoot, reverted));
            }
            menu.DropDown(buttonRect);
        }

        // Every section's saves in one dropdown, one submenu per section.
        public static void ShowRevertMenuAll(Rect buttonRect, string projectRoot, Action reverted)
        {
            var menu = new GenericMenu();
            var any = false;
            foreach (LunaSettingsSection section in Enum.GetValues(typeof(LunaSettingsSection)))
            {
                var records = For(section, projectRoot);
                var title = char.ToUpper(SectionName(section)[0]) + SectionName(section).Substring(1) + "/";
                if (records.Count == 0)
                {
                    menu.AddDisabledItem(new GUIContent(title + "No saves recorded yet"));
                    continue;
                }
                any = true;
                for (int i = 0; i < records.Count; i++)
                {
                    var index = i;
                    var record = records[i];
                    menu.AddItem(new GUIContent(title + "Undo " + record.SavedAt.ToString("MM-dd HH:mm:ss") + "  " + record.label.Replace("/", "∕")), false,
                        () => EditorApplication.delayCall += () => ConfirmRevert(records, index, projectRoot, reverted));
                }
            }
            if (!any)
                menu.AddDisabledItem(new GUIContent("No saves recorded yet"));
            menu.DropDown(buttonRect);
        }

        // For the browser page: what reverting records[index] of a section does (null when it cannot be read), and the revert itself.
        public static string RevertMessage(LunaSettingsSection section, int index, string projectRoot, out string error)
        {
            var records = For(section, projectRoot);
            error = null;
            if (index < 0 || index >= records.Count)
            {
                error = "That save is no longer in the history.";
                return null;
            }
            return RevertMessage(records, index, projectRoot, out error);
        }

        public static bool Revert(LunaSettingsSection section, int index, string projectRoot, out string error)
        {
            var records = For(section, projectRoot);
            if (index < 0 || index >= records.Count)
            {
                error = "That save is no longer in the history.";
                return false;
            }
            var record = records[index];
            var label = "Undo of " + record.SavedAt.ToString("HH:mm:ss") + " " + record.label;
            return LunaSettingsWriter.RestoreSection(record.section, record.before, label, projectRoot, out error);
        }

        static string RevertMessage(List<Record> records, int index, string projectRoot, out string error)
        {
            var record = records[index];
            var message = "Put Luna's " + SectionName(record.section) + " back to how they were before this save?\n\n"
                + record.SavedAt.ToString("yyyy-MM-dd HH:mm:ss") + "  " + record.label
                + (string.IsNullOrEmpty(record.detail) ? string.Empty : "\n" + record.detail);
            if (index > 0)
                message += "\n\nThis also undoes the " + index + " later save(s) in this list.";
            string current;
            if (!LunaSettingsWriter.TryReadSection(record.section, projectRoot, out current, out error))
                return null;
            if (!LunaSettingsWriter.SameSection(record.section, current, records[0].after))
                message += "\n\n⚠ The " + SectionName(record.section) + " changed after the newest save in this list (Luna window or another tool). "
                    + "Undoing also discards those changes.";
            return message + "\n\nOther Luna settings are not touched. The undo is recorded, so it can be undone too.";
        }

        static void ConfirmRevert(List<Record> records, int index, string projectRoot, Action reverted)
        {
            var record = records[index];
            var title = "Undo " + SectionName(record.section) + " save";
            var message = "Put Luna's " + SectionName(record.section) + " back to how they were before this save?\n\n"
                + record.SavedAt.ToString("yyyy-MM-dd HH:mm:ss") + "  " + record.label
                + (string.IsNullOrEmpty(record.detail) ? string.Empty : "\n" + record.detail);
            if (index > 0)
                message += "\n\nThis also undoes the " + index + " later save(s) in this list.";

            string current;
            string error;
            if (!LunaSettingsWriter.TryReadSection(record.section, projectRoot, out current, out error))
            {
                EditorUtility.DisplayDialog(title, "Nothing was changed.\n\n" + error, "OK");
                return;
            }
            if (!LunaSettingsWriter.SameSection(record.section, current, records[0].after))
                message += "\n\n⚠ The " + SectionName(record.section) + " changed after the newest save in this list (Luna window or another tool). "
                    + "Undoing also discards those changes.";
            message += "\n\nOther Luna settings are not touched. The undo is recorded, so it can be undone too.";

            if (!EditorUtility.DisplayDialog(title, message, "Undo", "Cancel"))
                return;
            var label = "Undo of " + record.SavedAt.ToString("HH:mm:ss") + " " + record.label;
            if (!LunaSettingsWriter.RestoreSection(record.section, record.before, label, projectRoot, out error))
            {
                EditorUtility.DisplayDialog(title, "Nothing was changed.\n\n" + error, "OK");
                return;
            }
            if (reverted != null)
                reverted();
        }

        public static string SectionName(LunaSettingsSection section)
        {
            switch (section)
            {
                case LunaSettingsSection.Textures: return "texture settings";
                case LunaSettingsSection.Sounds: return "sound settings";
                case LunaSettingsSection.AssetExcludes: return "asset exclusions";
                case LunaSettingsSection.Scenes: return "build scene";
                default: return "engine module settings";
            }
        }

        // Short list of names for a record's detail line.
        public static string Names(IEnumerable<string> names)
        {
            const int SHOWN = 6;
            var list = names.ToList();
            return string.Join(", ", list.Take(SHOWN)) + (list.Count > SHOWN ? " … +" + (list.Count - SHOWN) + " more" : string.Empty);
        }
    }
}
