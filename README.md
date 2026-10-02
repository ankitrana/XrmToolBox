<h1 align="center">XrmToolBox tools by Ankit Rana</h1>

<p align="center">
  Free <a href="https://www.xrmtoolbox.com/">XrmToolBox</a> tools that make Dataverse / Dynamics 365 administration and ALM safer and faster.
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT license" /></a>
  <img src="https://img.shields.io/badge/platform-XrmToolBox-6A5ACD" alt="XrmToolBox" />
  <img src="https://img.shields.io/badge/Dataverse-Dynamics%20365-0078D4" alt="Dataverse" />
</p>

---

## Tools

| | Tool | What it does | Status |
|---|---|---|---|
| <img src="UnmanagedLayerFinder/package/icon.png" width="32" /> | [**Unmanaged Layer Finder**](UnmanagedLayerFinder/) | Scans a whole solution for unmanaged (Active) layers and shows what changed, when, and by whom. Filters by solution type, publisher and component type; exports to CSV. | [![Build](https://github.com/ankitrana/XrmToolBox/actions/workflows/unmanaged-layer-finder.yml/badge.svg)](https://github.com/ankitrana/XrmToolBox/actions/workflows/unmanaged-layer-finder.yml) |
| <img src="DeploymentDoctor/package/icon.png" width="32" /> | [**Deployment Doctor**](DeploymentDoctor/) | "I deployed the managed solution, why doesn't my change show?" For forms, views, web resources, workflows and cloud flows: compares Dev and Target, checks solution layers, publish state, solution membership, flow state and connections, apps, form roles and order; names the cause, offers confirmed fixes and exports a shareable report. | [![Build](https://github.com/ankitrana/XrmToolBox/actions/workflows/deployment-doctor.yml/badge.svg)](https://github.com/ankitrana/XrmToolBox/actions/workflows/deployment-doctor.yml) |

## Install

All tools are installed from inside XrmToolBox: open **Tool Library**, search for the tool name, and click **Install**.

## Feedback

Bugs, ideas, or a tool you wish existed? [Open an issue](https://github.com/ankitrana/XrmToolBox/issues/new/choose) and pick the tool from the list.

## Repository layout

```
XrmToolBox/
├── UnmanagedLayerFinder/      one folder per tool: src/, package/, docs/, README, CHANGELOG
├── DeploymentDoctor/
├── .github/                   issue forms and one build workflow per tool
└── LICENSE                    MIT, applies to all tools
```

## Author

**Ankit Rana**, Dynamics 365 / Power Platform Solutions Architect
[LinkedIn](https://www.linkedin.com/in/mrankitrana/) · [GitHub](https://github.com/ankitrana)
