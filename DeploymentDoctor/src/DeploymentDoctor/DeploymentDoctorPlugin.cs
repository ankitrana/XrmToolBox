using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace DeploymentDoctor
{
    [Export(typeof(IXrmToolBoxPlugin)),
     ExportMetadata("Name", "Deployment Doctor"),
     ExportMetadata("Description", "Find out why a change deployed with a managed solution does not show in the target environment (forms, views, web resources, workflows, business rules and cloud flows) and fix it safely. Created by Ankit Rana."),
     ExportMetadata("SmallImageBase64", PluginIcons.Small),
     ExportMetadata("BigImageBase64", PluginIcons.Big),
     ExportMetadata("BackgroundColor", "Honeydew"),
     ExportMetadata("PrimaryFontColor", "Black"),
     ExportMetadata("SecondaryFontColor", "DarkGray")]
    public class DeploymentDoctorPlugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl()
        {
            return new DeploymentDoctorControl();
        }
    }
}
