using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
// the sizes a layout group gives an element (-1 means unset, like in the inspector of the layout element)
[AddComponentMenu("UI Constraints/Layout Element Constraint")]
[RequireComponent(typeof(LayoutElement))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/layout-element.png")]
public class LayoutElementConstraint : ComponentConstraint<LayoutElement> {
	[SerializeField, ResultType(typeof(bool))] private CodeInput ignoreLayout;
	[SerializeField, ResultType(typeof(float))] private CodeInput minWidth;
	[SerializeField, ResultType(typeof(float))] private CodeInput minHeight;
	[SerializeField, ResultType(typeof(float))] private CodeInput preferredWidth;
	[SerializeField, ResultType(typeof(float))] private CodeInput preferredHeight;
	[SerializeField, ResultType(typeof(float)), Tooltip("How much of the remaining space the element gets, relative to the others")] private CodeInput flexibleWidth;
	[SerializeField, ResultType(typeof(float)), Tooltip("How much of the remaining space the element gets, relative to the others")] private CodeInput flexibleHeight;

	protected override void Bind() {
		BindProperty<bool>(nameof(ignoreLayout), l => l.ignoreLayout, (l, value) => l.ignoreLayout = value);
		BindProperty<float>(nameof(minWidth), l => l.minWidth, (l, value) => l.minWidth = value);
		BindProperty<float>(nameof(minHeight), l => l.minHeight, (l, value) => l.minHeight = value);
		BindProperty<float>(nameof(preferredWidth), l => l.preferredWidth, (l, value) => l.preferredWidth = value);
		BindProperty<float>(nameof(preferredHeight), l => l.preferredHeight, (l, value) => l.preferredHeight = value);
		BindProperty<float>(nameof(flexibleWidth), l => l.flexibleWidth, (l, value) => l.flexibleWidth = value);
		BindProperty<float>(nameof(flexibleHeight), l => l.flexibleHeight, (l, value) => l.flexibleHeight = value);
	}
}
}
