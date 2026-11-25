# Project rules

## General (same in every project)
- The development environment is the devcontainer in .devcontainer/.
- Build, test and publish ONLY via make: `make build`, `make test`, `make publish`.
  Never run toolchain commands directly on the host.
- Do not install toolchains on the host. If a tool is missing, extend the Dockerfile or devcontainer.json.
- After every code change run `make build` and `make test` and fix errors before reporting done.
- Commit with a meaningful message; do not push unless asked.

## This project
- Purpose: Windows desktop 3D modeller for 3D printing (C# / .NET 10, WPF).
- Structure: `src/` (Formwerk.Core, Geometry, Kernel, Features, Repair, IO, Library, Viewport, App),
  `tests/` (one xUnit project per src project), `testdata/`, `docs/` (spec, ADRs), `scripts/build.ps1` (Windows build).
- Target platforms: Windows x64 only (self-contained single folder, `make publish` -> ~/dist/formwerk-3d-modelling/).
- The app is WPF (`net10.0-windows`): it builds on Linux via `-p:EnableWindowsTargeting=true` (set in the Makefile),
  but it cannot run on the server. `make dev` does not start the app.
- `make test` runs only the `net10.0` test projects. `Formwerk.Tests.App` and `Formwerk.Tests.Viewport`
  need WPF at runtime and must be run on Windows (`scripts/build.ps1`).
- Project-specific workflow rules (currentstep.md, spec) are in CLAUDE.md.
