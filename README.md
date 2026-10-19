# Formwerk

Formwerk (working title) is a Windows desktop 3D modeller for 3D printing. It is made for beginners: build a model from basic
shapes, edit it with handles or exact numbers, and export it for your slicer. It sits above Microsoft 3D Builder and below
Fusion 360, with the ease of Tinkercad but offline and on your own machine.

> Status: **phase 1 of the specification is complete.** See [Documentation.md](Documentation.md), section 9, for the status of every
> requirement.

## What you can do

- **Shapes:** box, cylinder, sphere, hemisphere, cone, frustum, pyramid, prism, wedge, torus, tube, capsule, all with editable parameters
  (typed numbers, expressions such as `20/2+3`, relative values such as `+10` and `150%`).
- **Edit directly in 3D:** move, rotate and scale handles, dimensions you can click and type into, rectangle selection, snapping to a
  grid, angles, other objects and their corners, edge middles, face centers and centers (with markers), "keep on floor".
- **Combine:** groups, merges and booleans; any object can be a hole that cuts what it is grouped with (shown as a hatched ghost);
  open a group with a double click to edit inside it. Everything stays editable until you flatten it.
- **Work like in a real program:** unlimited undo/redo with a visible history, copy/paste/duplicate, save and open projects, autosave
  with crash recovery, templates ("Save as Template", "My Templates"), a ViewCube, perspective and orthographic view, dark theme
  (default) and light theme.
- **Import and export:** STL, OBJ and 3MF in (with unit questions and a mesh check with one-click repair); 3MF, STL and OBJ out,
  "Open in Slicer".

All values are in millimetres and degrees. The decimal separator is the dot; the comma only separates function arguments.

## Run it

Windows 10 or 11, 64 bit. No installation and no .NET runtime needed: build once (below), then copy the **`binary`** folder to any
computer and start `binary\Formwerk.exe`.

## Build it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`) and PowerShell.

```powershell
.\scripts\build.ps1              # build, run all tests, publish into .\binary
.\scripts\build.ps1 -SkipTests   # build and publish only
```

The script stops at the first failing step. Close Formwerk before building: the `binary` folder is replaced by every build.

## Project layout

| Folder | Content |
| --- | --- |
| `src/Formwerk.Core` | Document model (node tree), commands with undo/redo, expressions. Knows neither UI nor geometry engine. |
| `src/Formwerk.Geometry` | Mesh type and the geometry kernel interface. |
| `src/Formwerk.Kernel` | The boolean engine ([ManifoldSharp](https://www.nuget.org/packages/ManifoldSharp), Apache-2.0). |
| `src/Formwerk.Features` | Parametric shapes and the evaluation of the node tree into meshes. |
| `src/Formwerk.Repair` | Mesh analysis and repair. |
| `src/Formwerk.IO` | Project, template, STL, OBJ and 3MF files, settings, autosave. |
| `src/Formwerk.Library` | Library of shapes. |
| `src/Formwerk.Viewport` | The 3D view (WPF 3D): camera, handles, snapping markers, ViewCube. |
| `src/Formwerk.App` | The WPF application (MVVM with CommunityToolkit.Mvvm). |
| `tests/` | One test project per source project (xUnit). |
| `docs/` | Specification (German), architecture decisions (`adr/`), project file format, open questions. |
| `scripts/` | `build.ps1`. |

## Documentation

- [Documentation.md](Documentation.md) - how the code works, module by module, with the status of every requirement.
- [docs/spec.md](docs/spec.md) - the specification (German; the program itself is English only).
- [docs/adr](docs/adr) - decisions: geometry kernel, UI framework, no GPU renderer for now.
- [docs/open-questions.md](docs/open-questions.md) - questions that came up while building.
- [CLAUDE.md](CLAUDE.md) - the working rules for the AI assistant that helps to build the project.
- [currentstep.md](currentstep.md) - what is being worked on, and the state of the project, so that work can be resumed.

## Working rules in short

Every feature starts with a failing test, every document change is a command with `Do`/`Undo`, all UI texts are English and live in
one resource file, new dependencies need an ADR and a compatible license (MIT, BSD, Apache, Boost, zlib), and every finished feature
is committed on its own.

## License

Not chosen yet. The dependencies are ManifoldSharp (Apache-2.0, with Clipper2 under the Boost license) and
CommunityToolkit.Mvvm (MIT).
