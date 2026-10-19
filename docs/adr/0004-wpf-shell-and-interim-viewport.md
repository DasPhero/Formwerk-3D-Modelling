# ADR 0004: WPF shell, MVVM toolkit and an interim 3D viewport

Status: accepted as an interim step (the renderer decision D-03 stays open)

## Context
The project needed a visible, usable application early. The DirectX 11 renderer with gizmos (spec D-03) needs a spike and visual iteration; the document model, kernel and evaluator were ready.

## Decisions
1. **MVVM:** `CommunityToolkit.Mvvm` 8.4.0 (MIT) in `Formwerk.App`. Its generator needs `<LangVersion>preview</LangVersion>` for `[ObservableProperty]` on partial properties; this is set for the App project only.
2. **Theme:** WPF's built-in Fluent theme via `ThemeMode="Dark"` on the application (experimental API, warning WPF0001 suppressed in the App project). Panel/text colours from spec section 7 are defined in `App.xaml`. The Fluent theme uses the Windows accent colour for selection; a fixed accent is still to do.
3. **Viewport:** `Formwerk.Viewport.ViewportControl` is implemented on WPF's own 3D (`Viewport3D`) as a stand-in. Its public surface (a list of `ViewportItem` in, `PickRequest` out, selection and build volume as properties) is renderer-independent, so the DirectX renderer can replace it without touching the App. Known limits: no edge lines (F-VIS-02), no GPU picking, mesh upload is per-triangle (about 3x memory), expected to be too slow beyond some hundred thousand triangles (N-PRF-01).
4. **Texts:** all UI strings live in `Resources/Strings.resx`; XAML uses `{res:Loc Key}`, code uses `Texts.Get`. Tests check that every referenced key exists.
5. **Viewport independence:** `Formwerk.Viewport` depends on Core and Geometry only. The App maps evaluator results (`ScenePart`) to `ViewportItem`.

## Consequences
- The app is usable now: add shapes, edit dimensions and positions by typing, group/hole/boolean, undo history, STL export.
- Moving objects by dragging (gizmos), snapping and the DirectX renderer are still open and will replace/extend the viewport.
