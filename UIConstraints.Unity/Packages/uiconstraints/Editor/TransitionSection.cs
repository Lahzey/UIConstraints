using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using UIConstraints.Core.Animation;
using UIConstraints.Core.Parsing;
using UIConstraints.Unity.Animation;
using UnityEditor;
using UnityEngine;
using Component = UnityEngine.Component;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace UIConstraints.Unity.Inspector {
/**
 * The animation settings of a code input (see CodeInput.transition): a button in the title row of the input that turns it on, a summary of the settings while they are collapsed,
 * and the settings themselves while expanded. Inputs without animation only show the button, so it stays out of the way until it is needed.
 */
public class TransitionSection {
	private const string ICON_PATH = "Packages/com.lahzey.uiconstraints/Editor/Icons/transition.png";
	private const float BUTTON_SIZE = 16;

	// the kinds new transitions start as, the first that can animate the value is used
	private static readonly Type[] DEFAULT_KINDS = { typeof(InterpolateTransition), typeof(NumbersInTextTransition), typeof(CrossfadeTransition), typeof(UnionFadeTransition), typeof(EdgeRevealTransition), typeof(DissolveTransition), typeof(SwitchTransition) };

	// the settings shown first, the settings of the kind (and of custom transitions) follow, then the rest in this order
	private static readonly string[] LEADING_SETTINGS = { "duration", "fullDurationFrom", "curve" };
	private static readonly string[] TRAILING_SETTINGS = { "delay", "animateWhen", "useScaledTime", "animateFrom" };

	private static Texture2D icon;
	private static readonly Dictionary<Type, List<Type>> KINDS_BY_VALUE_TYPE = new Dictionary<Type, List<Type>>(); // the inspector asks for them on every repaint

	// the 'animate from' expression, drawn like the input itself
	private ParseResult animateFromParseResult;
	private Vector2 animateFromSize;

	private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

	// the type of the animated value of a field, null if it can't be animated (see IAnimatedInputs)
	[CanBeNull]
	public static Type GetAnimatedType(SerializedProperty property, FieldInfo field) {
		return property.serializedObject.targetObject is IAnimatedInputs animatedInputs && !property.serializedObject.isEditingMultipleObjects ? animatedInputs.GetAnimatedType(field) : null;
	}

	// the transitions that can animate values of the type: all non-abstract ones, including those of other assemblies (e.g. custom ones)
	public static List<Type> GetKinds(Type valueType) {
		if (KINDS_BY_VALUE_TYPE.TryGetValue(valueType, out List<Type> kinds)) return kinds.ToList();

		UnityBlending.EnsureRegistered();
		kinds = TypeCache.GetTypesDerivedFrom<Transition>()
			.Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && t.GetConstructor(Type.EmptyTypes) != null && t.IsSerializable)
			.Where(t => CreateTransition(t).CanAnimate(valueType))
			.OrderBy(t => Array.IndexOf(DEFAULT_KINDS, t) is var index && index >= 0 ? index : DEFAULT_KINDS.Length)
			.ThenBy(Transition.GetDisplayName)
			.ToList();
		KINDS_BY_VALUE_TYPE[valueType] = kinds;
		return kinds.ToList();
	}

	private static Transition CreateTransition(Type type) => (Transition)Activator.CreateInstance(type);

	// the button that turns the animation on, and the summary of its settings while they are collapsed. Returns the rest of the title row
	public static Rect DrawHeader(Rect titleRow, SerializedProperty transitionProperty, Type valueType) {
		Transition transition = transitionProperty.managedReferenceValue as Transition;
		bool expanded = transition != null && transitionProperty.isExpanded;

		Rect buttonRect = new Rect(titleRow.xMax - BUTTON_SIZE, titleRow.y + (titleRow.height - BUTTON_SIZE) / 2, BUTTON_SIZE, BUTTON_SIZE);
		if (icon == null) icon = AssetDatabase.LoadAssetAtPath<Texture2D>(ICON_PATH);
		string tooltip = transition == null ? "Animate changes of this value" : expanded ? "Collapse the animation settings" : "Show the animation settings";
		Color oldColor = GUI.color;
		if (transition == null) GUI.color = new Color(1, 1, 1, 0.45f); // dimmed while off
		if (GUI.Button(buttonRect, new GUIContent(icon, tooltip), GUIStyle.none)) {
			if (transition == null) {
				Type kind = GetKinds(valueType).FirstOrDefault();
				if (kind != null) {
					transitionProperty.managedReferenceValue = CreateTransition(kind);
					transitionProperty.isExpanded = true;
				}
			}
			else {
				transitionProperty.isExpanded = !transitionProperty.isExpanded;
			}
		}

		GUI.color = oldColor;
		Rect rest = new Rect(titleRow.x, titleRow.y, buttonRect.x - titleRow.x - 4, titleRow.height);
		if (transition == null || expanded) return rest;

		// the summary, right aligned next to the button
		GUIContent summary = new GUIContent(transition.GetSummary(), "The animation of the value, click the button to change it");
		float width = Mathf.Min(EditorStyles.miniLabel.CalcSize(summary).x, rest.width * 0.6f);
		GUI.Label(new Rect(rest.xMax - width, rest.y, width, rest.height), summary, EditorStyles.miniLabel);
		return new Rect(rest.x, rest.y, rest.width - width - 4, rest.height);
	}

	public float GetHeight(SerializedProperty transitionProperty) {
		if (transitionProperty.managedReferenceValue == null || !transitionProperty.isExpanded) return 0;

		float height = 6 + Line; // separator and kind
		foreach (SerializedProperty setting in GetSettings(transitionProperty))
			height += setting.name == "animateFrom" ? GetAnimateFromHeight() : EditorGUI.GetPropertyHeight(setting, true) + EditorGUIUtility.standardVerticalSpacing;
		return height;
	}

	/**
	 * The settings, while they are expanded. usings, variables and resultType: what the input is parsed with, which 'animate from' is parsed with too.
	 * usings may be added to by accepting suggestions in 'animate from'.
	 */
	public void Draw(Rect position, SerializedProperty transitionProperty, Type valueType, List<string> usings, IEnumerable<string> allUsings, IReadOnlyDictionary<string, Type> variables, [CanBeNull] Type resultType) {
		Transition transition = transitionProperty.managedReferenceValue as Transition;
		if (transition == null || !transitionProperty.isExpanded) return;

		// a separator from the input
		if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(position.x, position.y + 2, position.width, 1), new Color(0.5f, 0.5f, 0.5f, 0.35f));
		position.yMin += 6;

		// the kind, or none to turn the animation off
		List<Type> kinds = GetKinds(valueType);
		if (!kinds.Contains(transition.GetType())) kinds.Insert(0, transition.GetType()); // e.g. if the type of the value changed, so it is not replaced silently
		GUIContent[] options = kinds.Select(k => new GUIContent(Transition.GetDisplayName(k), k.GetCustomAttribute<DescriptionAttribute>()?.Description)).Append(new GUIContent("None", "Changes are shown right away")).ToArray();
		int selected = kinds.IndexOf(transition.GetType());
		Rect kindRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
		int newSelected = EditorGUI.Popup(kindRect, new GUIContent("Transition", transition.GetType().GetCustomAttribute<DescriptionAttribute>()?.Description), selected, options);
		position.yMin += Line;
		if (newSelected != selected) {
			transitionProperty.managedReferenceValue = newSelected < kinds.Count ? ChangeKind(transition, kinds[newSelected]) : null;
			return; // the settings of the new kind are drawn next time
		}

		if (!transition.CanAnimate(valueType)) {
			EditorGUI.HelpBox(new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight), $"Can't animate {TypeNames.GetDisplayName(valueType)}, choose another transition", MessageType.Error);
			position.yMin += Line;
		}

		foreach (SerializedProperty setting in GetSettings(transitionProperty)) {
			GUIContent label = new GUIContent(setting.displayName, GetDescription(transition.GetType(), setting.name));
			if (setting.name == "animateFrom") {
				DrawAnimateFrom(new Rect(position.x, position.y, position.width, GetAnimateFromHeight()), setting, label, usings, allUsings, variables, resultType);
				position.yMin += GetAnimateFromHeight();
				continue;
			}

			float height = EditorGUI.GetPropertyHeight(setting, true);
			EditorGUI.PropertyField(new Rect(position.x, position.y, position.width, height), setting, label, true);
			position.yMin += height + EditorGUIUtility.standardVerticalSpacing;
		}
	}

	// a new transition of another kind, which keeps the settings both kinds have (e.g. the duration and curve)
	private static Transition ChangeKind(Transition old, Type kind) {
		Transition transition = CreateTransition(kind);
		foreach (FieldInfo field in kind.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
			FieldInfo oldField = old.GetType().GetField(field.Name, BindingFlags.Public | BindingFlags.Instance);
			if (oldField != null && oldField.FieldType == field.FieldType && oldField.DeclaringType == field.DeclaringType) field.SetValue(transition, oldField.GetValue(old));
		}

		return transition;
	}

	// the settings of the transition, in the order they are shown (see LEADING_SETTINGS and TRAILING_SETTINGS)
	private static List<SerializedProperty> GetSettings(SerializedProperty transitionProperty) {
		List<SerializedProperty> settings = new List<SerializedProperty>();
		SerializedProperty setting = transitionProperty.Copy();
		SerializedProperty end = setting.GetEndProperty();
		for (bool enterChildren = true; setting.NextVisible(enterChildren) && !SerializedProperty.EqualContents(setting, end); enterChildren = false) settings.Add(setting.Copy());

		int Order(SerializedProperty p) {
			int leading = Array.IndexOf(LEADING_SETTINGS, p.name);
			if (leading >= 0) return leading;
			int trailing = Array.IndexOf(TRAILING_SETTINGS, p.name);
			return trailing >= 0 ? 1000 + trailing : LEADING_SETTINGS.Length;
		}

		return settings.Select((p, index) => (p, index)).OrderBy(s => Order(s.p)).ThenBy(s => s.index).Select(s => s.p).ToList();
	}

	[CanBeNull]
	private static string GetDescription(Type transitionType, string fieldName) {
		return transitionType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)?.GetCustomAttribute<DescriptionAttribute>()?.Description;
	}

	private float GetAnimateFromHeight() {
		int errors = animateFromParseResult?.Exception?.MessageCount() ?? 0;
		return EditorGUIUtility.singleLineHeight + Mathf.Max(animateFromSize.y, EditorGUIUtility.singleLineHeight) + errors * EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2;
	}

	// an expression like the input itself, parsed with the same usings, variables and result type
	private void DrawAnimateFrom(Rect position, SerializedProperty setting, GUIContent label, List<string> usings, IEnumerable<string> allUsings, IReadOnlyDictionary<string, Type> variables, [CanBeNull] Type resultType) {
		float line = EditorGUIUtility.singleLineHeight;
		GUI.Label(new Rect(position.x, position.y, position.width, line), label);
		Rect area = new Rect(position.x, position.y + line, position.width, Mathf.Max(animateFromSize.y, line));
		setting.stringValue = CodeArea.ScrolledArea(area, setting.stringValue, usings, variables, ParseTarget.Value, animateFromParseResult, GUI.skin.label, out animateFromSize);

		ParseException exception = animateFromParseResult?.Exception;
		if (exception != null) {
			GUIStyle messageStyle = new GUIStyle(EditorStyles.miniLabel);
			messageStyle.normal.textColor = (EditorGUIUtility.isProSkin ? CodeAreaTheme.DEFAULT_DARK : CodeAreaTheme.DEFAULT_LIGHT).ErrorTextColor;
			for (int i = 0; i < exception.MessageCount(); i++) GUI.Label(new Rect(position.x, area.yMax + i * line, position.width, line), exception.GetMessage(i), messageStyle);
		}

		string source = setting.stringValue;
		ParseContext context = new ParseContext(allUsings, variables);
		if (string.IsNullOrWhiteSpace(source)) animateFromParseResult = null;
		else if (animateFromParseResult == null || source != animateFromParseResult.source || context.key != animateFromParseResult.context.key)
			animateFromParseResult = ParseCache.GetParseResult(source, context, ParseTarget.Value, resultType) ?? animateFromParseResult;
	}
}

// Unity's curve editor for the curves of Core (see CurveConversion), with buttons for common curves
[CustomPropertyDrawer(typeof(TransitionCurve))]
public class TransitionCurveDrawer : PropertyDrawer {
	private static readonly (string name, Func<TransitionCurve> create)[] PRESETS = {
		("Linear", TransitionCurve.Linear), ("Ease In", TransitionCurve.EaseIn), ("Ease Out", TransitionCurve.EaseOut), ("Ease In-Out", TransitionCurve.EaseInOut), ("Overshoot", TransitionCurve.Overshoot)
	};

	public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
		return EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
	}

	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
		EditorGUI.BeginProperty(position, label, property);
		Rect curveRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
		EditorGUI.BeginChangeCheck();
		AnimationCurve curve = EditorGUI.CurveField(curveRect, label, Read(property).ToAnimationCurve());
		if (EditorGUI.EndChangeCheck()) Write(property, curve.ToTransitionCurve());

		// the presets, below the curve, right of the label
		Rect presetsRect = EditorGUI.PrefixLabel(new Rect(position.x, curveRect.yMax + EditorGUIUtility.standardVerticalSpacing, position.width, EditorGUIUtility.singleLineHeight), new GUIContent(" "));
		float width = presetsRect.width / PRESETS.Length;
		for (int i = 0; i < PRESETS.Length; i++)
			if (GUI.Button(new Rect(presetsRect.x + i * width, presetsRect.y, width, presetsRect.height), PRESETS[i].name, EditorStyles.miniButton))
				Write(property, PRESETS[i].create());

		EditorGUI.EndProperty();
	}

	private static TransitionCurve Read(SerializedProperty property) {
		SerializedProperty keys = property.FindPropertyRelative("keys");
		CurveKey[] curveKeys = new CurveKey[keys.arraySize];
		for (int i = 0; i < curveKeys.Length; i++) {
			SerializedProperty key = keys.GetArrayElementAtIndex(i);
			curveKeys[i] = new CurveKey(key.FindPropertyRelative("time").floatValue, key.FindPropertyRelative("value").floatValue, key.FindPropertyRelative("inTangent").floatValue, key.FindPropertyRelative("outTangent").floatValue) {
				inWeight = key.FindPropertyRelative("inWeight").floatValue,
				outWeight = key.FindPropertyRelative("outWeight").floatValue,
				weightedSides = (WeightedSides)key.FindPropertyRelative("weightedSides").intValue
			};
		}

		return new TransitionCurve(curveKeys) {
			preWrapMode = (CurveWrapMode)property.FindPropertyRelative("preWrapMode").intValue,
			postWrapMode = (CurveWrapMode)property.FindPropertyRelative("postWrapMode").intValue
		};
	}

	private static void Write(SerializedProperty property, TransitionCurve curve) {
		SerializedProperty keys = property.FindPropertyRelative("keys");
		keys.arraySize = curve.keys.Length;
		for (int i = 0; i < curve.keys.Length; i++) {
			SerializedProperty key = keys.GetArrayElementAtIndex(i);
			CurveKey curveKey = curve.keys[i];
			key.FindPropertyRelative("time").floatValue = curveKey.time;
			key.FindPropertyRelative("value").floatValue = curveKey.value;
			key.FindPropertyRelative("inTangent").floatValue = curveKey.inTangent;
			key.FindPropertyRelative("outTangent").floatValue = curveKey.outTangent;
			key.FindPropertyRelative("inWeight").floatValue = curveKey.inWeight;
			key.FindPropertyRelative("outWeight").floatValue = curveKey.outWeight;
			key.FindPropertyRelative("weightedSides").intValue = (int)curveKey.weightedSides;
		}

		property.FindPropertyRelative("preWrapMode").intValue = (int)curve.preWrapMode;
		property.FindPropertyRelative("postWrapMode").intValue = (int)curve.postWrapMode;
	}
}
}
