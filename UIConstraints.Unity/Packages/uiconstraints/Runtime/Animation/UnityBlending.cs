using UIConstraints.Core.Animation;
using UnityEngine;

namespace UIConstraints.Unity.Animation {
/**
 * Registers how Unity types are blended (see ValueBlending), and how images are faded (see UnityImageFading). Vectors and colors would work through their operators too, registering them is faster
 * and gives colors a magnitude (so springs know when they settled). Rects have no operators.
 */
public static class UnityBlending {
	private static bool registered;

	public static void EnsureRegistered() {
		if (registered) return;
		registered = true;
		ImageFading.backend = new UnityImageFading();

		ValueBlending.Register<Vector2>((a, b) => a + b, (a, b) => a - b, (a, factor) => a * (float)factor, a => a.magnitude);
		ValueBlending.Register<Vector3>((a, b) => a + b, (a, b) => a - b, (a, factor) => a * (float)factor, a => a.magnitude);
		ValueBlending.Register<Vector4>((a, b) => a + b, (a, b) => a - b, (a, factor) => a * (float)factor, a => a.magnitude);
		ValueBlending.Register<Color>((a, b) => a + b, (a, b) => a - b, (a, factor) => a * (float)factor, a => Mathf.Sqrt(a.r * a.r + a.g * a.g + a.b * a.b + a.a * a.a));
		ValueBlending.Register<Rect>(
			(a, b) => new Rect(a.x + b.x, a.y + b.y, a.width + b.width, a.height + b.height),
			(a, b) => new Rect(a.x - b.x, a.y - b.y, a.width - b.width, a.height - b.height),
			(a, factor) => new Rect(a.x * (float)factor, a.y * (float)factor, a.width * (float)factor, a.height * (float)factor),
			a => Mathf.Sqrt(a.x * a.x + a.y * a.y + a.width * a.width + a.height * a.height));
	}
}
}
