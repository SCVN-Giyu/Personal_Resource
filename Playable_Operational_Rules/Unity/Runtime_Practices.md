# Runtime Practices

[Parent: Unity](README.md)

- Do not call `GetComponent`, `GetComponentInChildren`, `GameObject.Find`, or `GameObject.FindWithTag` in `Update`, `LateUpdate`, `FixedUpdate`, or other frequent loops. Cache references during initialization or assign them in the Inspector.
- Do not use global scene searches such as `FindObjectOfType`, `FindObjectsOfType`, `FindFirstObjectByType`, or `FindAnyObjectByType` in playable runtime code. Use serialized references, explicit initialization wiring, or an existing documented singleton.
- Do not use the null-conditional operator (`?.`) on `UnityEngine.Object` types. Use an explicit `!= null` check so Unity's destroyed-object check is respected.
- Avoid `SendMessage`, `BroadcastMessage`, and `StartCoroutine("MethodName")`. Use strongly typed calls or `StartCoroutine(MethodName())`.
- Do not inject required gameplay components through `AddComponent` in map/stage-loading paths. Author them on the relevant prefab or scene object.
- Do not use Visual Scripting or node-graph-authored executable logic. Use explicit C#; configuration assets and C# inspector tools are allowed.
- For pooling, follow [Pooling](Pooling.md).
