using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
// what a button does when clicked (e.g. shop.Buy(item, 1)), and whether it can be clicked
[AddComponentMenu("UI Constraints/Button Constraint")]
[RequireComponent(typeof(Button))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/button.png")]
public class ButtonConstraint : ComponentConstraint<Button> {
	[SerializeField, ResultType(typeof(bool))] private CodeInput interactable;
	[SerializeField, Action] private CodeInput onClick;

	protected override void Bind() {
		BindProperty<bool>(nameof(interactable), b => b.interactable, (b, value) => b.interactable = value);
		BindAction(nameof(onClick), Target.onClick);
	}
}
}
