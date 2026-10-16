# Deployment Doctor (XrmToolBox tool)

Created by **Ankit Rana**.

You deployed a managed solution and a change isn't showing in UAT or Prod. Deployment Doctor tells you why and helps you fix it, for one component or for a whole solution at once.

## Two ways to use it
- **Check one item**: pick the solution, the component type and the component, click Diagnose. You get the most likely cause, what to do next, and every check behind it.
- **Compare whole solution**: pick the solution and compare every component between Dev and Target. Red = different or missing in Target, orange = state differs (for example a flow on in Dev but off in Target), not published in Dev, or only in Target, green = same. Click a row for the exact differences, then open that component in the doctor.

## What it can check
Forms, views, site maps, ribbons (command bars), classic workflows, business rules, actions, business process flows, cloud flows, environment variables, connection references, plug-in steps, plug-in assemblies and web resources.

## What it checks (every type)
- Dev: unpublished changes (export takes the published version), and whether the component is in the solution
- Target: whether the component is in the solution, and the solution versions in Dev and Target
- Solution layers: unmanaged (Active) layer on top, other managed solutions above yours, staged upgrade not applied
- **What the unmanaged layer changed**, when it was last changed (your local time) and whether that is older or newer than your last deployment
- Content: Dev vs Target vs your solution's layer, in readable form

## Extra checks per type
- Forms: tabs, sections, fields, field order, scripts, event handlers; form active, in the apps, security roles, form order; **form health** (really corrupted, or only broken references?) with a "what was checked" list; recreated forms with the same name
- Views: columns, widths, filters, sort order, related tables; view active, default view, in the apps
- Site maps and ribbons: what changed, **health check** (damaged XML, duplicate ids, missing tables, web resources and scripts), which apps use a site map
- Web resources: changed lines (JS, HTML, CSS, XML), size for images; which forms use it, script used by no form
- Workflows and cloud flows: changed steps; turned on or off, connection references without a connection, old copies with the same name
- Environment variables: default value, and whether Target has any value (current values differ per environment by design)
- Connection references: whether they have a connection in Target
- Plug-in steps and assemblies: enabled or disabled, stage, filtering columns, assembly version and DLL fingerprint

## Fixes (each one explained first)
- Remove the unmanaged layer in Target
- Publish in Dev
- Add the component to the solution in Dev
- Turn a flow or workflow on in Target
- Enable a plug-in step in Target

Before any fix runs, a window shows which user the change runs as, what it does, whether it can be undone, what to check first, and the manual steps. Nothing changes until you tick "I have read this".

## Share
- Export the result as an HTML report (email / ticket), CSV (Excel) or text (Teams). Full content and environment URLs are left out unless you choose them.

## Notes
- Main connection = Target. Connect Dev as a second connection for the full diagnosis (and for Compare whole solution).
- The tool only reads data unless you click a Fix button and confirm.
- Not compared yet: tables, columns, choices, security roles, model-driven apps and dashboards (Compare whole solution only counts them). It compares published content, and cannot tell when an unmanaged layer was first added (Dataverse keeps only the last change).
