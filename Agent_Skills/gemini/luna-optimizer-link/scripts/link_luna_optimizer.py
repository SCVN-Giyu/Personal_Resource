#!/usr/bin/env python3
"""Link (or unlink) the shared Luna Optimizer package into a Unity project.

Adds one dependency to <project>/Packages/manifest.json pointing at the shared, live package folder, so every
project uses the same source and gets every update. Nothing else in the project is changed.

Usage:
  python3 -I link_luna_optimizer.py [PROJECT_DIR] [--unlink] [--dry-run] [--tool PATH]
Exit codes: 0 ok / already linked, 1 not a usable Unity project or tool missing, 2 manifest problem.
"""
import argparse
import json
import os
import re
import sys
from collections import OrderedDict

PACKAGE = "com.supercent.luna-texture-optimizer"
DEFAULT_TOOL = "/Users/giyu/Documents/Personal_resources/Unity_Packages/" + PACKAGE
MIN_UNITY = (2022, 3)
VERIFIED_LUNA = "7.2.0"


def fail(code, message):
    print("ERROR: " + message)
    sys.exit(code)


def unity_version(project):
    path = os.path.join(project, "ProjectSettings", "ProjectVersion.txt")
    if not os.path.isfile(path):
        fail(1, "not a Unity project (no ProjectSettings/ProjectVersion.txt): " + project)
    with open(path, encoding="utf-8") as handle:
        match = re.search(r"m_EditorVersion:\s*(\d+)\.(\d+)\S*", handle.read())
    if not match:
        fail(1, "cannot read the Unity version from " + path)
    return (int(match.group(1)), int(match.group(2))), match.group(0).split(":", 1)[1].strip()


def find_luna(packages_dir, dependencies):
    """Luna is referenced as a local package inside its install folder (which holds config.json and pipeline/)."""
    for name, value in dependencies.items():
        if not isinstance(value, str) or not value.startswith("file:"):
            continue
        target = value[len("file:"):]
        target = target if os.path.isabs(target) else os.path.normpath(os.path.join(packages_dir, target))
        root = os.path.dirname(target.rstrip("/"))
        config = os.path.join(root, "config.json")
        if os.path.isfile(config) and os.path.isdir(os.path.join(root, "pipeline")):
            try:
                with open(config, encoding="utf-8") as handle:
                    version = json.load(handle).get("version", "?")
            except (OSError, ValueError):
                version = "?"
            return name, root, version
    return None


def with_dependency(dependencies, key, value):
    """Insert key in alphabetical position without reordering the existing entries (Unity sorts them the same way)."""
    result = OrderedDict()
    inserted = False
    for existing, existing_value in dependencies.items():
        if existing == key:
            continue
        if not inserted and existing > key:
            result[key] = value
            inserted = True
        result[existing] = existing_value
    if not inserted:
        result[key] = value
    return result


def main():
    parser = argparse.ArgumentParser(description="Link the shared Luna Optimizer package into a Unity project.")
    parser.add_argument("project", nargs="?", default=os.getcwd(), help="Unity project folder (default: current folder)")
    parser.add_argument("--unlink", action="store_true", help="remove the package from the project")
    parser.add_argument("--dry-run", action="store_true", help="show what would change, write nothing")
    parser.add_argument("--tool", default=DEFAULT_TOOL, help="shared package folder (default: %(default)s)")
    args = parser.parse_args()

    project = os.path.abspath(os.path.expanduser(args.project))
    (major, minor), version_text = unity_version(project)
    print("Unity project: " + project)
    print("Unity version: " + version_text)

    manifest_path = os.path.join(project, "Packages", "manifest.json")
    if not os.path.isfile(manifest_path):
        fail(2, "no Packages/manifest.json in " + project)
    with open(manifest_path, encoding="utf-8") as handle:
        try:
            manifest = json.load(handle, object_pairs_hook=OrderedDict)
        except ValueError as error:
            fail(2, "Packages/manifest.json is not valid JSON: " + str(error))
    dependencies = manifest.get("dependencies", OrderedDict())

    if args.unlink:
        if PACKAGE not in dependencies:
            print("Not linked; nothing to do.")
            return 0
        del dependencies[PACKAGE]
        action = "Unlinked"
    else:
        if (major, minor) < MIN_UNITY:
            fail(1, "Luna Optimizer needs Unity 2022.3 or newer (UI Toolkit tables); this project uses " + version_text)
        tool = os.path.abspath(os.path.expanduser(args.tool))
        package_json = os.path.join(tool, "package.json")
        if not os.path.isfile(package_json):
            fail(1, "shared package not found: " + package_json)
        with open(package_json, encoding="utf-8") as handle:
            if json.load(handle).get("name") != PACKAGE:
                fail(1, package_json + " is not " + PACKAGE)
        value = "file:" + tool
        if dependencies.get(PACKAGE) == value:
            print("Already linked: " + value)
            action = None
        else:
            previous = dependencies.get(PACKAGE)
            dependencies = with_dependency(dependencies, PACKAGE, value)
            action = ("Relinked (was " + previous + ")") if previous else "Linked"
            action += ": " + value

        luna = find_luna(os.path.dirname(manifest_path), dependencies)
        if luna is None:
            print("WARNING: no Luna install found in the manifest. The tool opens, but sizes, predictions and saving need Luna.")
        else:
            name, root, luna_version = luna
            print("Luna: " + name + " " + luna_version + " at " + root)
            if luna_version != VERIFIED_LUNA:
                print("WARNING: tool verified with Luna " + VERIFIED_LUNA + "; saving to Luna and Measure read Luna internals, check them on " + luna_version + ".")
        if action is None:
            print("Next: open Unity, menu Supercent > Luna Optimizer.")
            return 0

    manifest["dependencies"] = dependencies
    text = json.dumps(manifest, indent=2, ensure_ascii=False) + "\n"
    if args.dry_run:
        print("[dry run] " + action)
        return 0
    with open(manifest_path, "w", encoding="utf-8") as handle:
        handle.write(text)
    print(action)
    print("Next: switch to Unity (it re-resolves packages when focused), then menu Supercent > Luna Optimizer.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
