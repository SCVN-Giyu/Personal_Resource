## 1. Context Menu & Inspector Interaction in Play Mode
- **Symptom**: Clicking Inspector `[ContextMenu]` or resizing Editor windows during Play Mode causes gameplay to appear frozen and then "snap / teleport" instantly.
- **Root Cause**:
  - Native OS popup menus stall the Game View repaint/render loop.
  - Real wall-clock time continues ticking in the background.
  - When the menu closes, Unity executes a burst of accumulated `FixedUpdate` simulation steps in a single frame to catch up (`Time.maximumDeltaTime`).
  - The next rendered frame displays objects already 15–20 physics frames ahead.
- **Best Practice**:
  - Avoid relying on Inspector context menus for time-sensitive physics or kinematic gameplay testing.
  - Use in-game keybinds (e.g., `Input.GetKeyDown(KeyCode.Space)`) or on-screen buttons to trigger test actions cleanly within the active Game View.