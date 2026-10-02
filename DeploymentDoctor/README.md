<p align="center">
  <img src="package/icon.png" width="96" alt="Deployment Doctor icon" />
</p>

<h1 align="center">Deployment Doctor</h1>

<p align="center">
  An <a href="https://www.xrmtoolbox.com/">XrmToolBox</a> tool that answers the classic ALM question: <b>"I deployed the managed solution. Why doesn't my change show?"</b><br/>
  Works for <b>forms, views, web resources, classic workflows, business rules, actions and cloud flows</b>.
</p>

<p align="center">
  <a href="https://github.com/ankitrana/XrmToolBox/actions/workflows/deployment-doctor.yml"><img src="https://github.com/ankitrana/XrmToolBox/actions/workflows/deployment-doctor.yml/badge.svg" alt="Build" /></a>
  <a href="https://www.nuget.org/packages/AnkitRana.XrmToolBox.DeploymentDoctor"><img src="https://img.shields.io/nuget/v/AnkitRana.XrmToolBox.DeploymentDoctor?label=XrmToolBox%20package" alt="NuGet" /></a>
  <img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4" alt=".NET Framework 4.8" />
  <a href="../LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT license" /></a>
</p>

---

## Why

A change made in Dev and shipped in a managed solution can fail to appear in UAT or Prod for many reasons. Each one is checked in a different screen, and some aren't visible anywhere:

| Cause | Applies to | What you see |
|---|---|---|
| Changes not published in Dev before export | forms, views, web resources | The package simply doesn't contain them |
| Component not in the solution (table added without it) | all | Import succeeds, nothing changes |
| Unmanaged (Active) layer in Target | all | Someone edited it in Target; it overrides your deployment |
| Another managed solution installed above yours | all | Its version wins |
| Staged upgrade never applied / imported as Update | all | Removed items are still there |
| Flow or workflow turned off after import, connection reference without a connection | processes | Nothing runs |
| Old copy with the same name still on | processes | The old behaviour keeps happening |
| Form or view inactive, missing from the app, not the default; form roles or order | forms, views | Users simply open a different one |
| Script not used by any form; browser cache | web resources | The new code never runs |

**Deployment Doctor checks all of them for one component in one click**, shows the evidence (solution layers and a readable Dev vs Target comparison), names the most likely cause and tells you how to fix it.

## Features

- **Two environments**: Target (where the change doesn't show) as the main connection, Dev (source) as a second connection
- **Verdict first**: "Most likely cause: ..." at the top, with every check listed below as Problem / Warning / OK / Info
- **Solution layers** of the component in Target, with your solution, layers above it and the unmanaged layer highlighted
- **Readable comparison** of Dev vs Target vs your solution's layer:
  - forms: tabs, sections, fields, field order, labels, visibility, scripts, event handlers, form access, form order
  - views: columns and widths, column order, filters, sort order, related tables
  - web resources: changed lines (JS, HTML, CSS, XML, SVG, RESX), size and hash for images
  - cloud flows: triggers, steps (including steps inside conditions, loops and scopes), connection references; workflows: definition lines
- **Confirmed fixes**: remove the unmanaged layer in Target, publish in Dev, add to the Dev solution, turn a flow or workflow on in Target. Each one shows exactly what changes, and where, before it runs
- **Export report** as HTML (email or ticket), CSV (Excel) or text (Teams), so anyone can see the cause and the next steps. Form XML and environment URLs are left out unless you tick them. See the [sample report](docs/sample-report.html)

## Install

In XrmToolBox, open **Tool Library**, search for **Deployment Doctor**, and click **Install**.

## How to use

1. Connect XrmToolBox to the **Target** environment (UAT/Prod) and open **Deployment Doctor**.
2. Click **Connect Dev (source)...** and pick your Dev connection (optional, but enables the most useful checks).
3. Pick the **solution you deployed**, the **type** (Form, View, Web resource, Process / flow), the **table** (or web resource prefix) and the item. Type in the item box to filter.
4. Click **Diagnose**. Read the verdict, then the findings. Use a **Fix** button if one is offered.
5. Click **Export report** to share the result as HTML, CSV or text.

## How it works

See [docs/how-it-works.md](docs/how-it-works.md).

## Feedback

[Open an issue](https://github.com/ankitrana/XrmToolBox/issues/new/choose) and pick **Deployment Doctor**.

## Author

Created by **Ankit Rana**, Dynamics 365 / Power Platform Solutions Architect. [LinkedIn](https://www.linkedin.com/in/mrankitrana/) · [GitHub](https://github.com/ankitrana)

MIT licensed.
