# How it works

## Finding layers

Dataverse exposes solution layers through the virtual table **`msdyn_componentlayer`**. It returns one row per layer of a component, ordered from bottom to top. A query must filter on both:

- `msdyn_componentid`: the component's id
- `msdyn_solutioncomponentname`: the component type name (e.g. `Entity`, `SystemForm`, `Workflow`)

Because the table cannot be queried across components, the tool checks the selected components one by one.

A component has an **unmanaged layer** when one of its layers belongs to the `Active` solution. For that layer the tool reads:

| Column | Used for |
|---|---|
| `msdyn_overwritetime` | When the layer was last written (blank when Dataverse did not record a time) |
| `msdyn_changes` | Which attributes the layer changes |

## Component types

`solutioncomponent.componenttype` codes are mapped to layer names in `ComponentTypes.cs`. Newer component types (connection references, AI models, …) have **org-specific codes**; the tool resolves them at runtime from the `solutioncomponentdefinition` table. If a layer name returns no rows, the next candidate name is tried and the working one is cached.

## Who changed it

There is no "created by" on a layer, so the tool uses the best available source:

1. **Component record**: `modifiedby` / `modifiedon` for components that are records (forms, views, processes, web resources, environment variables, apps, …). This is the *last* modifier of the record, which is usually but not always the author of the unmanaged change.
2. **Audit log** (optional): latest audit entry for the component. Only available if auditing was enabled when the change was made.

Metadata components (tables, columns, relationships) have no `modifiedby`; use the audit option for those.

## Known limitations

- One query per component, so checking hundreds of components takes a while. Use the component type filter to narrow the check.
- Turning a flow on/off or changing its connections updates `modifiedby` without necessarily creating an unmanaged layer.
- An **Environment Variable Value** usually has an unmanaged layer by design: values set at import or by a pipeline are stored unmanaged. The maker portal's "Solution layers" page for a variable shows the Definition, not the Value.
