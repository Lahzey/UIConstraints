using System;

namespace UIConstraints.Core.Animation;

/**
 * How deep inside its shape each pixel of an image is: 0 at the edge (and outside of it), 1 at the deepest point. Edge Reveal grows and shrinks images along it.
 * Pixels from half opacity on count as inside, the border of the image counts as outside. The distances are exact (euclidean), at a reduced size (see MAX_SIZE).
 */
public sealed class DistanceField {
    // the longer side of the images it is computed from, engines sample it smoothly, which is more than enough for a growing edge
    public const int MAX_SIZE = 128;

    private const double FAR = 1e20;

    public readonly int width;
    public readonly int height;
    public readonly byte[] values; // row by row, in the order of the alpha it was computed from, 0 to 255

    private DistanceField(int width, int height, byte[] values) {
        this.width = width;
        this.height = height;
        this.values = values;
    }

    public static DistanceField Compute(AlphaImage image) {
        // with a border of outside pixels, so shapes touching the border of the image shrink from there too, and every row and column has an outside pixel
        int width = image.width + 2;
        int height = image.height + 2;
        double[] distances = new double[width * height];
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                bool inside = x > 0 && y > 0 && x <= image.width && y <= image.height && image.alpha[(y - 1) * image.width + x - 1] >= 128;
                distances[y * width + x] = inside ? FAR : 0;
            }
        }

        // squared distances, first along the columns, then the rows (Felzenszwalb and Huttenlocher)
        int length = Math.Max(width, height);
        double[] line = new double[length];
        double[] result = new double[length];
        int[] parabolas = new int[length];
        double[] bounds = new double[length + 1];
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) line[y] = distances[y * width + x];
            Transform(line, height, result, parabolas, bounds);
            for (int y = 0; y < height; y++) distances[y * width + x] = result[y];
        }

        for (int y = 0; y < height; y++) {
            Array.Copy(distances, y * width, line, 0, width);
            Transform(line, width, result, parabolas, bounds);
            Array.Copy(result, 0, distances, y * width, width);
        }

        double max = 0;
        foreach (double distance in distances) max = Math.Max(max, distance);
        max = Math.Sqrt(max);

        byte[] values = new byte[image.width * image.height];
        if (max > 0) {
            for (int y = 0; y < image.height; y++) {
                for (int x = 0; x < image.width; x++) {
                    double distance = Math.Sqrt(distances[(y + 1) * width + x + 1]);
                    values[y * image.width + x] = (byte)Math.Round(distance / max * 255);
                }
            }
        }

        return new DistanceField(image.width, image.height, values);
    }

    // the squared distance of each point to the nearest point with 0, along one line (the lower envelope of the parabolas rooted at each point)
    private static void Transform(double[] line, int length, double[] result, int[] parabolas, double[] bounds) {
        int count = 0;
        parabolas[0] = 0;
        bounds[0] = double.NegativeInfinity;
        bounds[1] = double.PositiveInfinity;
        for (int q = 1; q < length; q++) {
            double intersection;
            while (true) {
                // where the new parabola gets lower than the last one of the envelope, which is removed if that is before where the last one starts
                int p = parabolas[count];
                intersection = (line[q] + (double)q * q - (line[p] + (double)p * p)) / (2.0 * q - 2.0 * p);
                if (intersection > bounds[count] || count == 0) break;
                count--;
            }

            count++;
            parabolas[count] = q;
            bounds[count] = intersection;
            bounds[count + 1] = double.PositiveInfinity;
        }

        int k = 0;
        for (int q = 0; q < length; q++) {
            while (bounds[k + 1] < q) k++;
            int p = parabolas[k];
            result[q] = (double)(q - p) * (q - p) + line[p];
        }
    }

    // the depth at a point (u and v 0 to 1 across the image, in the order of the rows), interpolated between pixels like engines sample it
    public float Sample(float u, float v) {
        return Sampling.Bilinear(values, width, height, u, v, false);
    }
}

internal static class Sampling {
    // a value of a byte image (0 to 1) at a point (0 to 1 across the image, pixel centers at half a pixel), either clamped at the border or repeating
    public static float Bilinear(byte[] values, int width, int height, float u, float v, bool repeat) {
        float x = u * width - 0.5f;
        float y = v * height - 0.5f;
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        float fx = x - x0;
        float fy = y - y0;
        float top = Lerp(Get(x0, y0), Get(x0 + 1, y0), fx);
        float bottom = Lerp(Get(x0, y0 + 1), Get(x0 + 1, y0 + 1), fx);
        return Lerp(top, bottom, fy) / 255f;

        float Get(int px, int py) {
            px = repeat ? Mod(px, width) : Math.Max(0, Math.Min(width - 1, px));
            py = repeat ? Mod(py, height) : Math.Max(0, Math.Min(height - 1, py));
            return values[py * width + px];
        }
    }

    public static float Point(byte[] values, int width, int height, float u, float v) {
        int x = Mod((int)Math.Floor(u * width), width);
        int y = Mod((int)Math.Floor(v * height), height);
        return values[y * width + x] / 255f;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static int Mod(int value, int divisor) => (value % divisor + divisor) % divisor;
}
