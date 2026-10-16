# Changelog

All notable changes to Deployment Doctor. Versions follow [Semantic Versioning](https://semver.org/).

## [1.5.2] - 2026-10-16
### Added
- **When was the unmanaged layer changed?** The "Unmanaged layer in Target" finding now says "unmanaged layer last changed <date and time>" in your computer's local time (layer times were shown in UTC before, without saying so). The details compare it with when your managed solution layer was last written (deployed) and say whether the unmanaged change is OLDER (already there, the deployment did not remove it) or NEWER (made after the deployment). The component's own modified date is deliberately not used: it changes on every import. Dataverse keeps only the time of the last change of a layer, not when it was first added; the start screen says so.
## [1.5.1] - 2026-10-16
### Changed
- The **beta** labels are gone (Ribbon, Environment variable, Connection reference, Plug-in step, Plug-in assembly), the start screen, About and docs no longer say beta, and the Ribbon no longer shows its always-on Beta note. What remains true is still listed under *What it does not do*: the unmanaged layer of a ribbon, environment variable, connection reference or plug-in can't be removed from here (the manual steps are shown).
## [1.5.0] - 2026-10-16
### Added (all four marked beta: not yet verified on a real environment)
- **Environment variable**: compares type and default value; shows the current value of each environment (different values are normal and are not flagged); a variable with no default and no current value in Target is a problem. Secrets are never read.
- **Connection reference**: compares connector and names; flags a reference that has no connection in Target (flows using it can't run). The connection itself is chosen per environment, so it is not compared. It has no fixed solution component number, so its membership in a solution is matched by id.
- **Plug-in step**: compares message, stage, mode, execution order, plug-in class, assembly version, filtering columns and (as a hash) the unsecure configuration; flags a step that is disabled in Target and offers a confirmed **Enable step in Target** fix.
- **Plug-in assembly**: compares version, isolation mode and the DLL itself (size and hash).
- All four work in **Check one item** and in **Compare whole solution**; in the compare, a state such as "No value" or "No connection" in Target is shown in orange even when Dev is the same.
- New category **Settings and connections** in the Component type dropdown.
### Changed
- The headline ("Most likely cause") now prefers a real cause over the "Target vs Dev" symptom row, which only says that the other findings explain why.
- Start screen, Compare text and About list the new types and no longer say plug-in steps and environment variables are not compared.
## [1.4.1] - 2026-10-16
### Changed
- The Compare start text and the About box also say what is not compared yet.
- The start screen now also lists **what it does not do yet** (no automatic changes, types not compared yet, published content only, Ribbon beta, Dev needed for comparing).
- The start screen of "Check one item" now also explains **Compare whole solution** (what the colours mean and how to open a component from the result).
## [1.4.0] - 2026-10-16
### Added
- **Compare whole solution** (toolbar button next to "Check one item"). Pick a solution, connect Dev, click **Compare with Dev**. Every form, view, site map, ribbon, process / flow and web resource of the solution in Dev is found in Target and compared with the same comparers as the single-item doctor:
  - **red**: different content, or missing in Target;
  - **orange**: content is the same but the state differs (flow or process on in Dev and off in Target, form or view inactive), Dev has changes that were never published, or the component is still in the solution in Target but no longer in Dev (an 'Update' import never deletes);
  - **green**: same.
  Click a row for the full list of differences (and a hint when Target has an unmanaged layer on it). **Diagnose this component** opens the normal doctor for that row. A tick box hides the components that match. The header shows the counts, the solution versions in Dev and Target, and which component types are not compared yet (tables, columns, plugin steps, environment variables ...).
- Export of the compare result as HTML, CSV or text (environment names only: no URLs and no component content).
- If one component type cannot be read (permissions, older environment), it is shown as "Could not check" and the rest of the compare continues.
### Notes
- Read-only. Tables, columns, plugin steps, environment variables, security roles and similar types are not compared yet.
- Only the published content is compared, like the single-item doctor.
## [1.3.2] - 2026-10-16
### Added
- **What the unmanaged layer changed.** The "Unmanaged layer in Target" finding now lists the parts changed (as reported by Dataverse) and what differs between the unmanaged layer and the managed layer right below it, for example "Column 'name': width 200 -> 300" or "Sort order: name asc -> name desc". When Dataverse does not store the content of the layer, it lists the Dev vs Target differences instead (they include the effect of the unmanaged change), or says how to see it in the maker portal. Also in the copied text and the reports.
## [1.3.1] - 2026-10-16
### Changed
- **"What was checked"** under every health row (Form, Site map, Ribbon): one line per test and environment with a tick, cross, warning or dash, for example "Fields: 47 fields on the form exist as columns of table account in Target" or "Subgrid views: none on the form". It also appears in the copied text and in the Text and HTML reports, so you can show a developer exactly what was looked at when you say the form is NOT corrupted.
- **Start screen** now explains what the tool can do: the component types it checks (with what is looked at for each), what you get back, and how to start.
## [1.3.0] - 2026-10-16
### Added
- **Site map** (the left navigation of a model-driven app) as a component type: Dev vs Target compare of areas, groups and sub areas (title, table, URL, dashboard, icon, privilege, order), solution layers, publish state, which apps use it, and a **Site map health** check (damaged XML, duplicate ids, sub areas that open a table or web resource that does not exist in that environment). Remove-unmanaged-layer and publish fixes work like for the other types; publishing a site map publishes all customizations in Dev and the confirmation window says so.
- **Ribbon (command bar)** as a component type, marked **beta**: compare of the buttons added or hidden, commands (with the script they run), enable/display rules and labels for a table's command bar or the application ribbon, plus a **Ribbon health** check (damaged XML, duplicate ids, scripts that do not exist, commands and rules that are not defined). How solution layers are reported for ribbons is not verified on every environment yet, and the tool does not remove ribbon layers (it explains the manual way instead).
### Changed
- **Component type is now a dropdown** (step 2), grouped by category: Screens and navigation (Form, View, Site map, Ribbon), Logic and automation (Process / cloud flow), Code and resources (Web resource). It replaces the four tiles, so more types fit later. Steps are named **1 Solution, 2 Component type, 3 Component**.
- The "health" rows of every type (Form health, Site map health, Ribbon health) are shown first.
## [1.2.1] - 2026-10-16
### Changed
- **Simpler screen.** The picker is now three short steps on the left (solution, what changed, which one) with one Diagnose button. The right side shows the answer first: a big verdict card with the next step and the main fix button, then form health, other problems and warnings as cards. Passed checks and the evidence (layers, differences, content) are collapsed until you open them.
- Toolbar reduced to the connections (Target, Dev menu) and three actions (Refresh lists, Export report, About).
- A short "how to start" message is shown before the first diagnosis. Diagnose stays disabled until an item is picked.
- **Show all details** button in the toolbar opens every section (passed checks, notes, evidence) at once; **Hide details** closes them again.
- Right-click any card > **Copy text** (the cards are not selectable like the old table cells).
- Notes that carry advice, such as "Connect Dev...", keep showing that advice in the collapsed list.
- Clearer step names: **1 Solution** (hint: solutions installed in Target), **2 What are you checking?**, **3 Pick the item**. The old label "Solution you deployed" was misleading.
- New tick box **Also show Microsoft and system solutions**; by default Microsoft, Default and Active solutions stay hidden.
- "Only items in this solution" moved under the item list, where it applies.
- **Evidence has two views, switchable with one click:** **User view** (plain words: the solution layers as labelled cards - UNMANAGED, ABOVE YOURS, YOUR SOLUTION, BELOW YOURS - and a Dev vs Target table with Missing in Target / Different / Only in Target; each part starts with "issue" or "no issue") and **Developer view** (the original tabs: layers grid, differences as text, Target / Dev / layer content). The Evidence title summarises the issues and the section opens by itself when there is one.
- Only the screen changed: the checks, fixes and reports are the same.
## [1.1.1] - 2026-10-15
### Added
- **Form health check** for forms, answering "is this form really corrupted?" in Target and (when connected) Dev, read-only:
  - Damaged XML (cannot be read, wrong root, duplicate tab / section / cell ids) = really corrupted.
  - Broken references (fields that are not columns of the table, script libraries that are not web resources, subgrid views, quick view forms and custom controls that do not exist) = the form is NOT corrupted, but it is broken.
  - Structure warnings (duplicate tab / section / control names, event handlers whose library is not added to the form).
  - Healthy forms get a green "NOT corrupted" row, so you can show that to a developer.
- **Same name, different id**: when the form is missing in Target, finds forms with the same name, table and type but another id (a recreated form); also warns about same-name forms next to a form that exists.
- The form health rows are always the first rows of the findings list (previously only when the form was damaged or healthy).
### Not verified yet
- The duplicate-id and name rules follow the form XML format; they have been tested on sample XML but not yet on damaged forms from a real environment.

## [1.0.0] - 2026-10-05
First public release (NuGet and XrmToolBox Tool Library). Same features as 0.3.2:
- Forms, views, web resources and processes (classic workflows, business rules, actions, business process flows, cloud and desktop flows).
- Generic checks: publish state in Dev, solution membership in Dev and Target, solution version, solution layers (unmanaged layer, managed solutions above yours, staged upgrade), Dev vs Target vs your layer content.
- Type checks: form state, apps, roles and order; view state, default view and apps; web resource usage; flow on/off, connection references and duplicate copies.
- Fixes (remove unmanaged layer, publish in Dev, add to Dev solution, turn on in Target), each explained in a "Before you continue" window with the user it runs as.
- Reports: HTML, CSV, text; full content and URLs only on request.

Not yet verified on every environment type: "Remove unmanaged layer" for web resources and processes (Dataverse may refuse it; the error is shown and nothing changes).

## [0.3.2] - 2026-10-02
### Changed
- Tool description in XrmToolBox and the NuGet package README now cover all component types, not only forms.

## [0.3.1] - 2026-10-02
### Changed
- Fix buttons now open a "Before you continue" window instead of a Yes/No box. It shows the environment, **which user the change runs as** (read from Dataverse with WhoAmI; application users are flagged), what it will do, whether it can be undone, advice on what to check first, and the manual steps to do the same in the maker portal (with "Copy manual steps").
- "Make the change" stays disabled until "I have read this..." is ticked; Enter and Esc cancel.

## [0.3.0] - 2026-10-02
### Added
- New component types besides forms: **views**, **web resources** and **processes** (classic workflows, business rules, actions, business process flows, cloud and desktop flows). Pick the type, then the table (or web resource prefix), then the item; typing in the item box filters the list.
- Views: compares columns (width, order), filters, sort order, related tables; checks view active, default view changed in Target, app membership.
- Web resources: compares the file (changed lines for JS/HTML/CSS/XML/SVG/RESX, size and hash for images); shows which forms and components use it, warns when no form uses a script; publish fix publishes just that web resource.
- Processes: compares cloud flow triggers, steps (nested steps included) and connection references, or the workflow XAML; checks turned on/off (with a confirmed "Turn on in Target" fix), connection references without a connection, duplicate copies with the same name, and notes that running instances keep the old version.
- Generic checks (publish, solution membership, layers, unmanaged layer fix, content compare, export) now work for every type.
### Changed
- Tabs renamed to "Target content", "Dev content", "Your solution's layer content"; export option is now "Include full content".
- Internals: one handler per component type (`Handlers/`), generic diagnosis in `DoctorService`.

## [0.2.2] - 2026-10-02
### Added
- Export options dialog for HTML reports: "Include full form XML" and "Include environment URLs", both off by default (remembered for the session).
- Sharing note at the top of HTML and text reports: configuration metadata only, no record data or credentials.

## [0.2.1] - 2026-10-02
### Fixed
- "Package content" no longer reports a Problem when Target's published form already matches Dev. The XML stored in a managed form layer is not always the full form (found in the first real test); it is now Info in that case.
- When the form matches Dev, the verdict says "Deployment worked", and a newer solution version in Dev is Info instead of a Warning.
### Added
- Differences tab and reports also list Dev vs your solution's layer XML.

## [0.2.0] - 2026-10-02
### Added
- Export report: HTML (to email or attach to a ticket), CSV (Excel), copy as text (Teams/chat). Reports include the form, solution, both environments, verdict, next step, an ordered "What to do next" list, all checks, solution layers, differences and the form XML.
- "Next step" line under the verdict.
### Changed
- "Copy report" button replaced by the "Export report" menu.

## [0.1.0] - 2026-10-02
### Added
- Form diagnosis: Target (main connection) plus optional Dev (second connection).
- Checks: Dev publish state, form in solution (Dev and Target, directly or through the table), solution versions, solution layers (unmanaged layer, managed solutions above yours, staged upgrade), Dev vs Target vs your layer form XML, form state, model-driven app membership, form security roles, default form order.
- Structural form XML comparison (tabs, sections, fields, field order, scripts, event handlers, access).
- Confirmed fixes: remove unmanaged layer in Target, publish table in Dev, add form to Dev solution.
- Copy report to the clipboard.
