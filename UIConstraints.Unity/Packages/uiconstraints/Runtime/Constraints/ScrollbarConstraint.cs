using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
[AddComponentMenu("UI Constraints/Scrollbar Constraint")]
[RequireComponent(typeof(Scrollbar))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/scrollbar.png")]
public class ScrollbarConstraint : ComponentConstraint<Scrollbar> {
	[SerializeField, ResultType(typeof(float)), Tooltip("The position of the handle, 0 to 1")] private CodeInput value;
	[SerializeField, ResultType(typeof(float)), Tooltip("The size of the handle, 0 to 1")] private CodeInput size;
	[SerializeField, ResultType(typeof(bool))] private CodeInput interactable;
	[SerializeField, Action("value", typeof(float))] private CodeInput onValueChanged;

	protected override void Bind() {
		BindProperty<float>(nameof(value), s => s.value, (s, newValue) => s.SetValueWithoutNotify(newValue));
		BindProperty<float>(nameof(size), s => s.size, (s, newSize) => s.size = newSize);
		BindProperty<bool>(nameof(interactable), s => s.interactable, (s, newInteractable) => s.interactable = newInteractable);
		BindAction(nameof(onValueChanged), Target.onValueChanged);
	}
}
}
