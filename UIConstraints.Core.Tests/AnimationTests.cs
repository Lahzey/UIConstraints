using System;
using NUnit.Framework;
using UIConstraints.Core.Animation;
using UIConstraints.Core.Tests.TestGame;

namespace UIConstraints.Core.Tests {
// curves, transitions and the blending of values, with simulated time
public class AnimationTests {
	private const float TOLERANCE = 1e-4f;

	private static TransitionState State(Transition transition, Type valueType) {
		return transition.CreateState(new TransitionContext(valueType));
	}

	private static InterpolateTransition Linear(float duration = 1) {
		return new InterpolateTransition { duration = duration, curve = TransitionCurve.Linear() };
	}

	[TestCase(0f, 0f)]
	[TestCase(0.25f, 0.25f)]
	[TestCase(1f, 1f)]
	[TestCase(-1f, 0f)] // clamped before the first key
	[TestCase(2f, 1f)] // and after the last
	public void LinearCurve(float time, float expected) {
		Assert.That(TransitionCurve.Linear().Evaluate(time), Is.EqualTo(expected).Within(TOLERANCE));
	}

	[Test]
	public void EasingCurves() {
		Assert.That(TransitionCurve.EaseIn().Evaluate(0.5f), Is.EqualTo(0.25f).Within(TOLERANCE)); // quadratic: t²
		Assert.That(TransitionCurve.EaseOut().Evaluate(0.5f), Is.EqualTo(0.75f).Within(TOLERANCE)); // 1 - (1 - t)²
		Assert.That(TransitionCurve.EaseInOut().Evaluate(0.5f), Is.EqualTo(0.5f).Within(TOLERANCE));
		Assert.That(TransitionCurve.EaseInOut().Evaluate(0.25f), Is.EqualTo(0.15625f).Within(TOLERANCE)); // 3t² - 2t³
		Assert.That(TransitionCurve.Overshoot().Evaluate(0.65f), Is.EqualTo(1.1f).Within(TOLERANCE));
	}

	[Test]
	public void WrapModes() {
		TransitionCurve curve = TransitionCurve.Linear();
		curve.postWrapMode = CurveWrapMode.Loop;
		Assert.That(curve.Evaluate(1.25f), Is.EqualTo(0.25f).Within(TOLERANCE));
		curve.postWrapMode = CurveWrapMode.PingPong;
		Assert.That(curve.Evaluate(1.25f), Is.EqualTo(0.75f).Within(TOLERANCE));
		curve.preWrapMode = CurveWrapMode.Loop;
		Assert.That(curve.Evaluate(-0.25f), Is.EqualTo(0.75f).Within(TOLERANCE));
	}

	[Test]
	public void InfiniteTangentsHoldTheValue() {
		TransitionCurve stepped = new TransitionCurve(new CurveKey(0, 0, 0, float.PositiveInfinity), new CurveKey(1, 1, float.PositiveInfinity, 0));
		Assert.That(stepped.Evaluate(0.99f), Is.EqualTo(0f));
	}

	[Test]
	public void DefaultWeightsMatchUnweightedKeys() {
		// a Bezier segment with handles at a third of the segment is the same as the Hermite spline of unweighted keys
		TransitionCurve unweighted = new TransitionCurve(new CurveKey(0, 0, 0.5f, 0.5f), new CurveKey(2, 3, -1, -1));
		TransitionCurve weighted = unweighted.Clone();
		weighted.keys[0].weightedSides = WeightedSides.Out;
		weighted.keys[1].weightedSides = WeightedSides.In;
		for (float time = 0; time <= 2; time += 0.1f) Assert.That(weighted.Evaluate(time), Is.EqualTo(unweighted.Evaluate(time)).Within(TOLERANCE), $"at {time}");
	}

	[Test]
	public void WeightedTangentsChangeTheShape() {
		TransitionCurve curve = new TransitionCurve(new CurveKey(0, 0, 0, 0), new CurveKey(1, 1, 0, 0));
		curve.keys[0].weightedSides = WeightedSides.Out;
		curve.keys[0].outWeight = 1; // the handle reaches the end, so the curve stays low for longer
		Assert.That(curve.Evaluate(0.5f), Is.LessThan(TransitionCurve.EaseInOut().Evaluate(0.5f)));
		Assert.That(curve.Evaluate(0), Is.EqualTo(0f).Within(TOLERANCE));
		Assert.That(curve.Evaluate(1), Is.EqualTo(1f).Within(TOLERANCE));
	}

	[Test]
	public void FirstValueIsShownRightAway() {
		TransitionState state = State(Linear(), typeof(float));
		Assert.That(state.Update(5f, 0), Is.EqualTo(5f));
		Assert.That(state.IsMoving, Is.False);
	}

	[Test]
	public void ValuesMoveOverTheDuration() {
		TransitionState state = State(Linear(2), typeof(float));
		state.Update(0f, 0);
		Assert.That(state.Update(10f, 1), Is.EqualTo(0f)); // starts moving when the change is seen
		Assert.That(state.Update(10f, 2), Is.EqualTo(5f));
		Assert.That(state.Update(10f, 3), Is.EqualTo(10f));
		Assert.That(state.IsMoving, Is.False);
	}

	[Test]
	public void ChangesWhileMovingContinueFromTheShownValue() {
		TransitionState state = State(Linear(), typeof(float));
		state.Update(0f, 0);
		state.Update(10f, 0);
		Assert.That(state.Update(10f, 0.5), Is.EqualTo(5f));
		Assert.That(state.Update(0f, 0.5), Is.EqualTo(5f)); // back, from where it is
		Assert.That(state.Update(0f, 1), Is.EqualTo(2.5f));
	}

	[Test]
	public void ChangesDuringTheDelayStartItAgain() {
		InterpolateTransition transition = Linear();
		transition.delay = 1;
		TransitionState state = State(transition, typeof(float));
		state.Update(100f, 0);
		state.Update(90f, 0);
		Assert.That(state.Update(90f, 0.9), Is.EqualTo(100f));
		state.Update(80f, 0.9); // another hit while waiting
		Assert.That(state.Update(80f, 1.8), Is.EqualTo(100f));
		Assert.That(state.Update(80f, 2.4), Is.EqualTo(90f).Within(TOLERANCE));
		Assert.That(state.Update(80f, 3), Is.EqualTo(80f));
	}

	[Test]
	public void ValuesChangingEveryFrameKeepMoving() {
		// e.g. regenerating health, which changes the target in every frame
		TransitionState state = State(Linear(0.5f), typeof(float));
		state.Update(0f, 0);
		float target = 1;
		float shown = 0;
		for (int frame = 1; frame <= 60; frame++) {
			target += 0.01f;
			shown = (float)state.Update(target, frame / 60.0);
		}

		Assert.That(shown, Is.LessThanOrEqualTo(target)); // never overshoots with a linear curve
		Assert.That(target - shown, Is.LessThan(0.5f)); // follows, lagging behind by about the duration
	}

	[Test]
	public void EasedValuesChangingEveryFrameKeepMoving() {
		// curves that start slowly would barely move if every change started them again
		InterpolateTransition transition = new InterpolateTransition { duration = 0.5f, curve = TransitionCurve.EaseInOut() };
		TransitionState state = State(transition, typeof(float));
		state.Update(1f, 0);
		float target = 0.5f;
		float shown = 1;
		for (int frame = 1; frame <= 36; frame++) {
			target += 0.001f; // regenerating
			shown = (float)state.Update(target, frame / 60.0);
		}

		Assert.That(shown, Is.EqualTo(target).Within(0.01f)); // arrived after about the duration
	}

	[Test]
	public void LargeChangesWhileMovingStartAgain() {
		InterpolateTransition transition = Linear();
		transition.delay = 0.5f;
		TransitionState state = State(transition, typeof(float));
		state.Update(100f, 0);
		state.Update(80f, 0);
		Assert.That(state.Update(80f, 1), Is.EqualTo(90f).Within(0.01f)); // moving since 0.5
		state.Update(40f, 1); // another hit
		Assert.That(state.Update(40f, 1.4), Is.EqualTo(90f).Within(0.01f)); // waits for the delay again
		Assert.That(state.Update(40f, 2), Is.EqualTo(65f).Within(0.01f));
	}

	[Test]
	public void ChangesBackDuringTheDelayDontStartItAgain() {
		InterpolateTransition transition = Linear();
		transition.delay = 1;
		transition.animateWhen = AnimateWhen.Decreasing;
		TransitionState state = State(transition, typeof(float));
		state.Update(100f, 0);
		state.Update(80f, 0); // a hit
		state.Update(81f, 0.5); // regeneration while waiting
		Assert.That(state.Update(81f, 0.9), Is.EqualTo(100f));
		Assert.That(state.Update(81f, 1.5), Is.EqualTo(90.5f).Within(0.01f)); // moving to 81 since the original wait ended
	}

	[Test]
	public void OnlyTheChosenDirectionIsAnimated() {
		InterpolateTransition transition = Linear();
		transition.animateWhen = AnimateWhen.Decreasing;
		TransitionState state = State(transition, typeof(float));
		state.Update(50f, 0);
		Assert.That(state.Update(80f, 0), Is.EqualTo(80f)); // healing is shown right away
		state.Update(40f, 1);
		Assert.That(state.Update(40f, 1.5), Is.EqualTo(60f)); // damage is animated
	}

	[Test]
	public void SmallChangesAreQuicker() {
		InterpolateTransition transition = new InterpolateTransition { duration = 0.5f, fullDurationFrom = 100 };
		Assert.That(transition.GetDuration(50, 51, typeof(int)), Is.EqualTo(0.075f).Within(0.001f));
		Assert.That(transition.GetDuration(0, 50, typeof(int)), Is.EqualTo(0.426f).Within(0.001f));
		Assert.That(transition.GetDuration(1, 1000000, typeof(int)), Is.EqualTo(0.5f));
		transition.fullDurationFrom = 0;
		Assert.That(transition.GetDuration(50, 51, typeof(int)), Is.EqualTo(0.5f));
	}

	[Test]
	public void AnimatingFromAStartValue() {
		TransitionState state = State(Linear(), typeof(float));
		Assert.That(state.Update(1f, 0, () => 0f), Is.EqualTo(0f));
		Assert.That(state.Update(1f, 0.5), Is.EqualTo(0.5f));
	}

	[Test]
	public void SnappingShowsTheValueRightAway() {
		TransitionState state = State(Linear(), typeof(float));
		state.Update(0f, 0);
		state.Update(10f, 0);
		state.Snap(3f);
		Assert.That(state.Update(3f, 0.5), Is.EqualTo(3f));
		Assert.That(state.IsMoving, Is.False);
	}

	[TestCase(IntegerRounding.Nearest, 13)]
	[TestCase(IntegerRounding.TowardsStart, 12)]
	[TestCase(IntegerRounding.TowardsTarget, 13)]
	public void IntegersAreRounded(IntegerRounding rounding, int expected) {
		InterpolateTransition transition = Linear();
		transition.rounding = rounding;
		TransitionState state = State(transition, typeof(int));
		state.Update(10, 0);
		state.Update(20, 0);
		Assert.That(state.Update(20, 0.25), Is.EqualTo(expected)); // 12.5
	}

	[Test]
	public void TypesWithOperatorsAreBlended() {
		Assert.That(ValueBlending.CanBlend(typeof(Vec)), Is.True);
		Assert.That(ValueBlending.CanBlend(typeof(Item)), Is.False);
		TransitionState state = State(Linear(), typeof(Vec));
		state.Update(new Vec(0, 0), 0);
		state.Update(new Vec(10, 20), 0);
		Vec halfway = (Vec)state.Update(new Vec(10, 20), 0.5);
		Assert.That(halfway.x, Is.EqualTo(5f).Within(TOLERANCE));
		Assert.That(halfway.y, Is.EqualTo(10f).Within(TOLERANCE));
		Assert.That(ValueBlending.Distance(new Vec(0, 0), new Vec(3, 4), typeof(Vec)), Is.EqualTo(5).Within(TOLERANCE));
	}

	[Test]
	public void BlendingCanBeRegistered() {
		ValueBlending.Register<Money>((a, b) => new Money(a.cents + b.cents), (a, b) => new Money(a.cents - b.cents), (a, factor) => new Money((int)Math.Round(a.cents * factor)), a => Math.Abs(a.cents));
		Assert.That(ValueBlending.Lerp(new Money(0), new Money(100), 0.25, typeof(Money)), Is.EqualTo(new Money(25)));
		Assert.That(new InterpolateTransition().CanAnimate(typeof(Money)), Is.True);
	}

	[Test]
	public void SpringsSettleOnTheTarget() {
		TransitionState state = State(new SpringTransition { stiffness = 100, damping = 5 }, typeof(float));
		state.Update(0f, 0);
		state.Update(1f, 0);
		float maximum = 0;
		for (double time = 0; time < 5; time += 1 / 60.0) maximum = Math.Max(maximum, (float)state.Update(1f, time));
		Assert.That(maximum, Is.GreaterThan(1.2f)); // little damping overshoots
		Assert.That(state.Update(1f, 10), Is.EqualTo(1f));
		Assert.That(state.IsMoving, Is.False);
	}

	[Test]
	public void SpringsKeepTheirMomentum() {
		TransitionState state = State(new SpringTransition(), typeof(float));
		state.Update(0f, 0);
		state.Update(1f, 0);
		float beforeChange = (float)state.Update(1f, 0.1);
		state.Update(0f, 0.1); // back to where it started, but it keeps moving forward for a moment
		Assert.That((float)state.Update(0f, 0.12), Is.GreaterThan(beforeChange));
	}

	[Test]
	public void NumbersInTextCount() {
		NumbersInTextTransition transition = new NumbersInTextTransition { duration = 1, fullDurationFrom = 0, curve = TransitionCurve.Linear() };
		TransitionState state = State(transition, typeof(string));
		state.Update("50 gold", 0);
		state.Update("42 gold", 0);
		Assert.That(state.Update("42 gold", 0.5), Is.EqualTo("46 gold"));
		Assert.That(state.Update("42 gold", 1), Is.EqualTo("42 gold"));

		state.Update("1,000 of 2.50", 1); // the text changed in more than its numbers, shown right away
		Assert.That(state.Update("1,000 of 2.50", 1.1), Is.EqualTo("1,000 of 2.50"));
		state.Update("3,000 of 3.50", 2);
		Assert.That(state.Update("3,000 of 3.50", 2.5), Is.EqualTo("2,000 of 3.00")); // keeps the format of the new numbers
	}

	[Test]
	public void NumbersInTextOnlyAnimateTheChosenDirection() {
		NumbersInTextTransition transition = new NumbersInTextTransition { animateWhen = AnimateWhen.Increasing };
		TransitionState state = State(transition, typeof(string));
		state.Update("Gold: 10", 0);
		Assert.That(state.Update("Gold: 5", 0), Is.EqualTo("Gold: 5"));
		state.Update("Gold: 50", 0);
		Assert.That(state.IsMoving, Is.True);
	}

	[Test]
	public void ScrambledTextSettles() {
		ScrambleTransition transition = new ScrambleTransition { duration = 1, characters = "#" };
		TransitionState state = State(transition, typeof(string));
		state.Update("Night", 0);
		state.Update("Forest", 0);
		Assert.That(state.Update("Forest", 0.5), Is.EqualTo("For###")); // left to right, half of the 6 characters settled
		Assert.That(state.Update("Forest", 1), Is.EqualTo("Forest"));
	}

	[TestCase(TypewriterMode.KeepCommonStart, new[] { "Hello World", "Hello Wor", "Hello ", "Hello Th", "Hello There" })] // 5 characters deleted, then 5 typed
	[TestCase(TypewriterMode.TypeOnly, new[] { "", "He", "Hello", "Hello Th", "Hello There" })] // 11 characters typed
	public void TypewriterDeletesAndTypes(TypewriterMode mode, string[] expected) {
		TypewriterTransition transition = new TypewriterTransition { mode = mode, duration = 1, fullDurationFrom = 0 };
		TransitionState state = State(transition, typeof(string));
		state.Update("Hello World", 0);
		state.Update("Hello There", 0);
		for (int i = 0; i < expected.Length; i++) Assert.That(state.Update("Hello There", i / 4.0), Is.EqualTo(expected[i]), $"at {i / 4.0}");
	}

	[Test]
	public void SwitchesHappenAfterTheDelay() {
		TransitionState state = State(new SwitchTransition { delay = 0.5f }, typeof(bool));
		state.Update(false, 0);
		state.Update(true, 0);
		Assert.That(state.Update(true, 0.4), Is.EqualTo(false));
		Assert.That(state.Update(true, 0.5), Is.EqualTo(true));
	}

	[Test]
	public void Summaries() {
		Assert.That(new InterpolateTransition { duration = 0.5f }.GetSummary(), Is.EqualTo("Interpolate · 0.5 s"));
		Assert.That(new InterpolateTransition { duration = 0.5f, fullDurationFrom = 10, delay = 1, animateWhen = AnimateWhen.Decreasing }.GetSummary(), Is.EqualTo("Interpolate · up to 0.5 s · 1 s delay · when decreasing"));
		Assert.That(new SwitchTransition().GetSummary(), Is.EqualTo("Switch"));
		Assert.That(Transition.GetDisplayName(typeof(NumbersInTextTransition)), Is.EqualTo("Numbers in Text"));
	}
}
}
