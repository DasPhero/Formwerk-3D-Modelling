# ADR 0005: no GPU renderer for now (decision D-03)

Status: accepted (2026-10-19), may be revisited

## Context
The spec (section 11, D-03) left open whether the 3D view gets a DirectX 11 renderer of its own (Vortice.Windows) or uses Helix Toolkit. The viewport so far runs on WPF's built-in 3D (ADR 0004). A benchmark with the real `ViewportControl` was run on an Intel Iris Xe laptop GPU (weak; the laptop was on 25% battery):

| Triangles | Camera moving | Scene rebuild plus frame (what each frame of a drag costs) | Memory |
| --- | --- | --- | --- |
| 20,000 | 55 fps | 34 ms | 0.4 GB |
| 100,000 | 54 fps | 95 ms | 0.4 GB |
| 300,000 | 42 fps | 260 ms | 0.65 GB |
| 600,000 | 27 fps | 445 ms | 1.0 GB |
| 1,000,000 | 24 fps | 575 ms | 1.4 GB |
| 2,000,000 | 9 fps | 1,130 ms | 2.4 GB |
| 100 objects / 84,000 triangles | 58 fps | 123 ms | 0.4 GB |
| 400 objects / 67,000 triangles | 53 fps | 121 ms | 0.4 GB |
| 100 objects / 476,000 triangles | 48 fps | 355 ms | 0.9 GB |

(Camera numbers are capped at about 55 to 58 fps by the way they were measured. Not measured: picking time on huge meshes, other GPUs.)

After the optimisations that follow from this decision (drag preview, shared vertices), same machine and benchmark:

| Triangles | Camera moving | Dragging with the preview | Scene rebuild plus frame | Memory |
| --- | --- | --- | --- | --- |
| 20,000 | 56 fps | 58 fps | 19 ms | 0.33 GB |
| 100,000 | 57 fps | 57 fps | 53 ms | 0.38 GB |
| 300,000 | 54 fps | 58 fps | 118 ms | 0.60 GB |
| 1,000,000 | 47 fps | 59 fps | 334 ms | 0.77 GB |
| 2,000,000 | 36 fps | 57 fps | 699 ms | 1.2 GB |

With these numbers the replacement target (20 fps orbiting at 1 million triangles, smooth dragging) is met with a large margin, and even 2 million triangles orbit at 36 fps on this weak GPU. The scene rebuild (still needed for objects inside groups and after a release) is 1.7 times faster.

## Decision
No GPU renderer for now. The viewport stays on WPF 3D; no new dependency. The interface of `Formwerk.Viewport` (viewport items in, pick requests and gizmo callbacks out) stays renderer independent, so a DirectX renderer can replace the module later without touching the rest of the application.

## Consequences
- Spec requirements N-PRF-01 (60 fps at 2 million triangles) and N-GPU-01, 02, 03, 04, 06 are postponed (marked in the spec). Replacement target: at least 20 fps while orbiting with 1 million triangles, dragging stays smooth for any mesh size, picking by CPU ray.
- Work that follows from it: while dragging, move the existing geometry with a transform instead of rebuilding the scene; share vertices on smooth surfaces to cut memory.
- Features that need no GPU stay possible: section view by cutting with the kernel, overhang coloring by vertex colors, measuring, ViewCube as a WPF overlay. SSAO (F-VIS-02, optional) is dropped. Hollowing (E-15) starts with the CPU method that the spec requires as fallback anyway.
- Revisit when users work with scans of several million triangles often enough that the limits hurt: then run the spike from D-03 (Vortice.Windows, D3DImage embedding) and write a new ADR with its own numbers.
