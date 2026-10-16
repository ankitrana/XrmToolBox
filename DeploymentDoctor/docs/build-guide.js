// Builds docs/Deployment-Doctor-User-Guide.docx (then the PDF is exported from Word).
// Run from the DeploymentDoctor folder:   node docs/build-guide.js
// Then open the .docx in Word, right-click the table of contents > Update field, save,
// and File > Save As > PDF to refresh Deployment-Doctor-User-Guide.pdf.
// Keep this guide in step with README.md and the start screen of the tool (whenever a feature is added or removed).
// Screenshots in docs/images use fictitious "Contoso" data: never put client names or screenshots in them.
const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType, Table, TableRow, TableCell,
  WidthType, ShadingType, BorderStyle, ImageRun, TableOfContents, Header, Footer, PageNumber,
  LevelFormat, ExternalHyperlink, TabStopType,
} = require("docx");

const ROOT = path.resolve(__dirname, "..");
const VERSION = "1.5.2";
const OUT = path.join(ROOT, "docs/Deployment-Doctor-User-Guide.docx");

const BRAND = "0F6CBD";
const DARK = "1B1B1B";
const MUTED = "605E5C";
const LIGHT = "EBF3FC";
const ZEBRA = "F5F5F5";
const FONT = "Segoe UI";
const CONTENT = 9026; // A4 width 11906 - 2 x 1440 margins

// PNG size from the header, so a picture keeps its proportions
function pngSize(buf) { return { w: buf.readUInt32BE(16), h: buf.readUInt32BE(20) }; }

function runs(text, base = {}) {
  const out = [];
  const re = /(\*\*[^*]+\*\*|\*[^*]+\*|`[^`]+`)/g;
  let last = 0, m;
  while ((m = re.exec(text))) {
    if (m.index > last) out.push(new TextRun({ text: text.slice(last, m.index), ...base }));
    const t = m[0];
    if (t.startsWith("**")) out.push(new TextRun({ text: t.slice(2, -2), bold: true, ...base }));
    else if (t.startsWith("*")) out.push(new TextRun({ text: t.slice(1, -1), italics: true, ...base }));
    else out.push(new TextRun({ text: t.slice(1, -1), font: "Consolas", size: 19, color: "A4262C", ...base }));
    last = m.index + t.length;
  }
  if (last < text.length) out.push(new TextRun({ text: text.slice(last), ...base }));
  return out;
}

const p = (text, opts = {}) => new Paragraph({ children: runs(text), spacing: { after: 120 }, ...opts });
const h1 = (text, pageBreak = true) => new Paragraph({ heading: HeadingLevel.HEADING_1, pageBreakBefore: pageBreak, children: [new TextRun(text)] });
const h2 = (text) => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun(text)] });

let listCount = 0;
const numberingConfigs = [];
function numbered(items) {
  const ref = `steps${++listCount}`;
  numberingConfigs.push({
    reference: ref,
    levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT,
      style: { paragraph: { indent: { left: 400, hanging: 300 } } } }],
  });
  return items.map((t) => new Paragraph({ numbering: { reference: ref, level: 0 }, children: runs(t), spacing: { after: 80 } }));
}
function bullets(items) {
  return items.map((t) => new Paragraph({ numbering: { reference: "bullets", level: 0 }, children: runs(t), spacing: { after: 80 } }));
}

function note(text, label = "Note") {
  return new Paragraph({
    children: [new TextRun({ text: `${label}: `, bold: true, color: BRAND }), ...runs(text)],
    shading: { type: ShadingType.CLEAR, fill: LIGHT, color: "auto" },
    border: { left: { style: BorderStyle.SINGLE, size: 24, color: BRAND, space: 8 } },
    indent: { left: 160, right: 160 },
    spacing: { before: 120, after: 200 },
  });
}

// maxW in pixels (about 600 fits the page), height follows the picture
function image(file, maxW, caption) {
  const data = fs.readFileSync(path.join(ROOT, "docs/images", file));
  const { w, h } = pngSize(data);
  const width = Math.min(maxW, w);
  const height = Math.round((width * h) / w);
  const out = [new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { before: 120, after: caption ? 60 : 200 },
    children: [new ImageRun({ type: "png", data, transformation: { width, height },
      altText: { title: caption || file, description: caption || file, name: file } })],
  })];
  if (caption) out.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 240 },
    children: [new TextRun({ text: caption, italics: true, size: 18, color: MUTED })] }));
  return out;
}

const cellBorder = { style: BorderStyle.SINGLE, size: 4, color: "D1D1D1" };
const borders = { top: cellBorder, bottom: cellBorder, left: cellBorder, right: cellBorder };
function table(header, rows, widths) {
  const total = widths.reduce((a, b) => a + b, 0);
  if (total !== CONTENT) throw new Error(`Table widths ${total} != ${CONTENT}`);
  const mkCell = (text, i, isHeader, zebra) => new TableCell({
    borders,
    width: { size: widths[i], type: WidthType.DXA },
    shading: isHeader ? { type: ShadingType.CLEAR, fill: BRAND, color: "auto" }
      : zebra ? { type: ShadingType.CLEAR, fill: ZEBRA, color: "auto" } : undefined,
    margins: { top: 80, bottom: 80, left: 120, right: 120 },
    children: [new Paragraph({ spacing: { after: 0 },
      children: isHeader ? [new TextRun({ text, bold: true, color: "FFFFFF" })] : runs(text) })],
  });
  return new Table({
    width: { size: CONTENT, type: WidthType.DXA },
    columnWidths: widths,
    rows: [
      new TableRow({ tableHeader: true, children: header.map((t, i) => mkCell(t, i, true)) }),
      ...rows.map((r, ri) => new TableRow({ cantSplit: true, children: r.map((t, i) => mkCell(t, i, false, ri % 2 === 1)) })),
    ],
  });
}
const spacer = () => new Paragraph({ spacing: { after: 120 }, children: [] });

// ------------------------------------------------------------------ content
const cover = [
  new Paragraph({ spacing: { before: 1800, after: 0 }, children: [new TextRun({ text: "XrmToolBox tool  ·  USER GUIDE", bold: true, size: 20, color: BRAND, characterSpacing: 40 })] }),
  new Paragraph({ spacing: { before: 120, after: 0 }, children: [new TextRun({ text: "Deployment Doctor", bold: true, size: 72, color: DARK })] }),
  new Paragraph({ spacing: { after: 240 }, children: [new TextRun({ text: "Why doesn't my deployed change show in the target environment?", size: 36, color: MUTED })] }),
  new Paragraph({
    spacing: { after: 480 },
    border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: BRAND, space: 12 } },
    children: [new TextRun({ text: "Check one component and get the cause and the fix, or compare a whole solution between Dev and Target in one go.", size: 24, color: DARK })],
  }),
  ...image("screen-diagnosis.png", 520, "Sample data (a fictitious Contoso environment)"),
  new Paragraph({ spacing: { before: 600, after: 60 }, children: [new TextRun({ text: `Version ${VERSION}  ·  October 2026`, size: 22, color: MUTED })] }),
  new Paragraph({ spacing: { after: 60 }, children: [new TextRun({ text: "Ankit Rana, Dynamics 365 / Power Platform Solutions Architect", size: 22, color: MUTED })] }),
  new Paragraph({ children: [
    new ExternalHyperlink({ link: "https://www.linkedin.com/in/mrankitrana/", children: [new TextRun({ text: "linkedin.com/in/mrankitrana", style: "Hyperlink", size: 22 })] }),
  ] }),
];

const toc = [
  new Paragraph({ pageBreakBefore: true, spacing: { after: 240 }, children: [new TextRun({ text: "Contents", bold: true, size: 36, color: BRAND })] }),
  new TableOfContents("Contents", { hyperlink: true, headingStyleRange: "1-2" }),
];

const body = [
  // 1
  h1("1. What is Deployment Doctor?", false),
  p("Deployment Doctor is a free tool for XrmToolBox that answers the classic ALM question:"),
  p("**\"I deployed the managed solution. Why doesn't my change show?\"**"),
  p("It has two modes, switched in the toolbar:"),
  ...bullets([
    "**Check one item**: pick the component that doesn't show your change. Deployment Doctor runs every relevant check in one click, shows the evidence, names the most likely cause and tells you how to fix it.",
    "**Compare whole solution**: pick the solution you deployed and see every component compared between Dev and Target. Red means the change did not arrive, orange means something needs a look, green means it is the same (the colours are used in the tool and in the exports).",
  ]),
  table(["Information", "Detail"], [
    ["Tool", "Deployment Doctor for XrmToolBox"],
    ["Version covered", VERSION],
    ["Works with", "Forms, views, site maps, ribbons (command bars), classic workflows, business rules, actions, business process flows, cloud flows, environment variables, connection references, plug-in steps, plug-in assemblies and web resources"],
    ["Access level", "Diagnosis and compare are read-only. Fixes run only after your confirmation."],
    ["Install from", "XrmToolBox Tool Library"],
    ["Author", "Ankit Rana"],
    ["Feedback", "github.com/ankitrana/XrmToolBox/issues"],
  ], [2400, 6626]),

  // 2
  h1("2. The problem it solves", false),
  p("A change made in Dev and shipped in a managed solution can fail to appear in UAT or Production for many different reasons. Each reason is checked in a different screen, and some aren't visible anywhere. Finding the cause by hand often takes hours of clicking between environments."),
  table(["Cause", "Applies to", "What you see"], [
    ["Changes not published in Dev before export", "Forms, views, web resources", "The package simply doesn't contain the change."],
    ["Component not in the solution (for example a table added without it)", "All", "Import succeeds, but nothing changes."],
    ["Unmanaged (Active) layer in Target", "All", "Someone edited it directly in Target. That overrides your deployment."],
    ["Another managed solution installed above yours", "All", "The other solution's version wins."],
    ["Staged upgrade never applied, or imported as Update", "All", "Removed items are still there."],
    ["Flow or workflow turned off after import; connection reference without a connection", "Processes, connection references", "Nothing runs."],
    ["Environment variable with no value in Target", "Environment variables", "Flows and apps read nothing."],
    ["Plug-in step disabled, or an older assembly version in Target", "Plug-in steps and assemblies", "The server logic never runs, or runs the old code."],
    ["Old copy with the same name still on", "Processes", "The old behaviour keeps happening."],
    ["Form or view inactive, missing from the app, not the default; form roles or order", "Forms, views", "Users open a different one."],
    ["Script not used by any form; browser cache", "Web resources", "The new code never runs."],
    ["Form XML damaged, or pointing at fields, scripts or views that don't exist", "Forms, site maps, ribbons", "\"The form is corrupted.\""],
  ], [3900, 2200, 2926]),
  spacer(),
  p("Deployment Doctor checks all of these in one click."),

  // 3
  h1("3. What it can check", false),
  table(["Component", "What is compared and checked"], [
    ["**Form**", "Tabs, sections, fields, field order, labels, visibility, scripts, event handlers. Form active, in the apps, security roles, form order. **Form health** (really corrupted, or only broken references?). Recreated forms with the same name."],
    ["**View**", "Columns and widths, column order, filters, sort order, related tables. View active, default view, in the apps."],
    ["**Site map**", "Areas, groups and sub areas (title, table, URL, icon, privilege, order). **Site map health**. Which apps use it."],
    ["**Ribbon (command bar)**", "Buttons added or hidden, commands and the scripts they run, rules, labels. **Ribbon health**."],
    ["**Process / cloud flow**", "Workflow definition lines. For flows: triggers, steps and connection references. Turned on or off. Old copies with the same name."],
    ["**Environment variable**", "Type and default value. Whether Target has any value. (Current values differ per environment by design, so they are shown, not compared.)"],
    ["**Connection reference**", "Connector, and whether it has a connection in Target."],
    ["**Plug-in step**", "Message, stage, mode, execution order, plug-in class and assembly version, filtering columns. Enabled or disabled."],
    ["**Plug-in assembly**", "Version, isolation mode and the DLL itself (size and fingerprint)."],
    ["**Web resource**", "Changed lines (JS, HTML, CSS, XML, SVG, RESX). For images the size and a hash. Which forms use it."],
  ], [2400, 6626]),
  spacer(),
  h2("What every check looks at"),
  ...bullets([
    "**Dev**: unpublished changes (an export takes the published version) and whether the component is in the solution.",
    "**Target**: whether the component is in the solution, and the solution versions in Dev and Target.",
    "**Solution layers**: the unmanaged (Active) layer on top, other managed solutions above yours, a staged upgrade that was never applied.",
    "**Content**: Dev vs Target vs your solution's layer, shown in readable form.",
  ]),

  // 4
  h1("4. Before you start", false),
  ...bullets([
    "XrmToolBox installed on a Windows PC (www.xrmtoolbox.com).",
    "**Target connection.** The environment where the change doesn't show (UAT or Production). It is the main XrmToolBox connection.",
    "**Dev connection** (needed for Compare whole solution, and recommended for one item). The environment where the change was made. It is added as a second connection inside the tool and unlocks the most useful checks.",
    "**Permissions.** Reading is enough to diagnose and compare. To use a Fix, the connected account needs permission to make that change. Dataverse checks this, and if it refuses, nothing changes.",
  ]),
  note("Diagnosing and comparing only read. A fix runs only when you click a Fix button and confirm it in a window that explains exactly what will change.", "The tool never changes anything without your confirmation"),

  // 5
  h1("5. Installing the tool", false),
  ...numbered([
    "Open XrmToolBox.",
    "Open the **Tool Library**.",
    "Search for **Deployment Doctor**.",
    "Click **Install**, then restart XrmToolBox if it asks.",
  ]),
  p("XrmToolBox also shows an update notice when a new version is published, with the release notes."),

  // 6
  h1("6. Check one item, step by step"),
  p("The start screen lists what the tool can check, what you get, how to start, and what it does not do yet."),
  ...image("screen-start.png", 420, "The start screen"),
  h2("Step 1: Connect to the Target environment"),
  p("In XrmToolBox connect to the environment where the change doesn't show, then open **Deployment Doctor**. The toolbar shows **Target: <name>** in green when it is connected."),
  h2("Step 2: Connect Dev"),
  p("Open the **Dev** menu in the toolbar and choose **Connect Dev (source)...**, then pick your Dev connection. Only one Dev connection is kept; a new one replaces the old one."),
  h2("Step 3: Pick what to diagnose"),
  ...numbered([
    "**1 Solution**: the solutions installed in Target. Tick *Also show Microsoft and system solutions* if you need those.",
    "**2 Component type**: Form, View, Site map, Ribbon, Process / cloud flow, Environment variable, Connection reference, Plug-in step, Plug-in assembly or Web resource.",
    "**3 Component**: the table (or prefix) and then the component. Type in the box to filter long lists. *Only items in this solution* is ticked by default.",
  ]),
  h2("Step 4: Click Diagnose"),
  p("The right side shows the answer first, then the evidence. Nothing is changed by Diagnose."),
  h2("Step 5: Use a Fix, if one is offered"),
  p("A **Fix** button appears next to a finding when the tool can change it for you. See section 9."),
  h2("Step 6: Export the report"),
  p("Use **Export report** in the toolbar to share the result. See section 10."),

  // 7
  h1("7. Reading the results", false),
  ...image("screen-diagnosis.png", 600, "A diagnosis: the most likely cause first, then what was checked"),
  h2("The verdict"),
  p("The big card at the top names the **most likely cause**, says what to do next, and shows the main fix button if there is one. If the only difference is the symptom (\"Target vs Dev\"), the card names the real cause when one was found."),
  h2("Result levels"),
  table(["Level", "Meaning"], [
    ["**Problem** (red)", "Something is wrong and is probably why the change doesn't show."],
    ["**Warning** (orange)", "Worth a look; it may explain what users see."],
    ["**OK** (green)", "Checked and fine."],
    ["**Info** (grey)", "Useful background."],
  ], [2400, 6626]),
  spacer(),
  p("Problems and warnings are shown as cards with a **How to fix** line. Passed checks and the evidence are collapsed until you open them; **Show all details** in the toolbar opens everything. Right-click any card and choose **Copy text** to paste it into a chat or ticket."),
  h2("\"Is it really corrupted?\": the health check"),
  p("For forms, site maps and ribbons the first card answers whether the item is really corrupted:"),
  ...bullets([
    "**Damaged XML** (cannot be read, duplicate ids, wrong structure): it is really corrupted and needs repairing in Dev.",
    "**Healthy XML but broken references** (fields, scripts, views, quick view forms, controls, tables that don't exist in that environment): it is not corrupted, but it is broken. Add the missing items to the solution.",
    "**Healthy**: a green card says so.",
  ]),
  p("Under the card, **What was checked** lists every test for each environment with a tick, a cross, a warning sign or a dash (nothing of that kind on the item), for example *Fields: 47 fields on the form exist as columns of table account in Target*. You can show this to a developer."),
  h2("The unmanaged layer"),
  p("If someone customized the component directly in Target, the verdict names the unmanaged layer. Under it, **What the unmanaged layer changed** lists:"),
  ...bullets([
    "the parts Dataverse says changed, and what differs from the managed layer right below it;",
    "when the unmanaged layer was **last changed** (in your computer's local time);",
    "whether that is **older or newer than your last deployment**: older means it was already there and the deployment did not remove it, newer means it was made after the deployment.",
  ]),
  note("Dataverse keeps only the time a layer was last written, so the tool cannot tell when it was first added, and it cannot tell for sure who made the change. The component's own modified date is not used because every import changes it.", "Limit"),
  h2("The Evidence section: User view and Developer view"),
  ...bullets([
    "**User view**: plain words. The solution layers as labelled cards (UNMANAGED, ABOVE YOURS, YOUR SOLUTION, BELOW YOURS) and a Dev vs Target table with *Missing in Target*, *Different* and *Only in Target*.",
    "**Developer view**: the detailed tabs: layers grid, differences as text, and the Target, Dev and layer content.",
  ]),

  // 8
  h1("8. Compare whole solution"),
  p("Use this after a deployment to check that everything arrived."),
  ...numbered([
    "Connect Target (main connection) and Dev (second connection).",
    "Click **Compare whole solution** in the toolbar.",
    "Pick the solution on the left and click **Compare with Dev**.",
  ]),
  p("Every component of the solution in Dev is found in Target and compared. A progress message shows where it is. A large solution can take a minute or more."),
  ...image("screen-compare.png", 600, "Compare whole solution (sample data)"),
  table(["Status", "Meaning"], [
    ["**Same**", "Content and state match."],
    ["**Different**", "The published content differs. The details list exactly what."],
    ["**Missing in Target**", "The component exists in the solution in Dev but not in Target: the import did not deliver it."],
    ["**State differs**", "The content is the same but the state is not, for example a flow on in Dev and off in Target, an inactive form or view, a disabled plug-in step, a connection reference with no connection, an environment variable with no value."],
    ["**Not published in Dev**", "Dev has unpublished changes. An export contains the published version, so those changes were not deployed."],
    ["**Only in Target**", "Still in the solution in Target but no longer in Dev. An 'Update' import never deletes anything; import as 'Upgrade' or apply the staged upgrade."],
    ["**Could not check**", "A read failed (for example no permission). The rest of the compare continues."],
  ], [2700, 6326]),
  spacer(),
  ...bullets([
    "The header shows the counts, the solution version in Dev and Target, and the component types that are **not compared yet** (tables, columns, plug-in registrations of other types and so on are only counted).",
    "Click a row for the full list of differences, plus a hint when Target has an unmanaged layer on it.",
    "**Diagnose this component** opens that component in the normal doctor, with the cause and the fix.",
    "Tick **Hide components that match** to see only what needs attention.",
    "**Export report** saves the compare as HTML, CSV or text (environment names only: no URLs and no component content).",
  ]),
  note("A state such as \"No value\" or \"No connection\" in Target is shown in orange even when Dev has the same, because it stops the component from working. Environment variable values and connections differ between environments by design, so they are not treated as differences.", "Good to know"),

  // 9
  h1("9. Fixing problems from the tool", false),
  p("When the tool can change something for you, a **Fix** button appears next to the finding:"),
  table(["Fix", "What it does", "Where"], [
    ["Remove unmanaged layer", "Removes the unmanaged (Active) customizations of a form, view, site map, web resource or process, then publishes where needed.", "Target"],
    ["Publish in Dev", "Publishes the component in Dev.", "Dev"],
    ["Add to Dev solution", "Adds the component to the unmanaged solution in Dev.", "Dev"],
    ["Turn on in Target", "Activates a flow or workflow.", "Target"],
    ["Enable step in Target", "Enables a disabled plug-in step.", "Target"],
  ], [2600, 4826, 1600]),
  spacer(),
  p("The tool does not remove the unmanaged layer of a ribbon, environment variable, connection reference or plug-in; for those it shows the manual steps."),
  h2("The \"Before you continue\" window"),
  p("Nothing changes on a direct click. A window first explains:"),
  ...bullets([
    "**Environment**: which environment will change.",
    "**Changes are made as**: the user the connection runs as (Dataverse shows it as *Modified by*).",
    "**What it will do** and **Can it be undone?**",
    "**Before you do this**: what to check first.",
    "**Prefer to do it by hand?**: the manual steps, with a copy button.",
  ]),
  ...image("guide-dd-4.png", 440, "The confirmation window (sample data)"),
  p("The **Make the change** button stays disabled until you tick \"I have read this\". Enter and Esc both mean Cancel."),

  // 10
  h1("10. Sharing the result: Export report", false),
  p("**Export report** (toolbar) saves the current result so anyone can read it:"),
  ...bullets([
    "**HTML report**: to email or attach to a ticket.",
    "**CSV**: opens in Excel.",
    "**Copy as text**: for Teams or chat.",
  ]),
  ...image("guide-dd-5.png", 400, "What may go into an HTML report"),
  h2("Privacy"),
  p("Reports always contain the verdict, next steps, all checks (including the *What was checked* lists and the unmanaged-layer details), solution layers and the list of differences. They never contain record data or credentials. Form XML, file content and environment URLs are left out unless you tick them. The compare report contains environment names only."),

  // 11
  h1("11. Tips and limitations", false),
  ...bullets([
    "**Connect Dev** whenever you can. Without it, the Dev checks (publish state, solution membership, comparison) are skipped, and Compare whole solution is not available.",
    "It compares the **published** content, not work in progress. When Dev has unpublished changes, the tool says so.",
    "**Not compared yet** (they are only counted by Compare whole solution): tables, columns, choices, security roles, model-driven apps and dashboards.",
    "It **cannot tell for sure who made an unmanaged change or when it was first added**: Dataverse keeps only the time of the last change of a layer.",
    "It **cannot remove** the unmanaged layer of a ribbon, environment variable, connection reference or plug-in.",
    "Solution layers for components other than forms, views, web resources and processes are reported as Dataverse gives them; if a type shows no layers, use *See solution layers* in the maker portal.",
    "If the Dev connection points to the same environment as Target, the tool asks you to connect the real source environment.",
  ]),

  // 12
  h1("12. Frequently asked questions", false),
  h2("Is it safe to run in Production?"),
  p("Diagnosing and comparing only read data. Nothing is changed unless you click a Fix button and confirm. Use a Fix in Production only at a quiet time and after reading the confirmation window."),
  h2("Do I need to connect Dev?"),
  p("Not to check one item, but it enables the most useful checks (publish state in Dev, solution membership in Dev, the Dev vs Target comparison). Compare whole solution needs it."),
  h2("Which account does a fix run as?"),
  p("The account of the XrmToolBox connection. The confirmation window shows it by name. It appears as *Modified by* in Dataverse."),
  h2("The verdict says \"Deployment worked\" but I still don't see the change."),
  p("Target's published content is identical to Dev. Follow the next step in the card: usually a hard refresh (Ctrl+F5), checking which form or view the user opens, the user's security role, or whether a flow ran after the import."),
  h2("Compare shows red for something I know is the same."),
  p("Click the row and read the differences: they are listed line by line. If a difference looks wrong, use **Diagnose this component** and send the report to feedback (section 13) so it can be fixed."),
  h2("Why isn't the environment variable's current value a difference?"),
  p("Current values are set per environment on purpose (for example a different URL in UAT). The tool shows them but only flags a variable that has no value at all."),
  h2("Does the report contain client data?"),
  p("No record data or credentials. Names of components and environments appear. Check the report before sharing it outside your team."),
  h2("What if a fix is refused?"),
  p("Dataverse checks the permission of the connected account. If it refuses, nothing changes, and the error is shown. Use the manual steps in the confirmation window instead."),

  // 13
  h1("13. Getting help and giving feedback", false),
  ...bullets([
    "Open an issue: github.com/ankitrana/XrmToolBox/issues (choose **Deployment Doctor**).",
    "How it works, for developers: DeploymentDoctor/docs/how-it-works.md in the same repository.",
    "Changes between versions: DeploymentDoctor/CHANGELOG.md.",
  ]),
  new Paragraph({
    spacing: { before: 480 },
    border: { top: { style: BorderStyle.SINGLE, size: 6, color: "D1D1D1", space: 8 } },
    children: [new TextRun({ text: "License: MIT. Free to use, provided as is without warranty. Try it in a non-production environment first.", size: 18, color: MUTED, italics: true })],
  }),
];

// ------------------------------------------------------------------ document
const doc = new Document({
  creator: "Ankit Rana",
  title: "Deployment Doctor - User Guide",
  description: "User guide for the Deployment Doctor XrmToolBox tool",
  styles: {
    default: { document: { run: { font: FONT, size: 21, color: DARK } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 34, bold: true, color: BRAND, font: FONT },
        paragraph: { spacing: { before: 360, after: 200 }, outlineLevel: 0, keepNext: true, keepLines: true } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 26, bold: true, color: DARK, font: FONT },
        paragraph: { spacing: { before: 280, after: 140 }, outlineLevel: 1, keepNext: true, keepLines: true } },
    ],
  },
  numbering: {
    config: [
      { reference: "bullets", levels: [{ level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT,
        style: { paragraph: { indent: { left: 400, hanging: 260 } } } }] },
      ...numberingConfigs,
    ],
  },
  sections: [
    {
      properties: { page: { size: { width: 11906, height: 16838 }, margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 } },
        titlePage: true },
      headers: {
        default: new Header({ children: [new Paragraph({
          border: { bottom: { style: BorderStyle.SINGLE, size: 4, color: "D1D1D1", space: 4 } },
          tabStops: [{ type: TabStopType.RIGHT, position: CONTENT }],
          children: [
            new TextRun({ text: "Deployment Doctor  ·  User Guide", size: 16, color: MUTED }),
            new TextRun({ text: `\tv${VERSION}`, size: 16, color: MUTED }),
          ] })] }),
        first: new Header({ children: [new Paragraph({ children: [] })] }),
      },
      footers: {
        default: new Footer({ children: [new Paragraph({
          tabStops: [{ type: TabStopType.RIGHT, position: CONTENT }],
          children: [
            new TextRun({ text: "Ankit Rana  ·  github.com/ankitrana", size: 16, color: MUTED }),
            new TextRun({ children: ["\tPage ", PageNumber.CURRENT, " of ", PageNumber.TOTAL_PAGES], size: 16, color: MUTED }),
          ] })] }),
        first: new Footer({ children: [new Paragraph({ children: [] })] }),
      },
      children: [...cover, ...toc, ...body],
    },
  ],
});

Packer.toBuffer(doc).then((buf) => {
  fs.writeFileSync(OUT, buf);
  console.log("Wrote", OUT, buf.length, "bytes");
});
