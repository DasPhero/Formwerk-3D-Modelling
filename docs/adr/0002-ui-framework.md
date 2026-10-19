# ADR 0002: UI framework

Status: accepted (spec decision D-02)

## Decision
WPF (Windows only), Fluent theme, dark by default. The 3D viewport is embedded via `D3DImage`, not `HwndHost`.

## Consequences
Portability rule: only `Formwerk.App`, `Formwerk.Viewport` and `*.Windows` adapter projects may use Windows APIs. All other projects target plain `net10.0`; CI additionally builds them on Linux.
