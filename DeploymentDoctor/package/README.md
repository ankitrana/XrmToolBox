# Deployment Doctor (XrmToolBox tool)

Created by **Ankit Rana**.

You changed a form, view, web resource, workflow or cloud flow in Dev, deployed the managed solution, and the change isn't showing in UAT or Prod. Deployment Doctor tells you why and helps you fix it.

Types: forms, views, web resources, classic workflows, business rules, actions, business process flows, cloud flows.

## What it checks (every type)
- Dev: unpublished changes (export takes the published version), and whether the component is in the solution
- Target: whether the component is in the solution, and the solution versions in Dev and Target
- Solution layers: unmanaged (Active) layer on top, other managed solutions above yours, staged upgrade not applied
- Content: Dev vs Target vs your solution's layer, in readable form

## Extra checks per type
- Forms: tabs, sections, fields, field order, scripts, event handlers; form active, in the apps, security roles, form order
- Views: columns, widths, filters, sort order, related tables; view active, default view, in the apps
- Web resources: changed lines (JS, HTML, CSS, XML), size for images; which forms use it, script used by no form
- Workflows and cloud flows: changed steps; turned on or off, connection references without a connection, old copies with the same name

## Fixes (each one explained first)
- Remove the unmanaged layer in Target
- Publish in Dev
- Add the component to the solution in Dev
- Turn a flow or workflow on in Target

Before any fix runs, a window shows which user the change runs as, what it does, whether it can be undone, what to check first, and the manual steps. Nothing changes until you tick "I have read this".

## Share
- Export the result as an HTML report (email / ticket), CSV (Excel) or text (Teams). Full content and environment URLs are left out unless you choose them.

## Notes
- Main connection = Target. Connect Dev as a second connection for the full diagnosis.
- The tool only reads data unless you click a Fix button and confirm.
