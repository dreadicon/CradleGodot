using System.Collections;
using Cradle;
using Godot;
using System.Collections.Generic;
using System.Runtime.Serialization;

#if TOOLS


/// <summary>
/// A Godot custom window/dock that is used to show the current state of a story.
/// </summary>
[Tool]
public partial class StoryInspector : EditorInspectorPlugin
{
	
	public override bool _CanHandle(GodotObject @object)
	{
		return @object is Story;
	}

	public override void _ParseBegin(GodotObject @object)
	{
		Story story = @object as Story;
		if (story == null || story.Output == null)
			return;

		// Separator
		AddSeparator();

		// Story State
		AddProperty("Story State", story.State.ToString());

		// Current Passage
		AddProperty("Current Passage", story.CurrentPassage == null ? "(none)" : story.CurrentPassage.Name);

		// Separator
		AddSeparator();

		// Indentation and output
		int defaultIndent = 0;
		for (int i = 0; i < story.Output.Count; i++)
		{
			StoryOutput output = story.Output[i];
			if (output is Embed)
				continue;

			int groupCount = 0;
			OutputGroup group = output.Group;
			while (group != null)
			{
				groupCount++;
				group = group.Group;
			}

			string indent = new string('\t', defaultIndent + groupCount);
			AddProperty($"{indent}{output.ToString()}", null);
		}
	}

	private void AddProperty(string name, string value)
	{
		var label = new Label
		{
			Text = name
		};
		AddCustomControl(label);

		if (value != null)
		{
			var valueLabel = new Label
			{
				Text = value
			};
			AddCustomControl(valueLabel);
		}
	}

	private void AddSeparator()
	{
		var separator = new HSeparator();
		AddCustomControl(separator);
	}
}
#endif


/// <summary>
/// A Unity custom inspector that is used to display the current state of a story.
/// </summary>
/*[CustomEditor(typeof(Story), true)]
public class StoryInspector : Editor
{
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();

		var story = target as Story;
		if (story == null || story.Output == null)
			return;

		EditorGUILayout.Separator();

		EditorGUILayout.LabelField("Story State", story.State.ToString());
		EditorGUILayout.LabelField("Current Passage", story.CurrentPassage == null ? "(none)" : story.CurrentPassage.Name);

		EditorGUILayout.Separator();

		int defaultIndent = EditorGUI.indentLevel;

		for(int i = 0; i < story.Output.Count; i++)
		{
			StoryOutput output = story.Output[i];

			if (output is Embed)
				continue;

			int groupCount = 0;
			OutputGroup group = output.Group;
			while (group != null)
			{
				groupCount++;
				group = group.Group;
			}
			EditorGUI.indentLevel = defaultIndent + groupCount;
			EditorGUILayout.LabelField(output.ToString());
		}
		
		EditorGUI.indentLevel = defaultIndent;
	}
}*/
