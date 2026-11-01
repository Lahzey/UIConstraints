using System.Linq;
using UIConstraints.Core.Animation;
using UnityEngine;

namespace UIConstraints.Unity.Animation {
// converts between Unity's AnimationCurve and the TransitionCurve of Core, which evaluate the same (including weighted tangents)
public static class CurveConversion {
	public static TransitionCurve ToTransitionCurve(this AnimationCurve curve) {
		return new TransitionCurve(curve.keys.Select(k => new CurveKey(k.time, k.value, k.inTangent, k.outTangent) {
			inWeight = k.inWeight,
			outWeight = k.outWeight,
			weightedSides = (WeightedSides)k.weightedMode
		}).ToArray()) {
			preWrapMode = ToCurveWrapMode(curve.preWrapMode),
			postWrapMode = ToCurveWrapMode(curve.postWrapMode)
		};
	}

	public static AnimationCurve ToAnimationCurve(this TransitionCurve curve) {
		return new AnimationCurve((curve.keys ?? new CurveKey[0]).Select(k => new Keyframe(k.time, k.value, k.inTangent, k.outTangent, k.inWeight, k.outWeight) {
			weightedMode = (WeightedMode)k.weightedSides
		}).ToArray()) {
			preWrapMode = ToWrapMode(curve.preWrapMode),
			postWrapMode = ToWrapMode(curve.postWrapMode)
		};
	}

	private static CurveWrapMode ToCurveWrapMode(WrapMode mode) {
		return mode switch {
			WrapMode.Loop => CurveWrapMode.Loop,
			WrapMode.PingPong => CurveWrapMode.PingPong,
			_ => CurveWrapMode.Clamp
		};
	}

	private static WrapMode ToWrapMode(CurveWrapMode mode) {
		return mode switch {
			CurveWrapMode.Loop => WrapMode.Loop,
			CurveWrapMode.PingPong => WrapMode.PingPong,
			_ => WrapMode.ClampForever
		};
	}
}
}
