using System;
using System.Linq;

namespace UIConstraints.Core.Animation;

// which sides of a key use their weight (see CurveKey), the same values as Unity's WeightedMode
public enum WeightedSides {
    None = 0,
    In = 1,
    Out = 2,
    Both = 3
}

// what a curve does before its first and after its last key
public enum CurveWrapMode {
    Clamp, // keeps the value of the first or last key
    Loop,
    PingPong
}

/**
 * A key of a TransitionCurve, with the same meaning as a Keyframe of Unity's AnimationCurve, so curves convert between the two without changing their shape.
 * Tangents are slopes (value per time), weights the fraction of the time to the neighboring key their handle reaches (1/3 if not weighted, which makes the curve a Hermite spline).
 */
[Serializable]
public struct CurveKey {
    public float time;
    public float value;
    public float inTangent;
    public float outTangent;
    public float inWeight;
    public float outWeight;
    public WeightedSides weightedSides;

    public CurveKey(float time, float value, float inTangent = 0, float outTangent = 0) {
        this.time = time;
        this.value = value;
        this.inTangent = inTangent;
        this.outTangent = outTangent;
        inWeight = DEFAULT_WEIGHT;
        outWeight = DEFAULT_WEIGHT;
        weightedSides = WeightedSides.None;
    }

    public const float DEFAULT_WEIGHT = 1 / 3f;
}

/**
 * The curve transitions use to map their progress (0 to 1) to how far the value has moved (usually also 0 to 1, but it may overshoot, e.g. for a bounce).
 * Engines have their own curves (AnimationCurve in Unity, Curve in Godot), which convert to this one, as Core can't reference them.
 * Evaluates exactly like Unity's AnimationCurve, including weighted tangents and infinite tangents (which hold the value of the key until the next one).
 */
[Serializable]
public class TransitionCurve {
    public CurveKey[] keys = Array.Empty<CurveKey>();
    public CurveWrapMode preWrapMode = CurveWrapMode.Clamp;
    public CurveWrapMode postWrapMode = CurveWrapMode.Clamp;

    public TransitionCurve() { }

    public TransitionCurve(params CurveKey[] keys) {
        this.keys = keys;
    }

    public static TransitionCurve Linear() => new TransitionCurve(new CurveKey(0, 0, 1, 1), new CurveKey(1, 1, 1, 1));

    // starts slowly (quadratic)
    public static TransitionCurve EaseIn() => new TransitionCurve(new CurveKey(0, 0, 0, 0), new CurveKey(1, 1, 2, 2));

    // ends slowly (quadratic)
    public static TransitionCurve EaseOut() => new TransitionCurve(new CurveKey(0, 0, 2, 2), new CurveKey(1, 1, 0, 0));

    // starts and ends slowly, like Unity's AnimationCurve.EaseInOut
    public static TransitionCurve EaseInOut() => new TransitionCurve(new CurveKey(0, 0, 0, 0), new CurveKey(1, 1, 0, 0));

    // overshoots the target a little before settling on it
    public static TransitionCurve Overshoot() => new TransitionCurve(new CurveKey(0, 0, 3.5f, 3.5f), new CurveKey(0.65f, 1.1f, 0, 0), new CurveKey(1, 1, 0, 0));

    public TransitionCurve Clone() {
        return new TransitionCurve(keys.ToArray()) { preWrapMode = preWrapMode, postWrapMode = postWrapMode };
    }

    public float Evaluate(float time) {
        if (keys == null || keys.Length == 0) return 0;
        if (keys.Length == 1) return keys[0].value;

        float start = keys[0].time;
        float end = keys[keys.Length - 1].time;
        if (time < start) {
            if (preWrapMode == CurveWrapMode.Clamp) return keys[0].value;
            time = Wrap(time, start, end, preWrapMode);
        }
        else if (time > end) {
            if (postWrapMode == CurveWrapMode.Clamp) return keys[keys.Length - 1].value;
            time = Wrap(time, start, end, postWrapMode);
        }

        int index = 0;
        while (index < keys.Length - 2 && time >= keys[index + 1].time) index++;
        return EvaluateSegment(keys[index], keys[index + 1], time);
    }

    private static float Wrap(float time, float start, float end, CurveWrapMode mode) {
        float length = end - start;
        if (length <= 0) return start;

        float offset = (time - start) % (2 * length);
        if (offset < 0) offset += 2 * length;
        if (mode == CurveWrapMode.Loop) return start + offset % length;
        return start + (offset <= length ? offset : 2 * length - offset); // ping pong
    }

    private static float EvaluateSegment(CurveKey from, CurveKey to, float time) {
        float duration = to.time - from.time;
        if (duration <= 0) return to.value;
        if (float.IsInfinity(from.outTangent) || float.IsInfinity(to.inTangent)) return from.value; // a stepped curve

        float s = (time - from.time) / duration;
        bool weighted = (from.weightedSides & WeightedSides.Out) != 0 || (to.weightedSides & WeightedSides.In) != 0;
        if (!weighted) return EvaluateHermite(from.value, from.outTangent * duration, to.value, to.inTangent * duration, s);

        float outWeight = (from.weightedSides & WeightedSides.Out) != 0 ? from.outWeight : CurveKey.DEFAULT_WEIGHT;
        float inWeight = (to.weightedSides & WeightedSides.In) != 0 ? to.inWeight : CurveKey.DEFAULT_WEIGHT;
        return EvaluateBezier(from, to, duration, outWeight, inWeight, s);
    }

    // the cubic Hermite spline between two values with the given slopes (already scaled by the duration of the segment), at s from 0 to 1
    private static float EvaluateHermite(float p0, float m0, float p1, float m1, float s) {
        float s2 = s * s;
        float s3 = s2 * s;
        return (2 * s3 - 3 * s2 + 1) * p0 + (s3 - 2 * s2 + s) * m0 + (-2 * s3 + 3 * s2) * p1 + (s3 - s2) * m1;
    }

    /**
     * A segment with weighted tangents is a cubic Bezier curve whose handles reach the given fraction of the segment's duration (as AnimationCurve does it).
     * Its time is a cubic function of the Bezier parameter, which is solved for the given time first.
     */
    private static float EvaluateBezier(CurveKey from, CurveKey to, float duration, float outWeight, float inWeight, float s) {
        // the control points in normalized time (0 to 1), weights are fractions of the segment
        outWeight = (float)Clamp01(outWeight);
        inWeight = (float)Clamp01(inWeight);
        double x1 = outWeight;
        double x2 = 1 - inWeight;
        double y0 = from.value;
        double y1 = from.value + from.outTangent * duration * outWeight;
        double y2 = to.value - to.inTangent * duration * inWeight;
        double y3 = to.value;

        double u = SolveBezierParameter(x1, x2, s);
        double v = 1 - u;
        return (float)(v * v * v * y0 + 3 * v * v * u * y1 + 3 * v * u * u * y2 + u * u * u * y3);
    }

    // the parameter u at which the Bezier curve with the x coordinates 0, x1, x2, 1 reaches x (the x coordinates are monotonic, as the handles stay within the segment)
    private static double SolveBezierParameter(double x1, double x2, double x) {
        double low = 0;
        double high = 1;
        double u = x;
        for (int i = 0; i < 40; i++) {
            double v = 1 - u;
            double current = 3 * v * v * u * x1 + 3 * v * u * u * x2 + u * u * u;
            if (Math.Abs(current - x) < 1e-7) return u;
            if (current < x) low = u;
            else high = u;

            // Newton's method, falling back to bisection where it would leave the bracket
            double derivative = 3 * v * v * x1 + 6 * v * u * (x2 - x1) + 3 * u * u * (1 - x2);
            double next = derivative > 1e-9 ? u - (current - x) / derivative : (low + high) / 2;
            u = next > low && next < high ? next : (low + high) / 2;
        }

        return u;
    }

    private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
}
