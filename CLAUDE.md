# XrmToolBox tools (AnkitRana-Tech)

Repo: `github.com/ankitrana/XrmToolBox` (Ankit's personal GitHub account; AnkitRana-Tech is the local brand folder. Can be moved to an `ankitrana-tech` organization later - GitHub redirects old links). Public portfolio repo, MIT. One folder per XrmToolBox tool; each tool folder has its own CLAUDE.md with architecture, build/deploy steps and gotchas - read it before working on that tool.

## Local structure
```
C:\Ankit Rana\Claude Work\
â”œâ”€â”€ AnkitRana-Tech\              personal brand (owned work only)
â”‚   â”œâ”€â”€ XrmToolBox\              THIS repo
â”‚   â”‚   â”œâ”€â”€ <ToolName>\          src/<ToolName>/ (csproj + code), package/ (nuspec, icon), docs/, README.md, CHANGELOG.md, CLAUDE.md
â”‚   â”‚   â”œâ”€â”€ .github\             ISSUE_TEMPLATE (tool dropdown) + workflows/<tool-name>.yml (path-filtered build per tool)
â”‚   â”‚   â”œâ”€â”€ README.md            tools table (add a row per tool)
â”‚   â”‚   â””â”€â”€ LICENSE              MIT for all tools
â”‚   â””â”€â”€ ankitrana-profile\       draft for the github.com/ankitrana profile README repo
â””â”€â”€ <CompanyName>\               same pattern per company; company code goes to THAT company's GitHub, never here
```

## Tools
| Tool | Folder | Status |
|---|---|---|
| Unmanaged Layer Finder | `UnmanagedLayerFinder\` | v1.3.0 works locally; repo not yet pushed; not yet on NuGet |
| Deployment Doctor | `DeploymentDoctor\` | v0.3.2 builds (forms, views, web resources, processes); first real-env test done 2026-10-02 (fixed false Package-content problem); fixes and export not yet tested; not on NuGet. Writes data only via confirmed Fix buttons |

## Adding a new tool
1. Copy the UnmanagedLayerFinder layout into `<ToolName>\` (rename project, namespace, plugin class, nuspec id, icons).
2. Add `.github/workflows/<tool-name>.yml` (copy and change paths), add the tool to both issue form dropdowns, add a row to README.md and the table above.
3. NuGet id: `AnkitRana.XrmToolBox.<ToolName>` (same prefix for all tools; ids are permanent once pushed).
4. Release tags per tool: `<ToolName>-v<version>`.

## Shared conventions
- SDK-style csproj, `net48`, `UseWindowsForms`, `LangVersion latest`, `AppendTargetFrameworkToOutputPath=false`, `NoWarn MSB3277`.
- Packages: `XrmToolBoxPackage` + `MscrmTools.Xrm.Connection` pinned to the version XrmToolBoxPackage needs.
- Plugin class: `[Export(typeof(IXrmToolBoxPlugin))]` with ALL of Name, Description, SmallImageBase64 (32px), BigImageBase64 (80px), BackgroundColor, PrimaryFontColor, SecondaryFontColor - a missing key makes XrmToolBox skip the tool silently.
- Control: `PluginControlBase`, UI built in code, Dataverse calls via `ExecuteMethod` + `WorkAsync`. Tools are read-only unless explicitly designed otherwise.
- Author "Ankit Rana" in description, status bar, About box, assembly info.
- Bump the version on EVERY local deploy (XrmToolBox caches plugin scans in Plugins\manifest.json by file + version). Close XrmToolBox before copying; deploy only the plugin DLL.
- Environment: VS 18 Professional, .NET SDK 9/10, .NET Framework 4.8 targeting pack. Ankit's XrmToolBox is portable (`C:\Ankit Rana\Claude Work\XrmToolbox\XrmToolBox.exe`) and loads plugins from `%APPDATA%\MscrmTools\XrmToolBox\Plugins\`.

## Git and publishing
- Repo-local git identity (set 2026-10-02): `Ankit Rana` / `6076532+ankitrana@users.noreply.github.com`. Never use the machine's global git email (it is a work account).
- Public repo: no client names, org URLs, customer data, or screenshots from client environments (use a trial/developer env).
- Author links: LinkedIn https://www.linkedin.com/in/mrankitrana/ , GitHub https://github.com/ankitrana
- NuGet push and xrmtoolbox.com submission are done by Ankit with his own credentials; never put API keys in chat or files.
