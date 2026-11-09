# Alchemy Reference Index

> Detailed reference guides for Alchemy attributes, layout grouping, drawers, and project setup.
> AI agents should only open the document matching the task at hand.

---

## 📚 Documents

1. [`setup-and-asmdef.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/setup-and-asmdef.md)
   - How to check if Alchemy is installed in `Packages/manifest.json` or `packages.lock.json`.
   - Configuring `.asmdef` references (`Alchemy.Editor`, `Alchemy`).
2. [`attributes-buttons.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/attributes-buttons.md)
   - `[Button]` attribute on methods.
   - `[ButtonGroup]` for horizontal row buttons.
   - Button sizing and custom labels.
3. [`attributes-logic.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/attributes-logic.md)
   - Conditional display: `[ShowIf]`, `[HideIf]`.
   - Conditional interactability: `[EnableIf]`, `[DisableIf]`.
   - Member condition matching (methods, booleans, enums).
4. [`attributes-layout.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/attributes-layout.md)
   - Organization: `[Foldout]`, `[TabGroup]`, `[HorizontalGroup]`, `[VerticalGroup]`.
   - Nesting and group naming rules.
5. [`attributes-validation.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/attributes-validation.md)
   - `[ValidateInput]` for custom inline checks.
   - `[Required]` for null reference guards.
   - `[HelpBox]` for informational and warning callouts.
6. [`attributes-decorations.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/attributes-decorations.md)
   - Visual polishing: `[Title]`, `[LabelText]`, `[Indent]`, `[Blockquote]`, `[HorizontalLine]`.
7. [`custom-drawers.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/custom-drawers.md)
   - Inspecting non-serialized C# properties with `[ShowInInspector]`.
   - Writing custom Alchemy drawers / `AlchemyEditor` overrides when attributes alone are not sufficient.
