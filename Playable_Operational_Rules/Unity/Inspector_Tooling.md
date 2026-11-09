# Inspector Tooling

[Parent: Unity](README.md)

- Check whether `com.annulusgames.alchemy` is installed before adding inspector tooling.
- When present, prefer its declarative attributes for buttons, groups, validation, and conditional visibility rather than boilerplate custom inspectors.
- Check the installed package's actual source/samples for attribute names and behavior.
- If a custom editor is genuinely necessary, check the installed Alchemy editor integration so attribute support is preserved.
- Do not install or integrate a package merely because this guidance mentions it; stay within the user's authorized scope.

## Learned correction: foldout attribute

- **Observed:** In the previously inspected Alchemy revision `9288cbcc663b179f060fc195064256af344d4244`, `[Foldout("Name")]` caused `CS0246`.
- **Use:** `[FoldoutGroup("Name")]` for that revision.
- **Evidence:** Installed package sample `Samples~/Samples/Scripts/Groups/FoldoutGroupSample.cs`, recorded 2026-10-20.
- Recheck package APIs when using a different revision. A skill example is not stronger evidence than the installed source.
