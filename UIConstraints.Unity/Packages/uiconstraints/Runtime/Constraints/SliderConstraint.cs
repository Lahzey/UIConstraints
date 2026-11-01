using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
/**
 * Like all inputs, the slider shows the value of an expression (e.g. settings.volume), which the action sets when the slider is moved (e.g. settings.volume = value).
 * It can also just show a value, e.g. as a health bar that can't be interacted with.
 */
[AddComponentMenu("UI Constraints/Slider Constraint")]
[RequireComponent(typeof(Slider))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/slider.png")]
public class SliderConstraint : ComponentConstraint<Slider> {
	[SerializeField, ResultType(typeof(float))] private CodeInput value;
	[SerializeField, ResultType(typeof(float))] private CodeInput minValue;
	[SerializeField, ResultType(typeof(float))] private CodeInput maxValue;
	[SerializeField, ResultType(typeof(bool))] private CodeInput interactable;
	[SerializeField, Action("value", typeof(float))] private CodeInput onValueChanged;

	protected override void Bind() {
		// the range first, as the value is clamped to it
		BindProperty<float>(nameof(minValue), s => s.minValue, (s, newMinValue) => s.minValue = newMinValue);
		BindProperty<float>(nameof(maxValue), s => s.maxValue, (s, newMaxValue) => s.maxValue = newMaxValue);
		BindProperty<float>(nameof(value), s => s.value, (s, newValue) => s.SetValueWithoutNotify(newValue), true);
		BindProperty<bool>(nameof(interactable), s => s.interactable, (s, newInteractable) => s.interactable = newInteractable);
		BindAction(nameof(onValueChanged), Target.onValueChanged);
	}
}
}
