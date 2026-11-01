using System;
using System.Reflection;
using JetBrains.Annotations;
using UIConstraints.Core.Animation;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using UnityEngine;

namespace UIConstraints.Unity.Animation {
/**
 * The animation of the value of a CodeInput field with its transition (see CodeInput.transition), which constraints show instead of the value itself.
 * Each component has its own, as the transition settings are shared, but not where the value currently is.
 */
public class AnimatedInput {
	private readonly Component owner;
	private readonly string fieldName;
	private readonly Transition transition;
	private readonly TransitionState state;
	[CanBeNull] private readonly Expression animateFrom;

	private AnimatedInput(Component owner, string fieldName, Transition transition, TransitionState state, [CanBeNull] Expression animateFrom) {
		this.owner = owner;
		this.fieldName = fieldName;
		this.transition = transition;
		this.state = state;
		this.animateFrom = animateFrom;
	}

	/**
	 * The animation of a field of the owner, null if it has no transition, or one that can't animate values of the type (which is logged).
	 * animated: the engine object the value is shown on (e.g. the image whose sprite is animated), for transitions that draw something themselves.
	 */
	[CanBeNull]
	public static AnimatedInput Create(Component owner, string fieldName, Type valueType, [CanBeNull] object animated = null) {
		FieldInfo field = CodeInputs.GetField(owner.GetType(), fieldName);
		Transition transition = ((CodeInput)field.GetValue(owner))?.transition;
		if (transition == null) return null;

		UnityBlending.EnsureRegistered();
		if (!transition.CanAnimate(valueType)) {
			Debug.LogError($"{fieldName} of {owner.GetType().Name} can't be animated with {Transition.GetDisplayName(transition.GetType())}, as it is {TypeNames.GetDisplayName(valueType)}", owner);
			return null;
		}

		Expression animateFrom = null;
		ParseResult animateFromResult = CodeInputs.ParseAnimateFrom(owner, field);
		if (animateFromResult != null && animateFromResult.Success) animateFrom = animateFromResult.Expression;
		else if (animateFromResult != null) Debug.LogError($"Invalid animate from of {fieldName} of {owner.GetType().Name} '{animateFromResult.source}': {animateFromResult.Exception.GetMessage(0)}", owner);

		return new AnimatedInput(owner, fieldName, transition, transition.CreateState(new TransitionContext(valueType, animated)), animateFrom);
	}

	// the value to show now, for the current value of the input
	public object Update(object target, DataContext context) {
		double time = transition.useScaledTime ? Time.timeAsDouble : Time.unscaledTimeAsDouble;
		return state.Update(target, time, animateFrom == null ? null : () => EvaluateAnimateFrom(context, target));
	}

	// shows a value right away, e.g. one the user just changed through an input
	public void Snap(object value) {
		state.Snap(value);
	}

	[CanBeNull]
	private object EvaluateAnimateFrom(DataContext context, object target) {
		try {
			object start = animateFrom.Evaluate(context);
			return target != null ? TypeRules.ConvertImplicitly(start, target.GetType()) : start;
		}
		catch (EvaluationException e) {
			Debug.LogException(new EvaluationException($"Animate from of {fieldName} of {owner.GetType().Name}: {e.Message}", e.ExpressionToEvaluate, e), owner);
			return null;
		}
	}
}
}
