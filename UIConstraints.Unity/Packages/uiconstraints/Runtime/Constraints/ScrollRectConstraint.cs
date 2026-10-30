using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
// which directions a scroll view can be scrolled in, and an action when it is scrolled (e.g. to load more elements once the end is reached)
[AddComponentMenu("UI Constraints/Scroll Rect Constraint")]
[RequireComponent(typeof(ScrollRect))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/scroll-rect.png")]
public class ScrollRectConstraint : ComponentConstraint<ScrollRect> {
	[SerializeField, ResultType(typeof(bool))] private CodeInput horizontal;
	[SerializeField, ResultType(typeof(bool))] private CodeInput vertical;
	[SerializeField, Action("value", typeof(Vector2)), Tooltip("Executed when scrolled, the value is the normalized position (0 to 1 on both axes, y = 0 is the bottom)")] private CodeInput onValueChanged;

	protected override void Bind() {
		BindProperty<bool>(nameof(horizontal), s => s.horizontal, (s, value) => s.horizontal = value);
		BindProperty<bool>(nameof(vertical), s => s.vertical, (s, value) => s.vertical = value);
		BindAction(nameof(onValueChanged), Target.onValueChanged);
	}
}
}
