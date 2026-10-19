# Project file format (`.fwp`), schema version 1

A Formwerk project is a ZIP container. Version 1 contains `document.json` (UTF-8, indented, `\n` line endings) and, for imported objects, one `meshes/<id>.bin` per mesh that a `mesh` node refers to. Implemented in `Formwerk.IO.ProjectFile`; the pinned test `Version_1_file_still_loads` in `tests/Formwerk.Tests.IO/ProjectFileTests.cs` holds an example that must keep loading.

Reserved for later (spec section 6): `thumbnail.png` for the file explorer and the library (needs off-screen rendering). Old versions of Formwerk ignore the `meshes/` parts, so adding them did not need a new schema version.

## `document.json`

```json
{
  "schemaVersion": 1,
  "application": "Formwerk",
  "settings": { "buildVolume": [256, 256, 256], "nozzleDiameter": 0.4, "tolerance": 0.2 },
  "variables": [ { "name": "Wandstaerke", "expression": "2" } ],
  "nodes": [ { "...": "node, see below" } ]
}
```

- `schemaVersion` is required. Files with a higher number are refused with a message to update; files with a lower number are migrated (`ProjectMigrations`). Unknown extra properties are ignored (forward compatibility).
- `variables` is an **array** because order matters: a variable may use earlier ones.
- `nodes` are the children of the scene root, in order.

## Node

| Property | Meaning |
| --- | --- |
| `type` | `primitive`, `mesh`, `group`, `boolean`, `merge` (unknown types are refused) |
| `id` | GUID, unique in the file |
| `name` | text |
| `role` | `solid` or `hole` |
| `color` | `#AARRGGBB` (`#RRGGBB` accepted) |
| `visible`, `locked` | booleans |
| `position` | `[x, y, z]` mm |
| `rotation` | quaternion `[x, y, z, w]` (normalised on load) |
| `scale` | `[x, y, z]` |
| `generator`, `parameters` | `primitive` only: shape id and parameter name to expression text (keys sorted) |
| `operation` | `boolean` only: `Union`, `Difference`, `Intersection` |
| `meshId` | `mesh` only: GUID of the stored mesh |
| `children` | `group`, `boolean`, `merge` only |

Geometry is never stored when it can be computed; only parameters and operations are.

## Meshes (`meshes/<id>.bin`)

`<id>` is the `meshId` of the node, written as a lower-case GUID with hyphens. The part is a little-endian binary file:

| Bytes | Content |
| --- | --- |
| 0-3 | the ASCII characters `FWM1` |
| 4-7 | vertex count (int32) |
| 8-11 | index count (int32, a multiple of 3) |
| 12 ... | vertices, three float32 each |
| ... | triangle indices, int32 each (counter-clockwise seen from outside) |

Only meshes that a node uses are written (in GUID order, a shared mesh once). Reading refuses a file whose mesh is missing, has the wrong size, a non-finite number or an index outside the vertex list; there is no silent data loss. Meshes are never changed after import; a copy of an object shares the mesh.

## Guarantees

- **Deterministic:** the same document always gives the same bytes (fixed ZIP timestamps, sorted parameter keys, shortest round-trip number format). Save, load, save gives an identical file.
- **Atomic:** written to `<name>.fwp.tmp`, flushed, then renamed over the target. A failed save leaves the old file untouched and removes the temp file (N-ROB-02).
- **Untrusted input (N-SEC-01/02):** limits on file size (256 MB), uncompressed size per part (64 MB for `document.json`, 400 MB for a mesh, 1 GB in total; also enforced while reading because declared sizes can lie), compression ratio, number of ZIP entries (4096), nodes (100 000), nesting depth (64), variables (1000), parameters per node (64) and text length (2000). ZIP entry names with `..`, a drive letter or a leading slash are refused (zip-slip), although nothing is ever extracted to disk. Numbers must be finite and in range. Every failure is a `ProjectFormatException` with a message for the user; a fuzz test corrupts valid files 800 times and checks that nothing else is ever thrown.

## Autosave

`AutosaveStore` keeps one copy, `%LOCALAPPDATA%\Formwerk\autosave\autosave.fwp` plus `autosave.json` (time and original path), written atomically every 2 minutes while there are unsaved changes. It is deleted on save, on a deliberate discard or exit, and when undo returns to the saved state. If it exists at the next start, the program was not closed normally and offers recovery.

## Changing the format

1. Increase `ProjectFile.CurrentSchemaVersion`.
2. Add a migration step in `ProjectMigrations.ToCurrent`.
3. Keep the old pinned file as a test fixture and add one for the new version.
4. Update this document.
