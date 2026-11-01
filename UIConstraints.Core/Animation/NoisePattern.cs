using System;
using System.Collections.Generic;

namespace UIConstraints.Core.Animation;

public enum NoiseKind {
    [System.ComponentModel.Description("Square blocks, each switching at once")]
    Pixels,
    [System.ComponentModel.Description("Smooth blobs")]
    Smooth,
    [System.ComponentModel.Description("Blobs with finer detail at their edges")]
    Clouds
}

/**
 * A square noise pattern that repeats seamlessly, e.g. for the order in which a dissolve switches points. Its values are spread evenly from 0 to 255,
 * so a dissolve switches the same share of points at any time. Generated here, so it looks the same in every engine.
 */
public sealed class NoisePattern {
    public const int SIZE = 128;

    private static readonly Dictionary<(NoiseKind, int, int), NoisePattern> PATTERNS = new Dictionary<(NoiseKind, int, int), NoisePattern>();

    public readonly NoiseKind kind;
    public readonly int cells; // across the pattern
    public readonly int seed;
    private byte[] values;

    private NoisePattern(NoiseKind kind, int cells, int seed) {
        this.kind = kind;
        this.cells = cells;
        this.seed = seed;
    }

    // whether engines sample it smoothly (instead of the nearest pixel)
    public bool Smooth => kind != NoiseKind.Pixels;

    // row by row, SIZE x SIZE
    public byte[] Values => values ??= Generate();

    // the pattern, shared by everything using the same one (engines can keep a texture of it)
    public static NoisePattern Get(NoiseKind kind, int cells, int seed) {
        cells = Math.Max(1, Math.Min(SIZE, cells));
        lock (PATTERNS) {
            if (!PATTERNS.TryGetValue((kind, cells, seed), out NoisePattern pattern)) {
                pattern = new NoisePattern(kind, cells, seed);
                PATTERNS[(kind, cells, seed)] = pattern;
            }

            return pattern;
        }
    }

    // the value at a point (0 to 1 across the pattern, repeating), sampled like engines do
    public float Sample(float u, float v) {
        return Smooth ? Sampling.Bilinear(Values, SIZE, SIZE, u, v, true) : Sampling.Point(Values, SIZE, SIZE, u, v);
    }

    private byte[] Generate() {
        float[] noise = new float[SIZE * SIZE];
        for (int y = 0; y < SIZE; y++) {
            for (int x = 0; x < SIZE; x++) {
                float u = (x + 0.5f) / SIZE;
                float v = (y + 0.5f) / SIZE;
                noise[y * SIZE + x] = kind switch {
                    NoiseKind.Pixels => Hash((int)(u * cells), (int)(v * cells), seed),
                    NoiseKind.Smooth => ValueNoise(u, v, cells, seed),
                    _ => Clouds(u, v)
                };
            }
        }

        return Equalize(noise);
    }

    private float Clouds(float u, float v) {
        float sum = 0;
        float amplitude = 1;
        int octaveCells = cells;
        for (int octave = 0; octave < 4 && octaveCells <= SIZE; octave++) {
            sum += ValueNoise(u, v, octaveCells, seed + octave * 7919) * amplitude;
            amplitude *= 0.5f;
            octaveCells *= 2;
        }

        return sum;
    }

    // smoothly interpolated random values at the corners of the cells, repeating after the given number of cells
    private static float ValueNoise(float u, float v, int cells, int seed) {
        float x = u * cells;
        float y = v * cells;
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        float fx = Fade(x - x0);
        float fy = Fade(y - y0);
        float top = Lerp(Corner(x0, y0), Corner(x0 + 1, y0), fx);
        float bottom = Lerp(Corner(x0, y0 + 1), Corner(x0 + 1, y0 + 1), fx);
        return Lerp(top, bottom, fy);

        float Corner(int cx, int cy) => Hash(cx % cells, cy % cells, seed);
    }

    private static float Fade(float t) => t * t * (3 - 2 * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // a random value from 0 to 1 for a cell, always the same for the same cell and seed
    private static float Hash(int x, int y, int seed) {
        unchecked {
            uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            n = (n ^ (n >> 13)) * 1274126177;
            n ^= n >> 16;
            return (n & 0xffffff) / (float)0x1000000;
        }
    }

    // replaces each value by its rank among all values, so they are spread evenly (equal values keep being equal)
    private static byte[] Equalize(float[] noise) {
        float[] sorted = (float[])noise.Clone();
        Array.Sort(sorted);
        byte[] values = new byte[noise.Length];
        for (int i = 0; i < noise.Length; i++) {
            int first = LowerBound(sorted, noise[i]);
            int last = LowerBound(sorted, BitIncrement(noise[i])) - 1;
            double rank = (first + last) / 2.0 / (noise.Length - 1);
            values[i] = (byte)Math.Round(rank * 255);
        }

        return values;
    }

    private static int LowerBound(float[] sorted, float value) {
        int low = 0;
        int high = sorted.Length;
        while (low < high) {
            int middle = (low + high) / 2;
            if (sorted[middle] < value) low = middle + 1;
            else high = middle;
        }

        return low;
    }

    // the next larger float (MathF.BitIncrement is not in .NET Standard 2.1)
    private static float BitIncrement(float value) {
        int bits = BitConverter.SingleToInt32Bits(value);
        return BitConverter.Int32BitsToSingle(value >= 0 ? bits + 1 : bits - 1);
    }
}
