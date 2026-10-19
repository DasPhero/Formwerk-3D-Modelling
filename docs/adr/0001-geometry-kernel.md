# ADR 0001: Geometry kernel

Status: accepted (spec decision D-01)

## Decision
Manifold (Apache 2.0) is the kernel for all mesh booleans, behind `IGeometryKernel`. Fillets/chamfers of stage 1 and 2 run on Manifold. OpenCascade is added in P4 as an optional module (separate DLLs, LGPL obligations per N-LIC-04) for F-EDG-03.

## Consequences
- `IGeometryKernel` must not leak Manifold types.
- Native interop lives in its own project with `SafeHandle` wrappers and leak tests.
