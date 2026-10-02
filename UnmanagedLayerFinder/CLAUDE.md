# Unmanaged Layer Finder (XrmToolBox plugin)

Author: Ankit Rana. Read-only XrmToolBox tool that finds unmanaged (`Active`) layers on a solution's components and shows when/who changed them.

This folder is one tool inside the repo `github.com/ankitrana/XrmToolBox`. Shared conventions, repo layout and git/publishing rules: `../CLAUDE.md` (read it too). All paths below are relative to THIS folder unless they say "repo root".

## Folder layout
- `src/UnmanagedLayerFinder/` - project and code (below)
- `package/` - nuspec, icon, NuGet README
- `docs/how-it-works.md`, `docs/images/` (screenshots from a demo env only - never client orgs)
- `README.md` (showcase page), `CHANGELOG.md` (update every release)
- Repo root: `LICENSE` (MIT), `.github/ISSUE_TEMPLATE/` (feedback forms), `.github/workflows/unmanaged-layer-finder.yml` (CI build, path-filtered)

## Files (src/UnmanagedLayerFinder)
- `UnmanagedLayerFinderPlugin.cs` - MEF export + metadata (name, description, icons). All 7 ExportMetadata keys are required or XrmToolBox silently skips the tool.
- `UnmanagedLayerFinderControl.cs` - WinForms UI built in code (no designer). Toolbar, solution filter row (type / publisher / exclude Microsoft), options row (component type filter, select all, only unmanaged, audit), grid, CSV export, About box.
- `LayerService.cs` - Dataverse logic: LoadSolutions, LoadComponents (+ dynamic type registration, name resolution), CheckComponent (layers, modifiedby, audit).
- `ComponentTypes.cs` - componenttype code -> label, msdyn_componentlayer name candidates, backing table. Codes > 10000 are org-specific and registered at runtime from `solutioncomponentdefinition`.
- `Models.cs` - ComponentRow (grid columns = its public properties, in order), SolutionItem.
- `PluginIcons.cs` - generated base64 PNGs (32px, 80px).
- `package/` - nuspec, icon, README for NuGet / Tool Library publishing.

## How layer detection works
- `msdyn_componentlayer` must be filtered by BOTH `msdyn_componentid` and `msdyn_solutioncomponentname`; one query per component.
- Unmanaged layer = row with `msdyn_solutionname == "Active"`.
- If a layer-name candidate returns 0 rows, the next candidate is tried; the working name is cached per type code.
- `msdyn_overwritetime` of 1900-01-01 means "not recorded" - shown blank.
- Who: `modifiedby` of the backing record (approximation), or latest `audit` row (only if auditing was on).
- Env variable Definition (380) and Value (381) are separate components; the maker portal "Solution layers" page for a variable shows the Definition only.

## Build and deploy (local test)
1. Bump `<Version>` in `src/UnmanagedLayerFinder/UnmanagedLayerFinder.csproj` - REQUIRED on every deploy (see gotchas).
2. `dotnet build src/UnmanagedLayerFinder/UnmanagedLayerFinder.csproj -c Release`
3. XrmToolBox must be closed (it locks the DLL).
4. Copy `src\UnmanagedLayerFinder\bin\Release\UnmanagedLayerFinder.dll` to `%APPDATA%\MscrmTools\XrmToolBox\Plugins\` and `Unblock-File` it. Only this DLL - the host already has the rest.
5. User's XrmToolBox is portable: `C:\Ankit Rana\Claude Work\XrmToolbox\XrmToolBox.exe` (v1.2026.9.78), but it reads plugins from the %APPDATA% folder above.

## Publish (NuGet -> XrmToolBox Tool Library)
1. Bump version in BOTH the csproj and `package/UnmanagedLayerFinder.nuspec`; update releaseNotes and CHANGELOG.md.
2. Build, then from THIS folder (absolute paths required): `dotnet pack src/UnmanagedLayerFinder/UnmanagedLayerFinder.csproj -c Release --no-build -p:NuspecFile="$PWD\package\UnmanagedLayerFinder.nuspec" -p:NuspecBasePath="$PWD\package" -o package`
3. User pushes with their own API key (never put the key in chat or files): `dotnet nuget push <nupkg> --api-key ... --source https://api.nuget.org/v3/index.json`
4. Package id `AnkitRana.XrmToolBox.UnmanagedLayerFinder` is permanent once pushed. Status: NOT yet published (as of 2026-10-02). projectUrl = github.com/ankitrana/XrmToolBox/tree/main/UnmanagedLayerFinder.
5. Release tag: `UnmanagedLayerFinder-v<version>`.

## Gotchas (learned the hard way)
- XrmToolBox 2026 caches plugin scans in `Plugins\manifest.json` keyed by file + version. Replacing a DLL without bumping the version = old (possibly "no plugin") result is reused.
- `Label` is ambiguous (System.Windows.Forms vs Microsoft.Xrm.Sdk) - use `System.Windows.Forms.Label` fully qualified.
- `MscrmTools.Xrm.Connection` must be pinned to 1.2025.9.64 to match XrmToolBoxPackage 1.2025.10.74 (CS1705 otherwise). MSB3277 warnings are suppressed.
- The `nuget.exe` in PowerAppsCLI is too old for `<icon>`; use `dotnet pack` with the nuspec.
- MEF load-test harness approach (scratch console referencing the host's XrmToolBox.Extensibility.dll + AssemblyCatalog) is the fastest way to tell "plugin broken" from "host not picking it up".

## Version history
- 1.3.0 component type filter (Select all / Check layers respect it)
- 1.2.0 flow categories, env var value details, dynamic types (connection references), 1900 date fix
- 1.1.0 solution type / publisher / exclude Microsoft filters
- 1.0.x initial, icons, author details, auto-load solutions

## Ideas not done yet
- "Flows only" quick filter; Cancel button for long checks; feedback link via XrmToolBox `IGitHubPlugin` (needs the GitHub username).

