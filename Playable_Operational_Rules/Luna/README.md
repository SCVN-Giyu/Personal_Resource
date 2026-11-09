# Luna

[Back to root](../README.md)

## Runtime compatibility rules

- Target C# 7.0 for playable runtime scripts. Do not use later language features such as default literals, readonly/ref structs, switch expressions, records, ranges, target-typed `new`, nullable reference types, or newer pattern syntax.
- Use compatible explicit constructors and `default(T)` instead of newer shorthand.
- Use only APIs supported by the target Luna version. For uncertain behavior, inspect actual Luna documentation, SDK declarations, or runtime source before relying on it.
- Unity or .NET availability, successful Editor compilation, and successful SDK declaration lookup are not proof of exported runtime parity.
- Avoid relying on reflection, threads, allocation-heavy LINQ queries, advanced physics joints, or custom compute shaders. Prefer supported direct calls/registrations, simple loops, coroutines, or frame-based timers as appropriate.
- Do not read `Collider.sharedMaterial` or depend on `PhysicMaterial` APIs in playable runtime code. Store required numeric physics values in Luna-safe configuration.
- Do not patch generated exports to fix behavior; fix original source/configuration.

## Size and runtime budget

- Check the actual destination network's current package-size and delivery requirements; do not assume a universal megabyte limit.
- Use appropriate texture compression, remove unused mesh/asset data, and avoid large dynamic allocations.
- For pooling, follow [Unity pooling](../Unity/Pooling.md).

## Specific findings

Read [Learned limitations](Learned_Limitations.md) before changing Luna runtime behavior. It separates historical compiler failures, user-confirmed fixes, static findings, and unresolved risks.
