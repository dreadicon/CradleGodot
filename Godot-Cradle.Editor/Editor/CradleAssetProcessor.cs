using Cradle.Editor.Utils;
using Microsoft.CSharp;
using Nustache.Core;
using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using Godot.Collections;
using static Godot.GD;

namespace Cradle.Editor
{
	[Tool]
	public partial class CradleAssetProcessor: EditorImportPlugin
	{
		#region EditorImportPlugin implementation

		public override string _GetImporterName() => "Godot Cradle";
		public override string _GetVisibleName() => "Twine Story";
		public override string[] _GetRecognizedExtensions() => new[] { "twee","html" };
		public override string _GetSaveExtension() => "cs";  // TODO: might need to change this later.
		public override string _GetResourceType() => "Script"; // TODO: might need to change this later.
		
		public enum Presets
		{
			DEFAULT
		}

		public override int _GetPresetCount() => Enum.GetNames(typeof(Presets)).Length;
		public override string _GetPresetName(int presetIndex)
		{
			switch ((Presets)presetIndex)
			{
				case Presets.DEFAULT:
					return "Auto-detect";
				default:
					return "Unknown";
			}
		}

		public override float _GetPriority()
		{
			return 2f;
		}

		public override Array<Dictionary> _GetImportOptions(string path, int presetIndex)
		{
			switch ((Presets)presetIndex)
			{
				case Presets.DEFAULT:
					return new Array<Dictionary> {
						new() {
							{"name", "Automatic"},
							{"default", 0},
							{"hint_string", "TwineHtml=0,Twee=1"}
						}
					} ;
				default:
					return new Array<Dictionary>();
			}
		}

		public override bool _GetOptionVisibility(string path, StringName optionName, Dictionary options)
		{
			return true;
		}

		#endregion
		
		static Regex NameSanitizer = new(@"([^a-z0-9_]|^[0-9])", RegexOptions.IgnoreCase);
		static char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
		static System.Collections.Generic.Dictionary<string, Type> ImporterTypes = new(StringComparer.OrdinalIgnoreCase);

		public override Error _Import(
			string sourceFile, 
			string savePath, 
			Dictionary options, 
			Array<string> platformVariants, 
			Array<string> genFiles)
		{
			var assetPath = sourceFile;
			// ======================================
			// Choose the importer for this file type

			string ext = Path.GetExtension(assetPath).ToLower();
			if (string.IsNullOrEmpty(ext))
				return Error.InvalidData;

			// Get the right importer for this type
			ext = ext.Substring(1);
			StoryImporter importer = null;
			Type importerType;
			if (!ImporterTypes.TryGetValue(ext, out importerType))
				return Error.InvalidData;

			importer = (StoryImporter)Activator.CreateInstance(importerType);
			importer.AssetPath = assetPath;

			// ======================================
			// Validate the file

			// Check that the file is relevant
			if (!importer.IsAssetRelevant())
				return Error.InvalidData;

			// Check that the story name is valid
			string fileNameExt = Path.GetFileName(assetPath);
			string fileName = Path.GetFileNameWithoutExtension(assetPath);
			string storyName = NameSanitizer.Replace(fileName, string.Empty);
			if (storyName != fileName)
			{
				PushError($"The story cannot be imported because \"{fileName}\" is not a valid Unity script name.");
				return Error.InvalidData;
			}

			// ======================================
			// Initialize the importer - load data and choose transcoder

			try
			{
				importer.Initialize();
			}
			catch (StoryImportException ex)
			{
				PushError($"Story import failed: {ex.Message} ({fileNameExt})");
				return Error.InvalidData;
			}

			// ======================================
			// Run the transcoder

			try
			{
				importer.Transcode();
			}
			catch (StoryFormatTranscodeException ex)
			{
				PushError($"Story format transcoding failed at passage {ex.Passage}: {ex.Message} ({fileNameExt})");
				return Error.InvalidData;
			}

			// ======================================
			// Generate code

			StoryFormatMetadata storyFormatMetadata = importer.Metadata;
			TemplatePassageData[] passageData = importer.Passages.Select(p => new TemplatePassageData()
				{
					Pid = p.Pid,
					Name = p.Name.Replace("\"", "\"\""),
					Tags = p.Tags,
					Code = p.Code.Main.Split(new string[]{ System.Environment.NewLine }, StringSplitOptions.None),
					Fragments = p.Code.Fragments.Select((frag, i) => new TemplatePassageFragment()
						{
							Pid = p.Pid,
							FragId = i,
							Code = frag.Split(new string[]{ System.Environment.NewLine }, StringSplitOptions.None)
						}).ToArray()
				}).ToArray();

			// Get template file from this editor script's directory
			string output = Nustache.Core.Render.FileToString(
				EditorFileUtil.FindFile("Story.template"),
				new System.Collections.Generic.Dictionary<string, object>()
				{
					{"version", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()},
					{"originalFile", Path.GetFileName(assetPath)},
					{"storyFormatName", storyFormatMetadata.StoryFormatName},
					{"storyFormatNamespace", storyFormatMetadata.StoryBaseType.Namespace},
					{"storyFormatClass", storyFormatMetadata.StoryBaseType.FullName},
					{"storyName", storyName},
					{"startPassage", storyFormatMetadata.StartPassage ?? "Start"},
					{"vars", importer.Vars},
					{"macroLibs", importer.MacroLibs},
					{"strictMode", storyFormatMetadata.StrictMode ? "true" : "false"},
					{"passages", passageData}
				}
			);

			// ======================================
			// Compile
			
			// Detect syntax errors
			
			var compilerSettings = new CompilerParameters()
			{
				GenerateInMemory = true,
				GenerateExecutable = false
			};
			foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
				if (!string.IsNullOrEmpty(assembly.Location))
					compilerSettings.ReferencedAssemblies.Add(assembly.Location);

			var results = new CSharpCodeProvider().CompileAssemblyFromSource(compilerSettings, output);

			if (results.Errors.Count > 0)
			{
				int errorLineOffset = 4;

				bool errors = false;
				for (int i = 0; i < results.Errors.Count; i++)
				{
					CompilerError error = results.Errors[i];

					switch (error.ErrorNumber)
					{
						//case "CS0246":
						case "CS0103":
						case "":
							continue;
							
						// Single quotes instead of double quotes
						case "CS1012":
							error.ErrorText = "Strings must use \"double-quotes\", not 'single-quotes'.";
							break;

						// Double var accessor
						case "CS1612":
							error.ErrorText = "Can't set a nested property directly like that. Use a temporary variable in-between.";
							break;
					}

					// Error only if not a warning
					errors |= !error.IsWarning;

					try
					{
						// Get some compilation metadata - relies on the template using the #frag# token
						string[] errorDirective = error.FileName.Split(new string[] { "#frag#" }, StringSplitOptions.None);
						string errorPassage = errorDirective[0];
						int errorFragment = errorDirective.Length > 1 ? int.Parse(errorDirective[1]) : -1;
						TemplatePassageData passage = passageData.FirstOrDefault(p => p.Name == errorPassage);
						string lineCode = passage == null || error.Line < errorLineOffset ? "(code not available)" : errorFragment >= 0 ?
							passage.Fragments[errorFragment].Code[error.Line - errorLineOffset] :
							passage.Code[error.Line - errorLineOffset];

						if (error.IsWarning)
							PushWarning($"Story compilation warning at passage '{errorPassage}': '{error.ErrorText}'\n\n\t{lineCode}\n");
						else
							PushError($"Story compilation error at passage '{errorPassage}': {error.ErrorText}\n\n\t{lineCode}\n");
					}
					catch
					{
						if (error.IsWarning)
							PushWarning($"Story compilation warning: {error.ErrorText} \n");
						else
							PushError($"Story compilation error: {error.ErrorText}\n");
					}
				}

				if (errors)
				{
					PushError($"The story {Path.GetFileName(assetPath)} has some errors and can't be imported (see console for details).");
					return Error.InvalidData;
				}
			}

			// Remove custom line directives so they won't interfere with debugging the final script
			output = Regex.Replace(output, @"^\s*\#line\b.*$", string.Empty, RegexOptions.Multiline);

			// Passed syntax check, save to file
			string csFile = Path.Combine(savePath, Path.GetFileNameWithoutExtension(assetPath) + ".cs");
			File.WriteAllText(csFile, output, Encoding.UTF8);
			

			// ======================================
			// Organize the assets

			#region Disabled prefab creation because the story class can't be added during this asset import
			/*
			// Find the story class
			string projectDir = Directory.GetParent((Path.GetFullPath(Application.dataPath))).FullName;
			Type storyClass = null;
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				// Skip references external to the project
				if (!string.IsNullOrEmpty(assembly.Location) && !Path.GetFullPath(assembly.Location).StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
					continue;
				foreach (Type type in assembly.GetTypes())
				{
					if (type.Name == storyName)
					{
						storyClass = type;
						break;
					}
				}

				if (storyClass != null)
					break;
			}

			if (storyClass == null)
			{
				Debug.LogWarning("UnityTwine successfully imported the story, but a prefab couldn't be made for you. Sorry :(");
				continue;
			}

			// Create a prefab
			var prefab = new GameObject();
			prefab.name = storyName;
			prefab.AddComponent(storyClass);

			PrefabUtility.CreatePrefab(Path.Combine(Path.GetDirectoryName(assetPath), Path.GetFileNameWithoutExtension(assetPath) + ".prefab"), prefab, ReplacePrefabOptions.Default);
			*/
			#endregion

			// ======================================

			return Error.Ok;
		}


		public class TemplatePassageData : PassageData
		{
			public new string[] Code;
			public TemplatePassageFragment[] Fragments;

			public string DirectiveName
			{
				get
				{

					string corrected = String.Join("_", this.Name.Split(InvalidFileNameChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
					return corrected;
				}
			}
		}

		public class TemplatePassageFragment
		{
			public string Pid;
			public int FragId;
			public string[] Code;
		}

		public static void RegisterImporter<T>(string extenstion) where T : StoryImporter, new()
		{
			ImporterTypes[extenstion] = typeof(T);
		}
	}
}
