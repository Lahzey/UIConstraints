using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using UIConstraints.Core.Parsing;
using UnityEditor;
using UnityEngine;

// Copied from https://forum.unity.com/threads/textarea-font-color.41325/, adjusted to be a PropertyDrawer
namespace UIConstraints.Unity.Inspector {
[CustomPropertyDrawer(typeof(CodeInput))]
public class CodeInputDrawer : PropertyDrawer {
	private static readonly GUIContent USINGS_LABEL = new GUIContent("Usings", "Namespaces whose types can be used by just their name, separated by ';'. Aliases can be added as Alias=Full.Type.Name, e.g. to resolve name conflicts. Choosing a type from another namespace in the suggestions adds what it needs automatically.");
	private const float BOX_PADDING = 6;
	private const float BOX_GAP = 6; // between the boxes of consecutive inputs
	private float lastTitleHeight;
	private Vector2 lastPreferredSize;

	private ParseResult parseResult;
	private readonly TransitionSection transitionSection = new TransitionSection();

	// using entries that would fix each error of the parse result (empty for errors that are not caused by an unresolved identifier)
	private ParseResult suggestionsParseResult;
	private readonly List<List<string>> errorSuggestions = new List<List<string>>();

	private static float UsingsHeight => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

	public static GUIContent UsingsLabel => USINGS_LABEL;

	// each input is a box with its name as the title, which separates it from the inputs around it
	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
		EditorGUI.BeginProperty(position, label, property);
		label = GetLabel(label);

		Rect box = new Rect(position.x, position.y, position.width, position.height - BOX_GAP);
		if (Event.current.type == EventType.Repaint) EditorStyles.helpBox.Draw(box, false, false, false, false); // not GUI.Box, as that would allocate a control ID in repaint events only
		position = new Rect(box.x + BOX_PADDING, box.y + BOX_PADDING, box.width - BOX_PADDING * 2, box.height - BOX_PADDING * 2);

		// the title, with the button that turns the animation of the value on (for inputs that can be animated, see TransitionSection)
		lastTitleHeight = EditorStyles.boldLabel.CalcSize(label).y + EditorGUIUtility.standardVerticalSpacing;
		Rect titleRect = new Rect(position.x, position.y, position.width, lastTitleHeight);
		SerializedProperty transitionProperty = property.FindPropertyRelative("transition");
		Type animatedType = TransitionSection.GetAnimatedType(property, fieldInfo);
		if (animatedType != null) titleRect = TransitionSection.DrawHeader(titleRect, transitionProperty, animatedType);
		GUI.Label(titleRect, label, EditorStyles.boldLabel);
		position.y += lastTitleHeight;
		position.height -= lastTitleHeight;

		// the usings that can be added to: those of the component if it shares them (see ISharedUsings, its inspector shows them), otherwise those of the input, edited as one line of text
		SerializedProperty ownUsingsProperty = property.FindPropertyRelative("usings");
		SerializedProperty sharedUsingsProperty = GetSharedUsingsProperty(property);
		SerializedProperty usingsProperty = sharedUsingsProperty ?? ownUsingsProperty;
		List<string> usings = ReadUsings(usingsProperty);
		if (sharedUsingsProperty == null) {
			usings = UsingsField(new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight), usings);
			position.y += UsingsHeight;
			position.height -= UsingsHeight;
		}

		// what the input is parsed with, like at runtime (see CodeInputs): the variables available at the component, and what the field expects
		Component owner = property.serializedObject.targetObject as Component;
		IReadOnlyDictionary<string, Type> variables = CodeInputs.GetVariables(fieldInfo, owner != null ? VariableScopes.GetAvailableVariables(owner) : new Dictionary<string, Type>());
		ParseTarget target = CodeInputs.GetTarget(fieldInfo);
		Type resultType = CodeInputs.GetResultType(fieldInfo);

		// code input, accepting a suggestion may add to the usings
		float errorsHeight = GetErrorsHeight();
		float transitionHeight = animatedType != null ? transitionSection.GetHeight(transitionProperty) : 0;
		Rect codeAreaPosition = new Rect(position.x, position.y, position.width, position.height - errorsHeight - transitionHeight);
		SerializedProperty sourceProperty = property.FindPropertyRelative("input");
		sourceProperty.stringValue = CodeArea.ScrolledArea(codeAreaPosition, sourceProperty.stringValue, usings, variables, target, parseResult, GUI.skin.label, out lastPreferredSize);

		// errors below the code input, with buttons to add usings that would fix them
		DrawErrors(new Rect(position.x, codeAreaPosition.yMax, position.width, errorsHeight), usings);

		// the animation settings, if they are expanded
		IEnumerable<string> allUsings = sharedUsingsProperty != null ? usings.Concat(ReadUsings(ownUsingsProperty)).ToList() : usings;
		if (animatedType != null) transitionSection.Draw(new Rect(position.x, codeAreaPosition.yMax + errorsHeight, position.width, transitionHeight), transitionProperty, animatedType, usings, allUsings, variables, resultType);
		WriteUsings(usingsProperty, usings);

		// parse current input, with the input's own usings too (see CodeInputs.GetUsings)
		ParseContext context = new ParseContext(allUsings, variables);
		string source = sourceProperty.stringValue;
		if (parseResult == null || source != parseResult.source || context.key != parseResult.context.key) parseResult = ParseCache.GetParseResult(source, context, target, resultType) ?? parseResult; // will return null if that source has not been parsed yet, in which case we will stick with the old parse result until the new one is ready

		EditorGUI.EndProperty();
	}

	// the usings of the component, if it shares them with all its inputs
	[CanBeNull]
	private static SerializedProperty GetSharedUsingsProperty(SerializedProperty property) {
		return property.serializedObject.targetObject is ISharedUsings ? property.serializedObject.FindProperty("usings") : null;
	}

	// usings edited as one line of text, separated by ';'
	public static List<string> UsingsField(Rect position, List<string> usings) {
		string usingsText = string.Join("; ", usings);
		string newUsingsText = EditorGUI.DelayedTextField(position, USINGS_LABEL, usingsText);
		if (newUsingsText == usingsText) return usings;
		return newUsingsText.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).Where(u => u.Length > 0).Distinct().ToList();
	}

	// actions show the variable they get, e.g. "On Value Changed (value: float)"
	private GUIContent GetLabel(GUIContent label) {
		ActionAttribute action = fieldInfo.GetCustomAttribute<ActionAttribute>();
		if (action == null) return label;

		string tooltip = "Executed when the event is raised: method calls and assignments, separated by ';'";
		if (action.parameterName == null) return new GUIContent(label.text, tooltip);
		return new GUIContent($"{label.text} ({action.parameterName}: {TypeNames.GetDisplayName(action.parameterType)})", $"{tooltip}. The variable '{action.parameterName}' is the value of the event.");
	}

	public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
		float usingsHeight = GetSharedUsingsProperty(property) == null ? UsingsHeight : 0;
		float transitionHeight = TransitionSection.GetAnimatedType(property, fieldInfo) != null ? transitionSection.GetHeight(property.FindPropertyRelative("transition")) : 0;
		return BOX_PADDING * 2 + lastTitleHeight + usingsHeight + lastPreferredSize.y + GetErrorsHeight() + transitionHeight + BOX_GAP;
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

	public static List<string> ReadUsings(SerializedProperty usingsProperty) {
		List<string> usings = new List<string>();
		for (int i = 0; i < usingsProperty.arraySize; i++) usings.Add(usingsProperty.GetArrayElementAtIndex(i).stringValue);
		return usings;
	}

	public static void WriteUsings(SerializedProperty usingsProperty, List<string> usings) {
		if (ReadUsings(usingsProperty).SequenceEqual(usings)) return; // don't touch the property if nothing changed

		usingsProperty.arraySize = usings.Count;
		for (int i = 0; i < usings.Count; i++) usingsProperty.GetArrayElementAtIndex(i).stringValue = usings[i];
	}
}
}
