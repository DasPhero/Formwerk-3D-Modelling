# ADR 0003: ManifoldSharp as the first geometry kernel implementation

Status: accepted (implements ADR 0001 / spec D-01 for stages 1 and 2)

## Context
The spec suggests the Manifold C API through P/Invoke. No prebuilt, maintained native binding with a Windows binary exists on NuGet, and building `manifoldc` from source needs CMake and a C++ toolchain on every developer and CI machine. The NuGet package `ManifoldSharp` is a pure managed C# port of Manifold (ported from manifold-rust, bit-exact against it), with `MeshGL` import/export, cooperative cancellation, an optional parallel mode and a robust engine for triangle soup.

## Decision
Use `ManifoldSharp` 0.5.0 (pinned) inside the new project `Formwerk.Kernel`, behind `IGeometryKernel`. No ManifoldSharp type leaves that project.

## Dependency check (spec rules)
| Item | Result |
| --- | --- |
| License | ManifoldSharp: Apache-2.0. Its only dependency Clipper2 1.5.4: Boost (BSL-1.0). Both allowed. |
| Maintenance | First 2026 release line, actively developed. |
| Size | Single 465 kB managed assembly plus Clipper2. |
| Native code | None, so no `SafeHandle` / leak tests needed and the project stays portable (spec D-02 portability rule). |

## Consequences
- Pro: no native build step, works on every platform, debuggable, cancellable (`CancellationToken` is passed into the boolean).
- Con: young package with a single maintainer and few users. Mitigation: pinned version, adapter boundary, own tests for volumes/watertightness/failure handling (`Formwerk.Tests.Kernel`), and the option to add a native Manifold adapter later without touching callers.
- Measured on this machine (Release, warm): plate with 50 holes of 32 segments: union of holes ~200 ms + cut ~80 ms; two 65k-triangle spheres intersected: ~1 s. First call after start is ~3x slower (JIT). Parallel mode makes almost no difference at these sizes. This misses the 200 ms goal of N-PRF-02 for the worst case; see `docs/open-questions.md` Q-03 for the planned optimizations.
- Operands must be closed manifolds (`Error.NotClosed` otherwise). Imported meshes will go through repair first; the robust engine (`BooleanEngine.Auto`) is available if needed.
