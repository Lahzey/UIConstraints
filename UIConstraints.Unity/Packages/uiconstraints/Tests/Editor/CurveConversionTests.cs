using NUnit.Framework;
using UIConstraints.Core.Animation;
using UIConstraints.Unity.Animation;
using UnityEngine;

namespace UIConstraints.Unity.Tests {
/**
 * The curves of Core evaluate like Unity's AnimationCurve, so curves edited in Unity behave the same when converted.
 * Curves with weighted tangents may differ slightly (well within a percent of their range): Core solves their Bezier segments exactly, Unity approximates them.
 */
public class CurveConversionTests {
	private const int CURVES = 200;
	private const float TOLERANCE = 1e-3f;
	private const float WEIGHTED_TOLERANCE = 0.01f; // relative to the range of the curve

	[Test]
	public void RandomCurvesEvaluateLikeAnimationCurves() {
		System.Random random = new System.Random(1234);
		for (int c = 0; c < CURVES; c++) {
			AnimationCurve curve = RandomCurve(random, c % 2 == 0);
			TransitionCurve converted = curve.ToTransitionCurve();
			float tolerance = TOLERANCE;
			if (System.Array.Exists(curve.keys, k => k.weightedMode != WeightedMode.None)) tolerance = Mathf.Max(TOLERANCE, WEIGHTED_TOLERANCE * Range(curve));
			for (float time = -0.5f; time <= 1.5f; time += 0.01f)
				Assert.That(converted.Evaluate(time), Is.EqualTo(curve.Evaluate(time)).Within(tolerance), $"curve {c} at {time}: {Describe(curve)}");
		}
	}

	[Test]
	public void PresetsMatchUnity() {
		AnimationCurve easeInOut = AnimationCurve.EaseInOut(0, 0, 1, 1);
		for (float time = 0; time <= 1; time += 0.05f) Assert.That(TransitionCurve.EaseInOut().Evaluate(time), Is.EqualTo(easeInOut.Evaluate(time)).Within(TOLERANCE));
	}

	[Test]
	public void ConversionKeepsTheCurve() {
		AnimationCurve curve = RandomCurve(new System.Random(5), true);
		curve.postWrapMode = WrapMode.PingPong;
		AnimationCurve roundTrip = curve.ToTransitionCurve().ToAnimationCurve();
		Assert.That(roundTrip.keys, Is.EqualTo(curve.keys));
		Assert.That(roundTrip.postWrapMode, Is.EqualTo(WrapMode.PingPong));
	}

	// keys with random values and tangents, half of them with weighted tangents
	private static AnimationCurve RandomCurve(System.Random random, bool weighted) {
		int count = random.Next(2, 5);
		Keyframe[] keys = new Keyframe[count];
		for (int i = 0; i < count; i++) {
			float time = (float)i / (count - 1);
			keys[i] = new Keyframe(time, Value(random, 2), Value(random, 6), Value(random, 6));
			if (weighted && random.Next(2) == 0) {
				keys[i].weightedMode = (WeightedMode)random.Next(1, 4);
				keys[i].inWeight = (float)random.NextDouble();
				keys[i].outWeight = (float)random.NextDouble();
			}
		}

		return new AnimationCurve(keys);
	}

	// how far the values of the curve spread, sampled
	private static float Range(AnimationCurve curve) {
		float min = float.MaxValue;
		float max = float.MinValue;
		for (float time = 0; time <= 1; time += 0.01f) {
			min = Mathf.Min(min, curve.Evaluate(time));
			max = Mathf.Max(max, curve.Evaluate(time));
		}

		return max - min;
	}

	private static float Value(System.Random random, float range) => (float)(random.NextDouble() * 2 - 1) * range;

	private static string Describe(AnimationCurve curve) {
		return string.Join("; ", System.Array.ConvertAll(curve.keys, k => $"({k.time}, {k.value}, in {k.inTangent} w{k.inWeight}, out {k.outTangent} w{k.outWeight}, {k.weightedMode})"));
	}
}
}
