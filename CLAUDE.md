# Formwerk

Windows desktop 3D modeller for 3D printing (C# / .NET 10, WPF). Single source of requirements: `docs/spec.md` (German; UI text is English only).

## Resume file: currentstep.md (mandatory)
Runs can be interrupted (token limit, crash). So:
1. **Before starting any step**, write it into `currentstep.md` under "Active step": goal, requirement IDs, a checklist plan, status `in progress`. Do this before writing any code.
2. While working, tick checklist items and note files touched, decisions and problems immediately, not at the end.
3. After every finished item run `.\scripts\build.ps1`.
4. When the step is finished, move it to "Done" (newest first), set Active step to `none`, refresh the "Current state" snapshot and the open questions.
5. At the start of every session read `currentstep.md` first and continue the active step where it stopped.

## Reporting format (mandatory)
For every step: before starting, a brief description of what will be done; afterwards, a brief description of what has been done. Each time first a **functional** overview (what the user can now do), then a **technical** view (types, files, decisions, caveats).

## Workflow per work package
1. One requirement ID (e.g. `F-LIB-01`) per package. Read it and the related spec sections.
2. Unclear points go to `docs/open-questions.md` instead of guessing.
3. Write the acceptance criterion as a failing test first, then implement.
4. No changes outside the work package.
5. Commit message starts with the ID: `F-HOL-02: countersink and counterbore`.
6. Short report: done, open, assumptions.

## Module rules (spec section 3)
- `Formwerk.Core` knows neither UI nor geometry kernel. Every document change is a command with `Do`/`Undo`.
- Dependencies: Geometry -> Core; Kernel -> Core, Geometry; Features -> Core, Geometry; Repair -> Geometry; IO -> Core, Geometry; Library -> Core, Features; Viewport -> Core, Geometry; App -> all. Features talks to the kernel only through `IGeometryKernel`; only `App` (composition root) and tests reference `Kernel`.
- All projects except `Formwerk.App` and `Formwerk.Viewport` target `net10.0` (no `-windows`) and use no Windows APIs. Windows adapters go in `*.Windows` projects.
- Geometry evaluation runs on a background thread with `CancellationToken`; UI shows last valid result.
- Native interop only in its own project, with `SafeHandle`.
- No network access in code (except optional update check). No `eval`; parameter expressions use the own parser.

## Code rules
- Nullable enabled, `TreatWarningsAsErrors`.
- MVVM with CommunityToolkit.Mvvm; no logic in code-behind.
- Units: millimetres, degrees.
- New dependencies need an ADR (license MIT/BSD/Apache/Boost/zlib; LGPL only as separate DLL; never GPL/AGPL/NC).
- All UI strings in the central resource file, English only. Dark theme by default.

## Build and test
```
.\scripts\build.ps1            # build + test + publish the self-contained program into .\binary
.\scripts\build.ps1 -SkipTests
```
Run `scripts\build.ps1` after every work step. The executable is `binary\Formwerk.exe`; the `binary` folder holds everything needed to run the program and can be copied to another computer as it is (generated, not in git).

## Documentation
`Documentation.md` explains the project, folders, modules and code. Update it in the same step as any change to projects, folders, public types, tests or requirement status (rules in its last section), then run the build script.

## Conventions
- Decimal separator in all numeric input is the dot (`0.5`); the comma only separates function arguments (`min(3,4)`).
- The project is under git (local repository, branch `master`). **After every finished feature (one requirement ID or one clearly named fix) commit everything**: only after the build script is green and `Documentation.md` and `currentstep.md` are updated. The message starts with the ID and a short summary (`F-HOL-02: countersink and counterbore`), a few lines say what and why, and it ends with the `Co-Authored-By` line. Never push, never rewrite history, never commit `binary/`, `bin/`, `obj/` (they are in `.gitignore`).

## Definition of done
Acceptance test green - undo/redo works - mouse and numeric input - dark theme + English resources - save/load keeps state - no new warnings.
