# 🧬 Helix Editor — Handy Keyboard Cheatsheet

> **Core Philosophy:** Selection $\to$ Action.  
> Press `Esc` at any time to return to **Normal Mode** (block cursor `█`) before issuing commands.

---

## 1. Entering Edit Mode (Insert Mode)

| Key | Action | Details |
| :--- | :--- | :--- |
| `i` | **Insert before** | Start editing at cursor |
| `a` | **Append after** | Start editing after cursor |
| `o` | **Open line below** | Insert new line below and edit |
| `O` | **Open line above** | Insert new line above and edit |
| `c` | **Change** | Delete selection and enter edit mode |
| `I` | **Insert at start** | Insert at first non-blank character of line |
| `A` | **Append at end** | Insert at end of line |
| `Esc` | **Exit edit mode** | Returns to Normal Mode |

---

## 2. Undo & Redo (macOS Equivalents)

| Key | Action | macOS Shortcut |
| :--- | :--- | :--- |
| `u` | **Undo** | `Cmd + Z` |
| `U` *(Shift+u)* | **Redo** | `Cmd + Shift + Z` |
| `Alt + u` | **Undo tree earlier** | Step back in revision history |
| `Alt + U` | **Undo tree later** | Step forward in revision history |

---

## 3. Fast Navigation (Jump by Word, Line, File)

### Word Movement (like `Option + Arrows`)
| Key | Action | Notes |
| :--- | :--- | :--- |
| `w` | **Next word start** | `Option + Right` equivalent |
| `b` | **Previous word start** | `Option + Left` equivalent |
| `e` | **Next word end** | Jumps to the end of word |
| `W` / `B` | **Next / Prev BIG word** | Treats symbols as part of word |

### Line & File Movement (like `Cmd + Arrows`)
| Key | Action | macOS Shortcut |
| :--- | :--- | :--- |
| `gs` | **Go to start of line** | `Cmd + Left` (first non-space) |
| `gh` | **Go home** (column 0) | Hard start of line |
| `gl` | **Go to end of line** | `Cmd + Right` |
| `gg` | **Go to top of file** | `Cmd + Up` (line 1) |
| `ge` | **Go to end of file** | `Cmd + Down` (last line) |
| `Ctrl + d` | **Page down** | Scroll half-page down |
| `Ctrl + u` | **Page up** | Scroll half-page up |

---

## 4. Moving / Reordering Lines

### Built-in Modal Way (Select $\to$ Cut $\to$ Paste)
1. `x` $\to$ Select current line (repeat `x` to select multiple lines)
2. `d` $\to$ Cut line(s)
3. `j` or `k` $\to$ Move up/down to target location
4. `p` (paste below) or `P` (paste above)

### Configured Direct Keys (if added to `config.toml`)
- `Alt + Down` or `Alt + j` $\to$ Slide line down
- `Alt + Up` or `Alt + k` $\to$ Slide line up

---

## 5. Unity & C# Code Tracing (LSP)

| Key | Action | Purpose |
| :--- | :--- | :--- |
| `gr` | **Goto References** | Interactive fuzzy popup of **all usages** across project + live code preview |
| `gd` | **Goto Definition** | Jump to where function / class / variable is defined |
| `gi` | **Goto Implementation** | Jump to interface implementation or override |
| `gy` | **Goto Type Definition**| Jump to definition of type |
| `Ctrl + o` | **Jump backward** | Return back to previous jump point (retrace call chain) |
| `Ctrl + i` | **Jump forward** | Advance forward in jump history |
| `Space` + `s` | **Document symbols** | Fuzzy search functions / variables in current file |
| `Space` + `S` | **Workspace symbols** | Fuzzy search symbols across whole Unity solution |
| `Space` + `d` | **Show diagnostics** | View errors / warnings in current file |
| `Space` + `D` | **Workspace diagnostics**| View all errors / warnings across all project files |
| `Space` + `k` | **Hover documentation**| View docs / tooltip of symbol under cursor |
| `Space` + `r` | **Rename symbol** | Rename symbol across whole project |

---

## 6. Project & File Navigation

| Key | Action | Description |
| :--- | :--- | :--- |
| `Space` + `f` | **File Picker** | Fuzzy find any file in project |
| `Space` + `/` | **Global Search** | Grep text across entire project |
| `Space` + `b` | **Buffer Picker** | Switch between opened files |
| `Space` + `w` | **Window / Splits** | Window management (splits, focus) |
| `Ctrl + v` | **Open in vertical split** | Inside file picker or `gr` |
| `Ctrl + s` | **Open in horizontal split** | Inside file picker or `gr` |

---

## 7. Essential Commands

Type `:` in Normal Mode to run commands:

- `:w` $\to$ Save file
- `:q` $\to$ Quit current buffer / window
- `:q!` $\to$ Quit without saving changes
- `:wq` or `:x` $\to$ Save and quit
- `:reload` $\to$ Reload current file from disk
- `:tutor` $\to$ Open Helix interactive tutorial
