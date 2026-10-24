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
	private static readonly GUIContent USINGS_LABEL = new GUIContent("Usings", "Namespaces whose types can be used by just their name, separated by ';'. Aliases can be added as Alias=Full.Type.Name, e.g. to resolve name conflicts. Choosing a type from another namespace in the suggestions adds what it needs automatically.");
	private float lastLabelHeight;
	private Vector2 lastPreferredSize;

	private ParseResult parseResult;

	// using entries that would fix each error of the parse result (empty for errors that are not caused by an unresolved identifier)
	private ParseResult suggestionsParseResult;
	private readonly List<List<string>> errorSuggestions = new List<List<string>>();

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
		float errorsHeight = GetErrorsHeight();
		Rect codeAreaPosition = new Rect(position.x, position.y, position.width, position.height - errorsHeight);
		SerializedProperty sourceProperty = property.FindPropertyRelative("input");
		sourceProperty.stringValue = CodeArea.ScrolledArea(codeAreaPosition, sourceProperty.stringValue, usings, parseResult, GUI.skin.label, out lastPreferredSize);

		// errors below the code input, with buttons to add usings that would fix them
		DrawErrors(new Rect(position.x, codeAreaPosition.yMax, position.width, errorsHeight), usings);
		WriteUsings(usingsProperty, usings);

		// parse current input
		ParseContext context = new ParseContext(usings);
		string source = sourceProperty.stringValue;
		if (parseResult == null || source != parseResult.source || context.key != parseResult.context.key) parseResult = ParseCache.GetParseResult(source, context) ?? parseResult; // will return null if that source has not been parsed yet, in which case we will stick with the old parse result until the new one is ready

		EditorGUI.EndProperty();
	}

	public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
		return lastPreferredSize.y + lastLabelHeight + UsingsHeight + GetErrorsHeight();
	}

	// one line per error, plus one line for the buttons of errors with suggestions
	private float GetErrorsHeight() {
		ParseException exception = parseResult?.Exception;
		if (exception == null) return 0;

		UpdateErrorSuggestions();
		int lines = exception.MessageCount() + errorSuggestions.Count(s => s.Count > 0);
		return lines * EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
	}

	private void UpdateErrorSuggestions() {
		if (suggestionsParseResult == parseResult) return;
		suggestionsParseResult = parseResult;

		errorSuggestions.Clear();
		ParseException exception = parseResult?.Exception;
		if (exception == null) return;
		for (int i = 0; i < exception.MessageCount(); i++) {
			string unresolvedIdentifier = exception.GetUnresolvedIdentifier(i);
			errorSuggestions.Add(unresolvedIdentifier == null ? new List<string>() : TypeResolver.SuggestUsings(unresolvedIdentifier, parseResult.context.namespaces));
		}
	}

	private void DrawErrors(Rect position, List<string> usings) {
		ParseException exception = parseResult?.Exception;
		if (exception == null) return;

		UpdateErrorSuggestions();
		GUIStyle messageStyle = new GUIStyle(EditorStyles.miniLabel);
		messageStyle.normal.textColor = (EditorGUIUtility.isProSkin ? CodeAreaTheme.DEFAULT_DARK : CodeAreaTheme.DEFAULT_LIGHT).ErrorTextColor;
		float lineHeight = EditorGUIUtility.singleLineHeight;
		float y = position.y + EditorGUIUtility.standardVerticalSpacing;
		for (int i = 0; i < exception.MessageCount(); i++) {
			GUI.Label(new Rect(position.x, y, position.width, lineHeight), exception.GetMessage(i), messageStyle);
			y += lineHeight;

			List<string> suggestions = errorSuggestions[i];
			if (suggestions.Count == 0) continue;

			float x = position.x;
			foreach (string suggestion in suggestions) {
				GUIContent buttonContent = new GUIContent("using " + suggestion, "Add '" + suggestion + "' to the usings");
				float buttonWidth = EditorStyles.miniButton.CalcSize(buttonContent).x;
				if (GUI.Button(new Rect(x, y, buttonWidth, lineHeight), buttonContent, EditorStyles.miniButton) && !usings.Contains(suggestion)) usings.Add(suggestion);
				x += buttonWidth + 2;
			}

			y += lineHeight;
		}
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
