# Alchemy: Input Validation & Notifications

> Guard against misconfigurations, empty references, and invalid ranges directly inside the Inspector.

---

## 1. Custom Validation: `[ValidateInput]`

Run a C# method to validate the value of a field. If validation fails, Alchemy displays an error/warning box in the Inspector.

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class NetworkConfig : MonoBehaviour
{
    [ValidateInput(nameof(ValidatePort), "Port must be between 1024 and 65535!")]
    [SerializeField] private int _port = 7777;

    private bool ValidatePort(int port)
    {
        return port >= 1024 && port <= 65535;
    }
}
```

---

## 2. Required References: `[Required]`

Displays an inline error/warning if a serialized object reference is null or missing.

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class Turret : MonoBehaviour
{
    [Required("A target Transform is mandatory for this turret to operate!")]
    [SerializeField] private Transform _target;
}
```

---

## 3. Informational Boxes: `[HelpBox]`

Attach contextual documentation, hints, or warnings directly to fields.

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class AudioEmitter : MonoBehaviour
{
    [HelpBox("Ensure the audio clip is compressed as Vorbis for mobile builds.", HelpBoxMessageType.Info)]
    [SerializeField] private AudioClip _bgmClip;
}
```
*(MessageType options: `None`, `Info`, `Warning`, `Error`)*
