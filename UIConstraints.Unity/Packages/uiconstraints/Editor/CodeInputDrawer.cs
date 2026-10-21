using System;
using System.Collections.Generic;
using System.Linq;
using UIConstraints.Core.Parsing;
using UnityEditor;
using UnityEngine;

// Copied from https://forum.unity.com/threads/textarea-font-color.41325/, adjusted to be a PropertyDrawer
namespace UIConstraints.Unity.Inspector {
[CustomPropertyDrawer(typeof(CodeInput))]
public class CodeInputDrawer : PropertyDrawer {
	private static readonly float MAX_INPUT_HEIGHT = GUI.skin.textArea.lineHeight * 5;
	private static readonly float BOTTOM_INPUT_PADDING = 4;
	private static readonly GUIContent USINGS_LABEL = new GUIContent("Usings", "Namespaces whose types can be used by just their name, separated by ';'. Choosing a type from another namespace in the suggestions adds its namespace automatically.");
	private float lastLabelHeight;
	private Vector2 lastPreferredSize;

	private ParseResult parseResult;

	private static float UsingsHeight => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
		EditorGUI.BeginProperty(position, label, property);

		lastLabelHeight = GUI.skin.label.CalcSize(label).y;
		GUI.Label(new Rect(position.position, new Vector2(position.width, lastLabelHeight)), label);
		position.y += lastLabelHeight;
		position.height -= lastLabelHeight;

		// usings, edited as one line of text
		SerializedProperty usingsProperty = property.FindPropertyRelative("usings");
		List<string> usings = ReadUsings(usingsProperty);
		string usingsText = string.Join("; ", usings);
		string newUsingsText = EditorGUI.DelayedTextField(new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight), USINGS_LABEL, usingsText);
		if (newUsingsText != usingsText) usings = newUsingsText.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).Where(u => u.Length > 0).Distinct().ToList();
		position.y += UsingsHeight;
		position.height -= UsingsHeight;

		// code input, accepting a suggestion may add to the usings
		SerializedProperty sourceProperty = property.FindPropertyRelative("input");
		sourceProperty.stringValue = CodeArea.ScrolledArea(position, sourceProperty.stringValue, usings, parseResult, GUI.skin.label, out lastPreferredSize);
		WriteUsings(usingsProperty, usings);

		// parse current input
		ParseContext context = new ParseContext(usings);
		string source = sourceProperty.stringValue;
		if (parseResult == null || source != parseResult.source || context.key != parseResult.context.key) parseResult = ParseCache.GetParseResult(source, context) ?? parseResult; // will return null if that source has not been parsed yet, in which case we will stick with the old parse result until the new one is ready

		EditorGUI.EndProperty();
	}

	public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
		return lastPreferredSize.y + lastLabelHeight + UsingsHeight;
	}

	private static List<string> ReadUsings(SerializedProperty usingsProperty) {
		List<string> usings = new List<string>();
		for (int i = 0; i < usingsProperty.arraySize; i++) usings.Add(usingsProperty.GetArrayElementAtIndex(i).stringValue);
		return usings;
	}

	private static void WriteUsings(SerializedProperty usingsProperty, List<string> usings) {
		if (ReadUsings(usingsProperty).SequenceEqual(usings)) return; // don't touch the property if nothing changed

		usingsProperty.arraySize = usings.Count;
		for (int i = 0; i < usings.Count; i++) usingsProperty.GetArrayElementAtIndex(i).stringValue = usings[i];
	}

	private static string PrintTextureColors(Texture2D texture2D) {
		// get each unique color in the texture and append it to a string
		if (texture2D == null) return "null";
		Color[] colors = texture2D.GetPixels();
		List<Color> uniqueColors = new List<Color>();
		foreach (Color color in colors)
			if (!uniqueColors.Contains(color))
				uniqueColors.Add(color);
		return string.Join(", ", colors);
	}
}
}
