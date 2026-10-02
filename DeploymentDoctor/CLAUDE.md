# Deployment Doctor (XrmToolBox plugin)

Author: Ankit Rana. Answers "I deployed the managed solution, why doesn't my change show in Target?" and offers confirmed fixes. Handles forms, views, web resources and processes (classic workflows, business rules, actions, BPFs, cloud/desktop flows) since v0.3.0.

This folder is one tool inside the repo `github.com/ankitrana/XrmToolBox`. Shared conventions, repo layout and git/publishing rules: `../CLAUDE.md` (read it too). Paths below are relative to THIS folder.

## Design decisions (agreed with Ankit, 2026-10-02)
- Started with forms; views, web resources and workflows added on his request (v0.3.0). Whole-solution compare (all flows on/off etc.) is a SEPARATE future tool, not a mode of this one.
- Diagnose by default; fixes only via Fix buttons, each with a Yes/No confirmation naming the environment (default button = No). This is the one tool in the repo that writes data.
- Main XrmToolBox connection = TARGET (where the change doesn't show). Dev = second connection via `MultipleConnectionsPluginControlBase` (optional; Dev checks skipped without it).
- Reports: full content and environment URLs OFF by default in HTML export (content can hold iframe URLs, keys, flow inputs; URLs reveal client orgs).

## Files (src/DeploymentDoctor)
- `DeploymentDoctorPlugin.cs` - MEF export + the 7 required metadata keys.
- `DeploymentDoctorControl.cs` - UI built in code: toolbar (Target/Dev labels, Connect Dev, Export report menu, About), pickers (solution, "only items in this solution", type, table/prefix, item with autocomplete, Diagnose), verdict banner, findings grid with Fix button column, tabs (layers, differences, Target/Dev/layer content). Items are loaded per type on first use and cached in `itemsByKind`.
- `DoctorService.cs` - solutions/tables loading, `IdsInSolution`, and the generic `Diagnose()`: exists, Dev publish state, solution membership (Dev + Target), version, layers (Active, above yours, `_Upgrade`, your layer), content (Dev vs Target, Dev vs your layer), Target unpublished, then `handler.TypeChecks`. Each check is wrapped in `Try()`. Findings are added in priority order, then stable-sorted by severity; the first Problem is the verdict.
- `Handlers/ComponentHandler.cs` - base class (table, componenttype, layer name, content attributes, Load/FromLayer/Compare/Pretty/Publish/TypeChecks, shared `CheckApps`), `Snapshot`, `CheckContext`, `Handlers` registry.
- `Handlers/FormHandler.cs`, `ViewHandler.cs`, `WebResourceHandler.cs`, `ProcessHandler.cs` - one per type (see docs/how-it-works.md for what each compares and checks).
- `Handlers/TextTools.cs` - `Json` (JavaScriptSerializer: parse, find value in msdyn_componentjson, pretty print) and `TextDiff` (lines only on one side, multiset, with line numbers; hash).
- `FormXmlDiff.cs` - form XML describe + `CompareMaps` (generic "item -> description" compare used by forms, views and flows) + `Add` (keeps duplicate keys).
- `ReportBuilder.cs` - Text / CSV / HTML reports. `ActionPlan()` = problems and warnings with a fix, in check order. `docs/sample-report.html` is generated from made-up demo data (Contoso) by calling ReportBuilder via reflection.
- `FixConfirmDialog.cs` - shown before every fix (Ankit's request 2026-10-02: never change anything on a direct click). Environment, caller (`DoctorService.GetCaller` = WhoAmI + systemuser, app users flagged), WhatItDoes, Undo, Advice, ManualSteps (copy button). "Make the change" disabled until the checkbox is ticked; Enter/Esc = Cancel. Every new FixAction MUST fill Undo, Advice and ManualSteps.
- `ExportOptionsDialog.cs` - include full content / environment URLs (both off by default, remembered per session). Text/CSV never contain content or URLs.
- `Models.cs` - SolutionItem, ComponentKind, ComponentItem, TableItem, GroupOption, Finding, FixAction, LayerRow, Diagnosis.
- `PluginIcons.cs` - generated base64 PNGs (green rounded square + white cross); `package/icon.png` is the 128px version.

## Dataverse notes
- Components keep the same id across environments; Dev and Target are matched by id.
- Layers: `msdyn_componentlayer` filtered by `msdyn_componentid` + `msdyn_solutioncomponentname` (SystemForm / SavedQuery / WebResource / Workflow), ordered by `msdyn_order` desc. `Active` = unmanaged, `<name>_Upgrade` = staged upgrade holding solution.
- Layer content: `msdyn_componentjson` (Key/Value pairs) parsed with JavaScriptSerializer (System.Web.Extensions, avoids a Newtonsoft version clash with the host).
- Solution membership: direct (componenttype 60/26/61/29) or via table (componenttype 1, rootcomponentbehavior 0) for forms, views and business rules only.
- `RemoveActiveCustomizationsRequest` isn't in CoreAssemblies 9.0.2.59, so it's sent as `OrganizationRequest("RemoveActiveCustomizations")` with `SolutionComponentName` + `ComponentId`.
- App membership: `appmodulecomponent` (componenttype 60 forms / 26 views) linked to `appmodule` on `appmoduleidunique`. An app with no rows for a table shows all.
- Form roles in `formxml` `DisplayConditions/Role/@Id` are root role ids; matched against `role.roleid` OR `parentrootroleid`.
- Views: `savedquery.statecode` read in a separate try (not in Load) so an org without it doesn't make the view "not found".
- Web resources: `content` is base64; publish with `<webresources><webresource>{id}</webresource></webresources>`; usage via `RetrieveDependentComponents` (type 61).
- Processes: `workflow` type 1 (definitions); not publishable; on = statecode 1 / statuscode 2 (SetState). Cloud flow `clientdata` -> properties.definition (triggers, actions with nested actions/else/cases/default) and properties.connectionReferences[*].connection.connectionReferenceLogicalName; `connectionreference.connectionid` empty = no connection.

## Build and deploy (local test)
1. Bump `<Version>` in `src/DeploymentDoctor/DeploymentDoctor.csproj` (required every deploy).
2. `dotnet build src/DeploymentDoctor/DeploymentDoctor.csproj -c Release`
3. Close XrmToolBox, copy `src\DeploymentDoctor\bin\Release\DeploymentDoctor.dll` to `%APPDATA%\MscrmTools\XrmToolBox\Plugins\`, `Unblock-File` it. (If XrmToolBox is open the DLL is locked; a background PowerShell loop that waits for the process to exit and then copies works well.)
4. Offline testing of comparers: load the DLL with `[Reflection.Assembly]::Load(bytes)` in PowerShell, map System.Memory/System.Buffers/Unsafe/Vectors from the NuGet cache in an AssemblyResolve handler (exact paths, not a recursive search: that hangs), call handler methods via reflection. Don't name a PowerShell function `H` (alias of Get-History).

## Publish
Same as Unmanaged Layer Finder (see its CLAUDE.md), with `package/DeploymentDoctor.nuspec`. NuGet id `AnkitRana.XrmToolBox.DeploymentDoctor` (permanent once pushed). Tag `DeploymentDoctor-v<version>`.

## Learned from real tests
- 2026-10-02 (custom table form, one managed layer): Target published formxml == Dev, but the formxml in the managed layer's `msdyn_componentjson` differed in 6 places. So layer content is NOT always the full published component; "Package content" is only a Problem when the published version also differs from Dev (else Info). `LayerDifferences` are shown to learn more.

## Not verified yet (test in a real env)
- Shape of `msdyn_componentjson` per type (what the 6 form layer differences were; views, web resources, workflows untested).
- RemoveActiveCustomizations end to end, especially for WebResource and Workflow (may be unsupported).
- Views: `savedquery.statecode`; Processes: SetState on a managed cloud flow; connectionreference query.
- `RetrieveUnpublished` with no pending changes (expected: returns the published copy).

## Ideas / next
- Sitemap (62), ribbons, model-driven app module, plugin steps; "check every component in the solution" batch mode belongs to the planned separate compare tool.
- Optional solution zip input instead of a Dev connection.
