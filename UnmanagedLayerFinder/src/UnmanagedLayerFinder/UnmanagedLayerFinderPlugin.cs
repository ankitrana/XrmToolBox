using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace UnmanagedLayerFinder
{
    [Export(typeof(IXrmToolBoxPlugin)),
     ExportMetadata("Name", "Unmanaged Layer Finder"),
     ExportMetadata("Description", "Find unmanaged (Active) layers on solution components, with when and who. Created by Ankit Rana."),
     ExportMetadata("SmallImageBase64", PluginIcons.Small),
     ExportMetadata("BigImageBase64", PluginIcons.Big),
     ExportMetadata("BackgroundColor", "Lavender"),
     ExportMetadata("PrimaryFontColor", "Black"),
     ExportMetadata("SecondaryFontColor", "DarkGray")]
    public class UnmanagedLayerFinderPlugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl()
        {
            return new UnmanagedLayerFinderControl();
        }
    }
}
