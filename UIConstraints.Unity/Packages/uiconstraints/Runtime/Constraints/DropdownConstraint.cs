using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace UIConstraints.Unity.Constraints {
/**
 * The options of a TextMeshPro dropdown (any collection, each element is shown with its ToString) and which one is selected (its index).
 * Like all inputs, the selection shows the value of an expression, which the action sets when another option is chosen (e.g. settings.quality = value).
 */
[AddComponentMenu("UI Constraints/Dropdown Constraint")]
[RequireComponent(typeof(TMP_Dropdown))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/dropdown.png")]
public class DropdownConstraint : ComponentConstraint<TMP_Dropdown> {
	[SerializeField, ResultType(typeof(IEnumerable))] private CodeInput options;
	[SerializeField, ResultType(typeof(int)), Tooltip("The index of the selected option")] private CodeInput value;
	[SerializeField, ResultType(typeof(bool))] private CodeInput interactable;
	[SerializeField, Action("value", typeof(int))] private CodeInput onValueChanged;

	protected override void Bind() {
		BindProperty<IEnumerable>(nameof(options), SetOptions); // before the value, which is clamped to the options
		BindProperty<int>(nameof(value), d => d.value, (d, index) => d.SetValueWithoutNotify(index), true);
		BindProperty<bool>(nameof(interactable), d => d.interactable, (d, newInteractable) => d.interactable = newInteractable);
		BindAction(nameof(onValueChanged), Target.onValueChanged);
	}

	// only replaces the options if they changed, as that rebuilds the dropdown
	private static void SetOptions(TMP_Dropdown dropdown, IEnumerable elements) {
		List<string> texts = elements == null ? new List<string>() : elements.Cast<object>().Select(e => e?.ToString() ?? "").ToList();
		if (dropdown.options.Select(o => o.text).SequenceEqual(texts)) return;

		dropdown.ClearOptions();
		dropdown.AddOptions(texts);
	}
}
}
