# Phase 2: Code Navigation, Implementation & Compiler Verification

[Back to Root Tree](../README.md)

## 1. Zero-Hallucination API Lookup (Before Writing Unfamiliar Code)

- **MCP Tool:** `context7` (`resolve-library-id` → `get-library-docs`)
- **Skill Leaf:** [source-driven-development](../../Agent_Skills/gemini/source-driven-development/SKILL.md)
- **Auto-Trigger:** Whenever calling an unfamiliar third-party library, newer Unity package, Luna SDK surface, Alchemy attribute, or framework API.
- **Action:** Fetch the exact version-specific documentation or inspect the actual installed SDK/source declaration rather than guessing method signatures or overloads from model weights.

## 2. Semantic Navigation & Surgical Editing (Instead of Raw Text Grep)

- **For All Languages (C#, JS, TS, Python):**
  - **MCP Tool:** `serena` (`find_symbol`, `find_referencing_symbols`, `replace_symbol_body`, `insert_after_symbol`)
  - **Action:** Inspect callers before changing signatures; replace whole symbols cleanly instead of brittle line-number edits.
- **For C# / Unity `.sln` & `.csproj` Projects:**
  - **MCP Tool:** `roslyn-mcp` (`~/.dotnet/tools/roslyn-mcp`)
  - **Auto-Trigger:** When extracting interfaces/methods, implementing interfaces, checking cyclomatic complexity, finding dead code, or inspecting compiled assembly signatures.
  - **Action:** Let Microsoft's Roslyn compiler perform AST refactorings deterministically.
- **For Live Unity Editor Inspection:**
  - **MCP Tool:** `ai-game-developer` (when connected to the target project)
  - **Action:** Verify target project identity first; respect [Work Safety](../../Playable_Operational_Rules/General/Work_Safety.md) (never enter Play Mode, run tests, or save dirty scenes without explicit user authorization).

## 3. Deterministic Post-Edit Compiler Gate (Auto-Trigger: After Code Edits)

- **Action:** Before claiming an implementation is complete:
  1. Run `serena:get_diagnostics_for_file` (or `roslyn-mcp` solution diagnostics for C#) on modified files to verify **zero new syntax or type errors**.
  2. Cross-check against the active domain's avoid list ([unity.md](../../Agent_Skills/gemini/avoid-mistakes/references/unity.md) / [playable-luna.md](../../Agent_Skills/gemini/avoid-mistakes/references/playable-luna.md)).
  3. Distinguish static compiler verification from runtime verification in your report.
