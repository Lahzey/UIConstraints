using System;
using System.ComponentModel;
using System.Reflection;
using JetBrains.Annotations;
using UIConstraints.Core.Parsing;

namespace UIConstraints.Core.Animation;

// in which direction a value has to change to be animated, changes in the other direction are shown right away
public enum AnimateWhen {
    Always,
    Increasing,
    Decreasing
}

// what a transition animates: the type of the value, and the engine object it is shown on (e.g. an image, for transitions that draw something themselves)
public class TransitionContext {
    public readonly Type valueType;
    [CanBeNull] public readonly object owner;

    public TransitionContext(Type valueType, [CanBeNull] object owner = null) {
        this.valueType = valueType;
        this.owner = owner;
    }
}

/**
 * How a value moves to a new value instead of changing right away, e.g. a health bar sliding to the new health. The settings are shared by everything animated with them,
 * each animated value has its own TransitionState (see CreateState).
 * To add a transition, extend this (or TimedTransition for transitions with a duration and curve) with public fields as its settings, which engines show in their editors.
 * DisplayNameAttribute names it there, DescriptionAttribute on the class and fields adds tooltips. It must have a constructor without parameters.
 */
[Serializable]
public abstract class Transition {
    [Description("Seconds to wait before the value starts moving. A further change in the same direction during the wait starts it again (e.g. a damage bar that only catches up once no damage was taken for a moment).")]
    public float delay;

    [Description("Only changes in this direction are animated, changes in the other direction are shown right away (e.g. a damage bar that only lags behind when health is lost).")]
    public AnimateWhen animateWhen = AnimateWhen.Always;

    [Description("Whether the animation follows the scaled time of the game (which stops while it is paused) instead of the real time.")]
    public bool useScaledTime;

    [Description("An expression for the value to animate from the first time the value is shown (e.g. 0 for a bar that fills up), empty to just show the first value.")]
    public string animateFrom = "";

    // whether values of the type can be animated with this transition
    public abstract bool CanAnimate(Type valueType);

    public abstract TransitionState CreateState(TransitionContext context);

    // a short description of the settings, shown while they are collapsed in the editor
    public virtual string GetSummary() {
        string summary = GetDisplayName(GetType());
        if (delay > 0) summary += $" · {FormatSeconds(delay)} delay";
        if (animateWhen != AnimateWhen.Always) summary += animateWhen == AnimateWhen.Increasing ? " · when increasing" : " · when decreasing";
        return summary;
    }

    // whether a change is animated (see animateWhen)
    public bool ShouldAnimate(object from, object to) {
        if (animateWhen == AnimateWhen.Always) return true;
        int direction = GetDirection(from, to);
        return direction == 0 || direction == (animateWhen == AnimateWhen.Increasing ? 1 : -1);
    }

    // 1 if the value increases, -1 if it decreases, 0 if that does not apply
    public int GetChangeDirection(object from, object to) => GetDirection(from, to);

    // 1 if the value increases, -1 if it decreases, 0 if that does not apply (which is always animated)
    protected virtual int GetDirection(object from, object to) {
        return ValueBlending.Compare(from, to);
    }

    public static string GetDisplayName(Type transitionType) {
        string name = transitionType.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
        if (name != null) return name;
        name = transitionType.Name;
        return name.EndsWith("Transition") && name.Length > "Transition".Length ? name.Substring(0, name.Length - "Transition".Length) : name;
    }

    protected static string FormatSeconds(float seconds) {
        return seconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " s";
    }
}

/**
 * A transition that takes a set time, following a curve. The duration can depend on how far the value moves (see fullDurationFrom),
 * so small changes are quick and big ones don't take forever, e.g. for a counter going from 50 to 51 or to 1,000,000.
 */
[Serializable]
public abstract class TimedTransition : Transition {
    [Description("Seconds the transition takes (for changes of at least 'Full Duration From', if that is set).")]
    public float duration = 0.3f;

    [Description("How far the value has to move to take the whole duration, smaller changes are quicker (on a logarithmic scale, so a tenth of it takes about half the duration). 0 to always take the whole duration.")]
    public float fullDurationFrom;

    [Description("How the value moves over time: from 0 (the start) to 1 (the new value) while the time goes from 0 to 1.")]
    public TransitionCurve curve = TransitionCurve.EaseOut();

    // the duration of a change, see fullDurationFrom
    public virtual float GetDuration(object from, object to, Type valueType) {
        if (fullDurationFrom <= 0) return duration;
        double distance = GetDistance(from, to, valueType);
        if (double.IsNaN(distance)) return duration;
        double share = Math.Log(1 + distance) / Math.Log(1 + fullDurationFrom);
        return (float)(duration * Math.Min(1, share));
    }

    // how far the value moves, NaN if that can't be measured
    protected virtual double GetDistance(object from, object to, Type valueType) {
        return ValueBlending.Distance(from, to, valueType);
    }

    public override string GetSummary() {
        string summary = GetDisplayName(GetType()) + " · " + (fullDurationFrom > 0 ? "up to " : "") + FormatSeconds(duration);
        string baseSummary = base.GetSummary();
        int extra = baseSummary.IndexOf(" · ", StringComparison.Ordinal);
        return extra < 0 ? summary : summary + baseSummary.Substring(extra);
    }
}

/**
 * The animation of one value: the value shown while it moves to its target, which the engine shows instead of the target itself.
 * It always moves from the value currently shown, so a change while it is still moving continues from where it is instead of jumping.
 * Values that change every frame (e.g. regenerating health) keep moving: a new target while moving continues the movement right away, without waiting for the delay again.
 */
public abstract class TransitionState {
    protected readonly TransitionContext context;
    private readonly Transition transition;

    private bool hasTarget;
    [CanBeNull] private object target;
    [CanBeNull] private object shown;
    private bool moving;
    private double changeTime; // when the movement started, the delay is counted from there
    private double lastTime; // of the last update

    protected TransitionState(Transition transition, TransitionContext context) {
        this.transition = transition;
        this.context = context;
    }

    [CanBeNull] public object Shown => shown;

    // whether the shown value is still moving to the target
    public bool IsMoving => moving;

    /**
     * The value to show at the given time (in seconds, see Transition.useScaledTime), for the current target.
     * animateFrom: the value to start from the first time a value is shown (null to show the first value right away), see Transition.animateFrom.
     */
    [CanBeNull]
    public object Update([CanBeNull] object newTarget, double time, [CanBeNull] Func<object> animateFrom = null) {
        if (!hasTarget) {
            hasTarget = true;
            target = newTarget;
            shown = newTarget;
            object start = animateFrom?.Invoke();
            if (start != null && newTarget != null) StartMoving(start, time);
        }
        else if (!Equals(newTarget, target)) {
            object previousTarget = target;
            target = newTarget;
            if (shown == null || newTarget == null || !transition.ShouldAnimate(shown, newTarget)) Snap(newTarget);
            else if (!moving) StartMoving(shown, time);
            else if (time - changeTime < transition.delay) {
                // still waiting: a further change in the same direction (e.g. another hit) waits again, a change back (e.g. regeneration) only changes where it moves to
                if (ContinuesChange(previousTarget, newTarget)) StartMoving(shown, time);
                else Begin(shown, newTarget);
            }
            else if (IsLargeChange(previousTarget, newTarget) && ContinuesChange(previousTarget, newTarget)) {
                StartMoving(shown, time); // e.g. another hit while the health bar is still moving: a new movement, which waits for the delay again
            }
            else if (IsLargeChange(previousTarget, newTarget) || !Retarget(shown, newTarget)) {
                // a new movement from the shown value (small changes, e.g. in every frame, only change where the current one moves to, see Retarget).
                // The time since the last update already counts, otherwise values that change every frame would never move
                changeTime = lastTime - transition.delay;
                Begin(shown, newTarget);
            }
        }

        lastTime = time;
        if (!moving) return shown;
        double elapsed = time - changeTime - transition.delay;
        if (elapsed < 0) return shown; // waiting for the delay, at the value it moves from

        shown = Sample(elapsed, out bool finished);
        if (finished) {
            moving = false;
            shown = target;
        }

        return shown;
    }

    // shows a value right away, e.g. when the user changed it through an input, so it doesn't animate to what is already shown
    public void Snap([CanBeNull] object value) {
        hasTarget = true;
        target = value;
        shown = value;
        if (moving) Stopped();
        moving = false;
    }

    // whether the change to the new target moves further away from the shown value than the previous target
    private bool ContinuesChange(object previousTarget, object newTarget) {
        int movement = transition.GetChangeDirection(shown, previousTarget);
        int change = transition.GetChangeDirection(previousTarget, newTarget);
        return movement == 0 || change == 0 || movement == change;
    }

    // whether the change of the target is big compared to how far the value still has to move (small changes, e.g. in every frame, just change where it moves to)
    private bool IsLargeChange(object previousTarget, object newTarget) {
        double change = ValueBlending.Distance(previousTarget, newTarget, context.valueType);
        double remaining = ValueBlending.Distance(shown, previousTarget, context.valueType);
        return !double.IsNaN(change) && !double.IsNaN(remaining) && change > remaining * 0.25;
    }

    private void StartMoving(object from, double time) {
        shown = from;
        changeTime = time;
        moving = true;
        Begin(from, target);
    }

    // starts moving from the shown value to a new target
    protected abstract void Begin(object from, object to);

    /**
     * Changes the target while moving, continuing the movement instead of starting a new one (the elapsed time passed to Sample continues too).
     * Returns false if not supported, in which case Begin starts a new movement from the shown value.
     */
    protected virtual bool Retarget(object from, object to) {
        return false;
    }

    // the value to show the given seconds after moving started (after the delay), finished once the target is reached
    protected abstract object Sample(double elapsed, out bool finished);

    // the movement stopped before it finished, as a value was shown right away (e.g. for transitions that draw something themselves, which they stop drawing then)
    protected virtual void Stopped() { }
}

// the state of a TimedTransition: progress through the duration, mapped by the curve
public abstract class TimedTransitionState<T> : TransitionState where T : TimedTransition {
    protected readonly T transition;
    private float duration;
    private float progress; // of the last sample
    private float retargetCurved; // the point of the curve when the target last changed while moving, the rest of the curve moves to the new target

    protected TimedTransitionState(T transition, TransitionContext context) : base(transition, context) {
        this.transition = transition;
    }

    protected override void Begin(object from, object to) {
        duration = transition.GetDuration(from, to, context.valueType);
        progress = 0;
        retargetCurved = 0;
        BeginMoving(from, to);
    }

    // keeps the progress and end time, and maps the rest of the curve onto the way to the new target, so the value moves on smoothly
    protected override bool Retarget(object from, object to) {
        retargetCurved = Evaluate(progress);
        if (retargetCurved >= 0.999f) return false; // practically done, a new movement fits better
        BeginMoving(from, to);
        return true;
    }

    protected override object Sample(double elapsed, out bool finished) {
        progress = duration <= 0 ? 1 : (float)Math.Min(1, elapsed / duration);
        finished = progress >= 1;
        float curved = (Evaluate(progress) - retargetCurved) / (1 - retargetCurved);
        return Sample(curved, progress, elapsed);
    }

    private float Evaluate(float progress) => transition.curve?.Evaluate(progress) ?? progress;

    protected abstract void BeginMoving(object from, object to);

    // the value at the given point of the curve (curved), at the progress through the duration (linear, 0 to 1)
    protected abstract object Sample(float curved, float progress, double elapsed);
}
