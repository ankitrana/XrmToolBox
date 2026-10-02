# Unmanaged Layer Finder (XrmToolBox tool)

Created by **Ankit Rana**.

Find unmanaged (Active) layers on the components of a Dataverse / Dynamics 365 solution, and see when and by whom they were changed.

## Features
- Filter solutions by type (managed / unmanaged / both), by publisher, and exclude Microsoft solutions
- Load a solution's components and filter them by component type (Form, View, Cloud Flow, Environment Variable, ...)
- For each component: layer stack, unmanaged layer time, changed attributes
- Who changed it: `modifiedby` / `modifiedon` from the component record, and optionally the audit log
- Shows current environment variable values
- Export results to CSV

## Notes
- Layer data comes from the `msdyn_componentlayer` table.
- Metadata components (tables, columns) have no `modifiedby`; use the audit option, which only works if auditing was enabled when the change was made.
- The tool is read-only. It never changes your environment.
