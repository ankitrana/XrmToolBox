using System.Collections.Generic;
using System.Linq;

namespace UnmanagedLayerFinder
{
    internal class ComponentTypeInfo
    {
        public string Label;            // shown in grid
        public List<string> LayerNames = new List<string>(); // msdyn_solutioncomponentname candidates, tried in order
        public string Table;            // backing table, if the component is a real record
        public string IdAttribute;
        public string NameAttribute;

        public bool SupportsLayers { get { return LayerNames.Count > 0; } }
    }

    /// <summary>
    /// Maps solutioncomponent.componenttype codes to the names the msdyn_componentlayer table expects,
    /// and to backing tables (used for names and the "modified by" lookup).
    /// Codes above 10000 (connection references, etc.) differ per org, so they are registered at runtime
    /// from the solutioncomponentdefinition table - see RegisterDynamic.
    /// </summary>
    internal static class ComponentTypes
    {
        private static ComponentTypeInfo T(string label, string layer, string table = null, string id = null, string name = null)
        {
            var info = new ComponentTypeInfo { Label = label, Table = table, IdAttribute = id, NameAttribute = name };
            if (layer != null) info.LayerNames.Add(layer);
            return info;
        }

        private static readonly Dictionary<int, ComponentTypeInfo> Fixed = new Dictionary<int, ComponentTypeInfo>
        {
            { 1,   T("Table",                 "Entity") },
            { 2,   T("Column",                "Attribute") },
            { 9,   T("Choice (OptionSet)",    "OptionSet") },
            { 10,  T("Relationship",          "EntityRelationship") },
            { 14,  T("Entity Key",            "EntityKey") },
            { 20,  T("Security Role",         "Role", "role", "roleid", "name") },
            { 26,  T("View",                  "SavedQuery", "savedquery", "savedqueryid", "name") },
            { 29,  T("Process",               "Workflow", "workflow", "workflowid", "name") },
            { 31,  T("Report",                "Report", "report", "reportid", "name") },
            { 59,  T("Chart",                 "SavedQueryVisualization", "savedqueryvisualization", "savedqueryvisualizationid", "name") },
            { 60,  T("Form",                  "SystemForm", "systemform", "formid", "name") },
            { 61,  T("Web Resource",          "WebResource", "webresource", "webresourceid", "name") },
            { 62,  T("Site Map",              "SiteMap", "sitemap", "sitemapid", "sitemapname") },
            { 66,  T("Custom Control",        "CustomControl") },
            { 80,  T("Model-driven App",      "AppModule", "appmodule", "appmoduleid", "name") },
            { 91,  T("Plug-in Assembly",      "PluginAssembly", "pluginassembly", "pluginassemblyid", "name") },
            { 92,  T("Plug-in Step",          "SdkMessageProcessingStep", "sdkmessageprocessingstep", "sdkmessageprocessingstepid", "name") },
            { 300, T("Canvas App",            "CanvasApp", "canvasapp", "canvasappid", "displayname") },
            { 371, T("Connector",             "Connector", "connector", "connectorid", "displayname") },
            { 380, T("Environment Variable Definition", "EnvironmentVariableDefinition", "environmentvariabledefinition", "environmentvariabledefinitionid", "schemaname") },
            { 381, T("Environment Variable Value",      "EnvironmentVariableValue", "environmentvariablevalue", "environmentvariablevalueid", "schemaname") },
        };

        private static readonly Dictionary<int, ComponentTypeInfo> Dynamic = new Dictionary<int, ComponentTypeInfo>();

        public static bool IsKnown(int code) { return Fixed.ContainsKey(code) || Dynamic.ContainsKey(code); }

        public static void ClearDynamic() { Dynamic.Clear(); }

        public static void RegisterDynamic(int code, ComponentTypeInfo info) { Dynamic[code] = info; }

        public static ComponentTypeInfo Get(int code)
        {
            ComponentTypeInfo info;
            if (Fixed.TryGetValue(code, out info)) return info;
            if (Dynamic.TryGetValue(code, out info)) return info;
            return T("Type " + code, null);
        }

        /// <summary>Process (workflow) category to a readable label.</summary>
        public static string ProcessLabel(int? category)
        {
            switch (category)
            {
                case 0: return "Classic Workflow";
                case 1: return "Dialog";
                case 2: return "Business Rule";
                case 3: return "Action";
                case 4: return "Business Process Flow";
                case 5: return "Cloud Flow";
                case 6: return "Desktop Flow";
                case 7: return "AI Flow";
                default: return "Process";
            }
        }

        public static IEnumerable<int> UnknownCodes(IEnumerable<int> codes)
        {
            return codes.Distinct().Where(c => !IsKnown(c));
        }
    }
}
