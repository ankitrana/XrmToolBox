<p align="center">
  <img src="package/icon.png" width="96" alt="Unmanaged Layer Finder icon" />
</p>

<h1 align="center">Unmanaged Layer Finder</h1>

<p align="center">
  An <a href="https://www.xrmtoolbox.com/">XrmToolBox</a> tool that finds <b>unmanaged layers</b> on the components of your Dataverse / Dynamics 365 solutions, and shows <b>what changed, when, and by whom</b>.
</p>

<p align="center">
  <a href="https://github.com/ankitrana/XrmToolBox/actions/workflows/unmanaged-layer-finder.yml"><img src="https://github.com/ankitrana/XrmToolBox/actions/workflows/unmanaged-layer-finder.yml/badge.svg" alt="Build" /></a>
  <a href="https://www.nuget.org/packages/AnkitRana.XrmToolBox.UnmanagedLayerFinder"><img src="https://img.shields.io/nuget/v/AnkitRana.XrmToolBox.UnmanagedLayerFinder?label=XrmToolBox%20package" alt="NuGet" /></a>
  <img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4" alt=".NET Framework 4.8" />
  <a href="../LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT license" /></a>
</p>

<p align="center">
  <img src="docs/images/screenshot-results.png" width="900" alt="Unmanaged Layer Finder showing components with unmanaged layers highlighted" />
</p>

---

## Why

In a healthy ALM setup, test and production environments contain **managed** solutions only. When someone edits a component directly in one of those environments, Dataverse creates an **unmanaged ("Active") layer** on top of the managed one. That layer:

- silently overrides what your next deployment ships,
- blocks or confuses solution upgrades,
- is hard to find: the maker portal only shows layers one component at a time.

**Unmanaged Layer Finder scans a whole solution in one go** and tells you exactly which components have an unmanaged layer, what was changed, and who changed it.

## Features

- **Solution picker with filters**: managed / unmanaged / both, by publisher, and one click to hide Microsoft solutions
- **Component type filter**: focus on Forms, Views, Cloud Flows, Environment Variables, and more, with per-type unmanaged counts
- **Layer stack per component**: e.g. `Active > MySolution`
- **What changed**: attributes touched by the unmanaged layer
- **Who and when**: last modified by / on from the component record, plus an optional audit-log lookup
- **Readable component types**: Cloud Flow, Classic Workflow, Business Rule, Business Process Flow, Environment Variable Definition vs. Value (with the current value), connection references and other newer types
- **Export to CSV** for reports and clean-up tracking
- **Read-only**: the tool never changes your environment

## Install

In XrmToolBox, open **Tool Library**, search for **Unmanaged Layer Finder**, and click **Install**.

## How to use

1. Connect to an environment and open **Unmanaged Layer Finder** from the Tools tab.
2. Filter and pick a solution, then click **Load components**.
3. Optionally choose a **Component type**, tick **Select all**, and click **Check layers**.
4. Rows with an unmanaged layer are highlighted. Tick **Show only unmanaged** to focus on them.
5. Click **Export CSV** to share the results.

## How it works

Layer data comes from the Dataverse `msdyn_componentlayer` table. A component has an unmanaged layer when one of its layers belongs to the `Active` solution. "Who" comes from the component's own `modifiedby` (where the component is a record) or from the audit log when auditing was enabled at the time of the change.

Details and known limitations: [docs/how-it-works.md](docs/how-it-works.md).

## Feedback

Found a bug or have an idea? [Open an issue](https://github.com/ankitrana/XrmToolBox/issues/new/choose). All feedback is welcome.

## Building from source

Requirements: .NET SDK 8 or later and the .NET Framework 4.8 targeting pack (Windows).

```bash
dotnet build UnmanagedLayerFinder/src/UnmanagedLayerFinder/UnmanagedLayerFinder.csproj -c Release
```

Copy `UnmanagedLayerFinder/src/UnmanagedLayerFinder/bin/Release/UnmanagedLayerFinder.dll` to `%APPDATA%\MscrmTools\XrmToolBox\Plugins\` with XrmToolBox closed.

## Author

**Ankit Rana**, Dynamics 365 / Power Platform Solutions Architect
[LinkedIn](https://www.linkedin.com/in/mrankitrana/) · [GitHub](https://github.com/ankitrana) · [More tools](https://github.com/ankitrana/XrmToolBox)

## License

[MIT](../LICENSE) © 2026 Ankit Rana
