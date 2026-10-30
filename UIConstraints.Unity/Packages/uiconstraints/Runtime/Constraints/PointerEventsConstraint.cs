using UIConstraints.Core.Parsing.Expressions;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UIConstraints.Unity.Constraints {
/**
 * Actions for pointer (mouse and touch) events on any UI element, e.g. to show a tooltip while it is hovered. The element needs a graphic that is a raycast target (e.g. an Image).
 * The variable 'eventData' has the details of the event, e.g. eventData.button or eventData.position.
 */
[AddComponentMenu("UI Constraints/Pointer Events Constraint")]
[RequireComponent(typeof(RectTransform))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/pointer-events.png")]
public class PointerEventsConstraint : ComponentConstraint<RectTransform>, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler {
	private const string PARAMETER = "eventData";

	[SerializeField, Action(PARAMETER, typeof(PointerEventData))] private CodeInput onPointerEnter;
	[SerializeField, Action(PARAMETER, typeof(PointerEventData))] private CodeInput onPointerExit;
	[SerializeField, Action(PARAMETER, typeof(PointerEventData))] private CodeInput onPointerDown;
	[SerializeField, Action(PARAMETER, typeof(PointerEventData))] private CodeInput onPointerUp;
	[SerializeField, Action(PARAMETER, typeof(PointerEventData))] private CodeInput onPointerClick;

	private Expression enterAction;
	private Expression exitAction;
	private Expression downAction;
	private Expression upAction;
	private Expression clickAction;

	protected override void Bind() {
		enterAction = ParseField(nameof(onPointerEnter));
		exitAction = ParseField(nameof(onPointerExit));
		downAction = ParseField(nameof(onPointerDown));
		upAction = ParseField(nameof(onPointerUp));
		clickAction = ParseField(nameof(onPointerClick));
	}

	public void OnPointerEnter(PointerEventData eventData) {
		Execute(enterAction, PARAMETER, eventData);
	}

	public void OnPointerExit(PointerEventData eventData) {
		Execute(exitAction, PARAMETER, eventData);
	}

	public void OnPointerDown(PointerEventData eventData) {
		Execute(downAction, PARAMETER, eventData);
	}

	public void OnPointerUp(PointerEventData eventData) {
		Execute(upAction, PARAMETER, eventData);
	}

	public void OnPointerClick(PointerEventData eventData) {
		Execute(clickAction, PARAMETER, eventData);
	}
}
}
