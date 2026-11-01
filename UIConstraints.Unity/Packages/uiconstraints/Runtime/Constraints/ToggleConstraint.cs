using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
/**
 * Like all inputs, the toggle shows the value of an expression (e.g. settings.music), which the action sets when the toggle is clicked (e.g. settings.music = value).
 * Without an action, clicking the toggle has no lasting effect, as the next render shows the value of the expression again.
 */
[AddComponentMenu("UI Constraints/Toggle Constraint")]
[RequireComponent(typeof(Toggle))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/toggle.png")]
public class ToggleConstraint : ComponentConstraint<Toggle> {
	[SerializeField, ResultType(typeof(bool))] private CodeInput isOn;
	[SerializeField, ResultType(typeof(bool))] private CodeInput interactable;
	[SerializeField, Action("value", typeof(bool))] private CodeInput onValueChanged;

	protected override void Bind() {
		BindProperty<bool>(nameof(isOn), t => t.isOn, (t, value) => t.SetIsOnWithoutNotify(value), true);
		BindProperty<bool>(nameof(interactable), t => t.interactable, (t, value) => t.interactable = value);
		BindAction(nameof(onValueChanged), Target.onValueChanged);
	}
}
}
