using TMPro;
using UnityEngine;

namespace UIConstraints.Unity.Constraints {
/**
 * Like all inputs, the input field shows the value of an expression (e.g. player.name), which the action sets while typing (e.g. player.name = value).
 * The text is only replaced if it differs from the value, so typing is not interrupted as long as the action keeps them the same.
 */
[AddComponentMenu("UI Constraints/Input Field Constraint")]
[RequireComponent(typeof(TMP_InputField))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/input-field.png")]
public class InputFieldConstraint : ComponentConstraint<TMP_InputField> {
	[SerializeField] private CodeInput text;
	[SerializeField, Tooltip("Shown while the field is empty (needs a TextMeshPro text as the placeholder of the input field)")] private CodeInput placeholder;
	[SerializeField, ResultType(typeof(bool))] private CodeInput interactable;
	[SerializeField, Action("value", typeof(string)), Tooltip("Executed whenever the text is changed, e.g. while typing")] private CodeInput onValueChanged;
	[SerializeField, Action("value", typeof(string)), Tooltip("Executed when the field is left, e.g. by clicking somewhere else or pressing enter")] private CodeInput onEndEdit;
	[SerializeField, Action("value", typeof(string)), Tooltip("Executed when enter is pressed")] private CodeInput onSubmit;

	protected override void Bind() {
		BindProperty<string>(nameof(text), i => i.text, (i, value) => i.SetTextWithoutNotify(value ?? ""), true);
		BindProperty<string>(nameof(placeholder), i => (i.placeholder as TMP_Text)?.text, SetPlaceholder);
		BindProperty<bool>(nameof(interactable), i => i.interactable, (i, value) => i.interactable = value);
		BindAction(nameof(onValueChanged), Target.onValueChanged);
		BindAction(nameof(onEndEdit), Target.onEndEdit);
		BindAction(nameof(onSubmit), Target.onSubmit);
	}

	private void SetPlaceholder(TMP_InputField inputField, string value) {
		if (inputField.placeholder is TMP_Text placeholderText) placeholderText.text = value ?? "";
		else Debug.LogWarning("The placeholder of the input field has to be a TextMeshPro text to set its text", this);
	}
}
}
