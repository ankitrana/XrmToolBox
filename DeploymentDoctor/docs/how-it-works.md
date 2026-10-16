# How Deployment Doctor works

Solution components keep the same id in every environment, so the tool reads the same component in Target and Dev and compares them.
The generic checks below run for every type; the table further down lists what each type adds. (The check table shows the form names; the others use the same logic with their own table and layer name.)

| Type | Table | Layer name | Compared | Extra checks |
|---|---|---|---|---|
| Form | `systemform` | `SystemForm` | `formxml` (tabs, sections, fields, order, scripts, handlers, access) | form health, same-name forms, active, apps, security roles, form order |
| View | `savedquery` | `SavedQuery` | `fetchxml` + `layoutxml` (columns, widths, order, filters, sort, joins) | active, default view, apps (public views) |
| Web resource | `webresource` | `WebResource` | `content` (changed lines for text types, size/hash for images) | used by (RetrieveDependentComponents), script used by no form |
| Process | `workflow` (type 1) | `Workflow` | cloud flows: `clientdata` triggers/steps/connection refs; others: `xaml` lines | on/off (+ Turn on fix), connection references have connections, duplicate copies by name, running instances note |
| Environment variable | `environmentvariabledefinition` | `EnvironmentVariableDefinition` | type, display name, default value (the current value is shown, not compared: it differs per environment by design) | no value at all in Target; state "No value" in the compare |
| Connection reference | `connectionreference` | `ConnectionReference` | connector, display name, logical name (the connection id differs per environment by design) | no connection in Target. No fixed component type number: solution membership is matched by id |
| Plug-in step | `sdkmessageprocessingstep` | `SdkMessageProcessingStep` | message, stage, mode, rank, plug-in class and assembly version, filtering columns, unsecure configuration (hash only) | disabled in Target (Fix: enable, with confirmation); assembly version differs |
| Plug-in assembly | `pluginassembly` | `PluginAssembly` | version, isolation mode, the DLL itself (size and hash) | version differs |
| Site map | `sitemap` | `SiteMap` | `sitemapxml` (areas, groups, sub areas: title, table, URL, dashboard, icon, privilege, order) | Site map health (damaged XML, duplicate ids, missing tables and web resources), which apps use it. Publish = `PublishAllXml` |
| Ribbon | `ribboncustomization` | `RibbonCustomization` | `ribbondiffxml` (buttons added/hidden, commands and the scripts they run, enable/display rules, labels) | Ribbon health (damaged XML, duplicate ids, missing scripts, undefined commands and rules). No remove-layer fix: layer behaviour is not verified |

Processes are not published, so the publish checks are skipped for them. Business rules count as included when their table is in the solution with all subcomponents; other processes must be added directly.

## Checks, in the order they run

| # | Check | Source | Problem when |
|---|---|---|---|
| 1 | Form exists in Target | `systemform` | not found |
| 2 | Published in Dev | `RetrieveUnpublished` vs `Retrieve` on `systemform` | unpublished XML differs from published |
| 3 | Form in Dev solution | `solutioncomponent` (type 60, or table type 1 with `rootcomponentbehavior` = 0) | not in the solution |
| 4 | Form in Target solution | same as 3, in Target | not in the solution |
| 5 | Solution version | `solution.version` | Dev is newer (warning) |
| 6 | Your solution's layer | `msdyn_componentlayer` (`SystemForm`) | your solution has no layer |
| 7 | Other managed solutions above yours | layer order (`msdyn_order`) | any managed layer above yours (warning) |
| 8 | Pending upgrade | layer named `<solution>_Upgrade` | exists (warning) |
| 9 | Unmanaged layer in Target | layer `Active` | exists |
| 10 | Package content | `formxml` inside `msdyn_componentjson` of your layer vs Dev | differs |
| 11 | Target vs Dev | published `formxml` | differs |
| 12 | Removed items still in Target | diff lines "Only in Target" without an Active layer | any (warning) |
| 13 | Unpublished changes in Target | `RetrieveUnpublished` in Target | differs (warning) |
| 14 | Form is active | `formactivationstate` | 0 |
| 15 | Form in apps | `appmodulecomponent` (type 60) + `appmodule` | an app picks forms for this table but not this one |
| 16 | Form access | `DisplayConditions` in `formxml` + `role` | restricted to roles (warning) |
| 17 | Form order (main forms) | `DisplayConditions/@Order` of active main forms open to everyone | another form comes first (warning) |

Findings are sorted Problem > Warning > OK > Info, keeping the order above within each group. The first Problem becomes the verdict.

The health row also lists **what was checked**: for each environment one line per test (readable XML, unique ids, unique names, fields, script libraries, event handlers, subgrid views, quick view forms, custom controls) with a tick when it passed, a cross or warning when it found something, and a dash when the form has nothing of that kind. The same list is in the copied text and in the Text and HTML reports.

The unmanaged-layer finding shows the layer's last-written time (`msdyn_overwritetime`, converted to local time) and compares it with the last-written time of your managed solution's layer (older = the unmanaged change predates the deployment, newer = made after it). The component's own `modifiedon` is not used because imports change it. Dataverse has no 'first added' time for a layer.

## Form XML comparison

Raw XML diffs are noisy (attribute order, ids, formatting). The tool compares what a maker recognises:
tabs, sections, fields (section, label, visibility, disabled), field order per section, header/footer fields,
script libraries, event handlers, form access and form order. Raw XML is still shown in the XML tabs.

## Forms merge

Model-driven forms use merge behaviour: each managed layer only overrides the parts of the form it changes.
So "another solution above yours" or an unmanaged layer only wins for the parts it touched. The Differences
tab tells you which parts are actually different.

## Fixes

| Fix | Where | API |
|---|---|---|
| Remove unmanaged layer | Target | `RemoveActiveCustomizations` (`SolutionComponentName` = `SystemForm`) + `PublishXml` for the table |
| Publish in Dev | Dev | `PublishXml` for the table |
| Add to Dev solution | Dev | `AddSolutionComponent` (type 60 / 26 / 61 / 29, no required components) |
| Turn on in Target (processes) | Target | `SetState` Activated (state 1, status 2) |
| Enable plug-in step | Target | `SetState` Enabled (state 0, status 1) on `sdkmessageprocessingstep` |

Publishing a web resource publishes only that web resource; forms and views publish their table.
`RemoveActiveCustomizations` is not supported for every component type; if Dataverse refuses, the error is shown.

Every fix first opens a "Before you continue" window: the environment, the user the change runs as (the account of
that XrmToolBox connection, read with `WhoAmI`; application users are flagged), what it does, whether it can be
undone, what to check first, and the manual maker-portal steps (copyable). "Make the change" only enables after
ticking "I have read this". Dataverse checks the account's permissions; if it refuses, nothing changes.
The diagnosis runs again afterwards.

## Compare whole solution
`SolutionCompare.Run` reads the solution in Dev (found by unique name), then for each component type: lists the components of the solution (`IdsInSolution`, direct or through a table added with all subcomponents), loads each one in Dev and Target by id, and compares published content with the same `Compare` the single-item doctor uses. Status per component:

| Status | Meaning |
|---|---|
| Same | content and state match |
| Different (red) | published content differs |
| Missing in Target (red) | the id does not exist in Target |
| State differs (orange) | content same, state differs (`State()` of the handler: form active, view active, flow on/off) |
| Not published in Dev (orange) | Dev has unpublished changes (not in the export) |
| Only in Target (orange) | in the Target copy of the solution, not in Dev (removed in Dev; 'Update' imports never delete) |
| Could not check | a read failed; the rest continues |

Rows that are not Same also check Target for an unmanaged layer (a hint). Component types the tool does not compare yet (tables, columns, plugin steps ...) are counted from the Dev solution and listed in the header.

## Reports and privacy

Reports contain configuration metadata only: no record data and no credentials.

| Included | HTML | Text | CSV |
|---|---|---|---|
| Verdict, next step, action plan, all checks | yes | yes | checks |
| Solution layers, differences | yes | yes | no |
| Connection names | yes | yes | yes |
| Environment URLs | only if ticked | no | no |
| Full form XML | only if ticked | no | no |

Form XML is off by default because control settings (iframe URLs, web resource parameters, PCF settings) can hold
sensitive values. The HTML is self-contained (no scripts, no external requests) and every value is HTML-encoded.

## Form health ("is the form really corrupted?")

Run for forms in Target and, when Dev is connected, in Dev. It only reads.

| Level | What it checks | Meaning |
|---|---|---|
| Damaged XML | XML can be parsed; root is `<form>`; tab, section and cell ids are unique | Really corrupted. Repair in Dev (restore a good copy, fix the XML, or rebuild). |
| Broken references | `datafieldname` columns exist on the table; script libraries exist as web resources; subgrid `ViewId`s and `QuickFormId`s exist; custom (PCF) controls are installed | NOT corrupted, but broken. Add the missing items to the solution. |
| Structure warnings | Repeated tab / section / control names; handlers whose library is not added to the form | Works, but scripts can misbehave. |
| Healthy | None of the above | Not corrupted: the real cause is elsewhere (layers, publish, solution, order...). |

Platform controls (`MscrmControls.*`, `Microsoft.*`) are not checked for installation. When the form id is missing in Target, forms with the same name, table and type but another id are reported (a recreated form).