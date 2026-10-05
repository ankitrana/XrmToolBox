# Changelog

All notable changes to Deployment Doctor. Versions follow [Semantic Versioning](https://semver.org/).

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
