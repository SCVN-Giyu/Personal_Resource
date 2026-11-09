# Alchemy: Setup & Assembly Definition (.asmdef)

> Verification procedures and `.asmdef` setup when working with Alchemy in Unity.

---

## 1. Verifying Installation

Before writing Alchemy attributes, verify that the package is part of the project:

### Check 1: `Packages/manifest.json`
Look for the git URL or package identifier:
```json
"dependencies": {
  "com.annulusgames.alchemy": "https://github.com/annulusgames/Alchemy.git",
  ...
}
```

### Check 2: OpenUPM / `packages.lock.json`
Alternatively, check `Packages/packages.lock.json` for:
```json
"com.annulusgames.alchemy": {
  "version": "...",
  "depth": 0,
  "source": "..."
}
```

If neither is present, **do not use Alchemy attributes** unless the user requests installing it.

---

## 2. Assembly Definition (.asmdef) Configuration

If the target script is inside an assembly with an `.asmdef` file:

### Runtime Scripts (MonoBehaviour, ScriptableObject, Data types)
Add the runtime reference to the `.asmdef`:
```json
"references": [
    "Alchemy"
]
```

### Editor-Only Scripts (Drawers, Custom Windows, Processors)
Inside the `Editor/` folder assembly:
```json
"references": [
    "Alchemy",
    "Alchemy.Editor"
]
```

---

## 3. Namespace Usage

In runtime code:
```csharp
using Alchemy.Inspector;
```

In editor code:
```csharp
using Alchemy.Editor;
```
