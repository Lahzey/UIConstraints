using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace UIConstraints.Unity.Constraints {
// the text and look of a TextMeshPro text (UI or 3D), the text can be any value (its ToString is shown)
[AddComponentMenu("UI Constraints/Text Constraint")]
[RequireComponent(typeof(TMP_Text))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/text.png")]
public class TextConstraint : ComponentConstraint<TMP_Text> {
	[SerializeField, FormerlySerializedAs("textConstraint")] private CodeInput text;
	[SerializeField, ResultType(typeof(Color))] private CodeInput color;
	[SerializeField, ResultType(typeof(float))] private CodeInput fontSize;
	[SerializeField, ResultType(typeof(FontStyles))] private CodeInput fontStyle;
	[SerializeField, ResultType(typeof(TextAlignmentOptions))] private CodeInput alignment;
	[SerializeField, ResultType(typeof(int)), Tooltip("How many characters are visible, e.g. for a typewriter effect")] private CodeInput maxVisibleCharacters;

	protected override void Bind() {
		BindProperty<string>(nameof(text), t => t.text, (t, value) => t.text = value ?? "");
		BindProperty<Color>(nameof(color), t => t.color, (t, value) => t.color = value);
		BindProperty<float>(nameof(fontSize), t => t.fontSize, (t, value) => t.fontSize = value);
		BindProperty<FontStyles>(nameof(fontStyle), t => t.fontStyle, (t, value) => t.fontStyle = value);
		BindProperty<TextAlignmentOptions>(nameof(alignment), t => t.alignment, (t, value) => t.alignment = value);
		BindProperty<int>(nameof(maxVisibleCharacters), t => t.maxVisibleCharacters, (t, value) => t.maxVisibleCharacters = value);
	}
}
}
