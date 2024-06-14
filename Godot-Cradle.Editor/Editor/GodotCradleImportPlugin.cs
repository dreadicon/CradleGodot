using Cradle.Editor.Importers;
using Cradle.Editor.StoryFormats.Harlowe;
using Godot;

namespace Cradle.Editor;

[Tool]
public partial class GodotCradleImportPlugin : EditorPlugin
{
    CradleAssetProcessor cradleAssetProcessor;
    TwineHtmlImporter twineHtmlImporter;
    HarloweTranscoder harloweTranscoder;

    public override void _Ready()
    {
        base._Ready();
        cradleAssetProcessor = new CradleAssetProcessor();
        twineHtmlImporter = new TwineHtmlImporter();
        twineHtmlImporter.OnCreate();
        harloweTranscoder = new HarloweTranscoder();
        harloweTranscoder.OnCreate();
        AddImportPlugin(cradleAssetProcessor);
    }
    
    public override void _ExitTree()
    {
        base._ExitTree();
        RemoveImportPlugin(cradleAssetProcessor);
        cradleAssetProcessor = null;
        twineHtmlImporter.QueueFree();
        twineHtmlImporter = null;
    }
}