using System;
using System.IO;
using System.Linq;
using SkiaSharp;
using Svg.Skia;

namespace UIConstraints.Icons {
/**
 * Renders every SVG of the icons folder to PNGs for the Unity package, which can't use SVGs as component icons.
 * Each icon is rendered at 1x (16px, used by the inspector at normal resolution) and 2x (32px, which Unity picks on high resolution displays, named with the @2x suffix).
 * Usage: dotnet run --project UIConstraints.Icons/Converter [svg folder] [output folder], the folders default to those of this repository.
 * With --sheet [file] (instead of the folders), it renders a preview of all icons on the dark and light backgrounds of the Unity editor instead.
 */
public static class Program {
	private static readonly int[] SCALES = { 1, 2 };

	private static readonly SKColor[] SHEET_BACKGROUNDS = { new SKColor(56, 56, 56), new SKColor(200, 200, 200) }; // the inspector backgrounds of Unity's dark and light skin
	private const int SHEET_SCALE = 4;
	private const int SHEET_COLUMNS = 6;

	public static int Main(string[] args) {
		string repositoryRoot = FindRepositoryRoot();
		if (args.Length > 0 && args[0] == "--sheet") {
			RenderSheet(Path.Combine(repositoryRoot, "UIConstraints.Icons", "svg"), args.Length > 1 ? args[1] : "icons-sheet.png");
			return 0;
		}

		string svgFolder = args.Length > 0 ? args[0] : Path.Combine(repositoryRoot, "UIConstraints.Icons", "svg");
		string outputFolder = args.Length > 1 ? args[1] : Path.Combine(repositoryRoot, "UIConstraints.Unity", "Packages", "uiconstraints", "Editor", "Icons");
		Directory.CreateDirectory(outputFolder);

		string[] svgFiles = Directory.GetFiles(svgFolder, "*.svg").OrderBy(f => f, StringComparer.Ordinal).ToArray();
		foreach (string svgFile in svgFiles) {
			using SKSvg svg = new SKSvg();
			if (svg.Load(svgFile) == null) {
				Console.Error.WriteLine($"Could not load {svgFile}");
				return 1;
			}

			string name = Path.GetFileNameWithoutExtension(svgFile);
			foreach (int scale in SCALES) {
				string outputFile = Path.Combine(outputFolder, name + (scale == 1 ? "" : $"@{scale}x") + ".png");
				Render(svg.Picture, scale, outputFile);
			}
		}

		Console.WriteLine($"Rendered {svgFiles.Length} icons to {outputFolder}");
		return 0;
	}

	private static void Render(SKPicture picture, int scale, string outputFile) {
		SKRect bounds = picture.CullRect; // the size of the SVG (16x16)
		using SKBitmap bitmap = new SKBitmap((int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale), SKColorType.Rgba8888, SKAlphaType.Unpremul);
		using (SKCanvas canvas = new SKCanvas(bitmap)) {
			canvas.Clear(SKColors.Transparent);
			canvas.Scale(scale);
			canvas.DrawPicture(picture);
		}

		// unchanged icons are not written again, so Unity doesn't reimport them
		using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
		byte[] bytes = png.ToArray();
		if (File.Exists(outputFile) && File.ReadAllBytes(outputFile).AsSpan().SequenceEqual(bytes)) return;
		File.WriteAllBytes(outputFile, bytes);
	}

	// every icon on each background, enlarged without smoothing so single pixels can be judged
	private static void RenderSheet(string svgFolder, string outputFile) {
		string[] svgFiles = Directory.GetFiles(svgFolder, "*.svg").OrderBy(f => f, StringComparer.Ordinal).ToArray();
		int cell = 16 * SHEET_SCALE + 8;
		int rows = (svgFiles.Length + SHEET_COLUMNS - 1) / SHEET_COLUMNS;
		using SKBitmap sheet = new SKBitmap(cell * SHEET_COLUMNS * SHEET_BACKGROUNDS.Length, cell * rows);
		using (SKCanvas canvas = new SKCanvas(sheet)) {
			for (int b = 0; b < SHEET_BACKGROUNDS.Length; b++) {
				using SKPaint background = new SKPaint { Color = SHEET_BACKGROUNDS[b] };
				canvas.DrawRect(b * cell * SHEET_COLUMNS, 0, cell * SHEET_COLUMNS, cell * rows, background);
				for (int i = 0; i < svgFiles.Length; i++) {
					using SKSvg svg = new SKSvg();
					svg.Load(svgFiles[i]);
					using SKBitmap icon = new SKBitmap(16, 16);
					using (SKCanvas iconCanvas = new SKCanvas(icon)) {
						iconCanvas.Clear(SKColors.Transparent);
						iconCanvas.DrawPicture(svg.Picture);
					}

					SKRect target = SKRect.Create(b * cell * SHEET_COLUMNS + i % SHEET_COLUMNS * cell + 4, i / SHEET_COLUMNS * cell + 4, 16 * SHEET_SCALE, 16 * SHEET_SCALE);
					using SKImage image = SKImage.FromBitmap(icon);
					canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Nearest));
				}
			}
		}

		using FileStream stream = File.Create(outputFile);
		sheet.Encode(SKEncodedImageFormat.Png, 100).SaveTo(stream);
		Console.WriteLine($"Rendered a sheet of {svgFiles.Length} icons to {Path.GetFullPath(outputFile)}: " + string.Join(", ", svgFiles.Select(Path.GetFileNameWithoutExtension)));
	}

	// the folder containing UIConstraints.sln, searched upwards from the working directory
	private static string FindRepositoryRoot() {
		for (DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
			if (File.Exists(Path.Combine(directory.FullName, "UIConstraints.sln")))
				return directory.FullName;
		throw new InvalidOperationException("Run this inside the UIConstraints repository, or pass the svg and output folders as arguments");
	}
}
}
