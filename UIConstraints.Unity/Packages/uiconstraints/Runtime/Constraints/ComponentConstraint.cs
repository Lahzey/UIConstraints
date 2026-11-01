using System;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using UIConstraints.Unity.Animation;
using UnityEngine;
using UnityEngine.Events;

namespace UIConstraints.Unity.Constraints {
/**
 * A constraint for a component on the same game object (e.g. a Slider): sets its properties to the values of expressions whenever the UI is rendered,
 * and executes actions when its events are raised (e.g. what happens when the slider is moved).
 * Subclasses declare a CodeInput field per property and event, and register them in Bind. Empty inputs leave their property as it is, and do nothing for events.
 */
public abstract class ComponentConstraint<T> : UIConstraint, IAnimatedInputs where T : Component {
	private readonly List<PropertyBinding> properties = new List<PropertyBinding>();
	private readonly List<Action> unbindActions = new List<Action>(); // remove the listeners of actions from their events

	// the context of the last render, which actions are executed with, so they can use the same variables (e.g. the item of a ForEachConstraint)
	[CanBeNull] private DataContext renderedContext;

	// Events raised while rendering are caused by setting properties, not by the user, so they don't execute actions.
	// E.g. setting the minimum of a slider may clamp its value, which raises onValueChanged even though the value was not changed by the user.
	private bool rendering;

	// whether the user changed a value through the component (e.g. moved a slider) since the last render, which is then shown right away instead of being animated (see BindProperty)
	private bool changedByUser;

	// the component whose properties are set
	protected T Target { get; private set; }

	protected override void Awake() {
		base.Awake();
		Target = GetComponent<T>();
		Bind();
	}

	protected override void OnDestroy() {
		base.OnDestroy();
		foreach (Action unbind in unbindActions) unbind();
	}

	// registers the inputs (see BindProperty and BindAction)
	protected abstract void Bind();

	/**
	 * Sets a property to the value of an input on every render. The value is converted to TValue (which the ResultTypeAttribute of the field should match, so mistakes show in the editor),
	 * string properties accept any value and use its ToString. To not mark anything dirty needlessly, the property is only set if its value changed (according to get).
	 * If the input has a transition, the property is animated to new values. userEditable: whether the user changes the property through the component (e.g. the value of a slider),
	 * which is not animated, as the component already shows it.
	 */
	protected void BindProperty<TValue>(string fieldName, [CanBeNull] Func<T, TValue> get, Action<T, TValue> set, bool userEditable = false) {
		Expression expression = ParseField(fieldName);
		if (expression == null) return;

		AnimatedInput animation = AnimatedInput.Create(this, fieldName, typeof(TValue), Target);
		properties.Add(new PropertyBinding<TValue>(this, fieldName, expression, get, set, animation, userEditable));
	}

	// like BindProperty, for properties that are compared by set itself (e.g. because they are lists)
	protected void BindProperty<TValue>(string fieldName, Action<T, TValue> set) {
		BindProperty(fieldName, null, set);
	}

	// every input that is not an action, with the type of its property: the result type of the field, or string for fields without one (which accept anything, see BindProperty)
	public virtual Type GetAnimatedType(FieldInfo field) {
		if (field.IsDefined(typeof(ActionAttribute), false) || field.IsDefined(typeof(TypeInputAttribute), false)) return null;
		return CodeInputs.GetResultType(field) ?? typeof(string);
	}

	// executes the action of an input when the event is raised
	protected void BindAction(string fieldName, UnityEvent unityEvent) {
		Expression expression = ParseField(fieldName);
		if (expression == null) return;

		UnityAction listener = () => Execute(expression, null, null);
		unityEvent.AddListener(listener);
		unbindActions.Add(() => unityEvent.RemoveListener(listener));
	}

	// executes the action of an input when the event is raised, with the value of the event as the parameter declared by its ActionAttribute
	protected void BindAction<TArgument>(string fieldName, UnityEvent<TArgument> unityEvent) {
		Expression expression = ParseField(fieldName);
		if (expression == null) return;

		string parameterName = CodeInputs.GetField(GetType(), fieldName).GetCustomAttribute<ActionAttribute>()?.parameterName;
		UnityAction<TArgument> listener = argument => Execute(expression, parameterName, argument);
		unityEvent.AddListener(listener);
		unbindActions.Add(() => unityEvent.RemoveListener(listener));
	}

	// for events that are not UnityEvents (e.g. pointer events): executes the action of an input (does nothing if it is empty or invalid)
	protected void Execute([CanBeNull] Expression action, [CanBeNull] string parameterName, object argument) {
		if (action == null || rendering) return;
		changedByUser = true;

		DataContext context = new DataContext(renderedContext);
		if (parameterName != null) context.SetLocal(parameterName, argument);
		try {
			action.Evaluate(context);
		}
		catch (EvaluationException e) {
			Debug.LogException(e, this);
		}
	}

	public override void Render(DataContext context) {
		renderedContext = context;
		rendering = true;
		try {
			foreach (PropertyBinding property in properties) property.Render(context, changedByUser);
		}
		finally {
			rendering = false;
			changedByUser = false;
		}
	}

	private abstract class PropertyBinding {
		// changedByUser: whether the user changed a value through the component since the last render
		public abstract void Render(DataContext context, bool changedByUser);
	}

	private class PropertyBinding<TValue> : PropertyBinding {
		private readonly ComponentConstraint<T> owner;
		private readonly string fieldName;
		private readonly Expression expression;
		[CanBeNull] private readonly Func<T, TValue> get;
		private readonly Action<T, TValue> set;
		[CanBeNull] private readonly AnimatedInput animation;
		private readonly bool userEditable;
		[CanBeNull] private string loggedError; // errors are logged once instead of on every render

		public PropertyBinding(ComponentConstraint<T> owner, string fieldName, Expression expression, [CanBeNull] Func<T, TValue> get, Action<T, TValue> set, [CanBeNull] AnimatedInput animation, bool userEditable) {
			this.owner = owner;
			this.fieldName = fieldName;
			this.expression = expression;
			this.get = get;
			this.set = set;
			this.animation = animation;
			this.userEditable = userEditable;
		}

		public override void Render(DataContext context, bool changedByUser) {
			try {
				TValue value = Convert(expression.Evaluate(context));
				if (animation != null) {
					if (changedByUser && userEditable) animation.Snap(value); // already shown by the component
					value = (TValue)animation.Update(value, context);
				}

				if (get == null || !EqualityComparer<TValue>.Default.Equals(get(owner.Target), value)) set(owner.Target, value);
				loggedError = null;
			}
			catch (EvaluationException e) {
				if (loggedError != e.Message) Debug.LogException(new EvaluationException($"{fieldName} of {owner.GetType().Name}: {e.Message}", e.ExpressionToEvaluate, e), owner);
				loggedError = e.Message;
			}
		}

		private TValue Convert(object value) {
			if (typeof(TValue) == typeof(string)) return (TValue)(object)value?.ToString();

			object converted = TypeRules.ConvertImplicitly(value, typeof(TValue));
			if (converted is TValue typed) return typed;
			if (converted == null && TypeRules.CanBeNull(typeof(TValue))) return default;
			throw new EvaluationException($"The value has to be {TypeNames.GetDisplayName(typeof(TValue))}, but is {(value == null ? "null" : TypeNames.GetDisplayName(value.GetType()))}", expression);
		}
	}
}
}
