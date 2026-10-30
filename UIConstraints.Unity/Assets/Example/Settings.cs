using System.Collections.Generic;
using UnityEngine;

namespace Example {
// a color scheme of the example UI, chosen in its settings
public class Theme {
	public readonly string name;
	public readonly Color background;
	public readonly Color panel;
	public readonly Color text;

	public Theme(string name, Color background, Color panel, Color text) {
		this.name = name;
		this.background = background;
		this.panel = panel;
		this.text = text;
	}

	// shown as the option of the theme dropdown
	public override string ToString() {
		return name;
	}
}

// what the settings panel of the example UI changes, which the rest of the UI uses (e.g. the panels have the color Settings.Theme.panel)
public static class Settings {
	public static readonly List<Theme> themes = new List<Theme> {
		new Theme("Night", new Color(0.09f, 0.1f, 0.14f), new Color(0.16f, 0.18f, 0.24f), new Color(0.92f, 0.93f, 0.96f)),
		new Theme("Paper", new Color(0.86f, 0.84f, 0.78f), new Color(0.97f, 0.96f, 0.92f), new Color(0.15f, 0.14f, 0.12f)),
		new Theme("Forest", new Color(0.07f, 0.14f, 0.1f), new Color(0.13f, 0.24f, 0.17f), new Color(0.88f, 0.95f, 0.88f))
	};

	public static int themeIndex;
	public static float accentHue = 0.1f;
	public static float panelOpacity = 1;
	public static float spacing = 12;
	public static float rowHeight = 44;
	public static bool animateBackground = true;

	public static Theme Theme => themes[themeIndex];
	public static Color Accent => Color.HSVToRGB(accentHue, 0.65f, 0.95f);
}

// what the tooltip of the inventory shows: the hovered item, set by the pointer events of the inventory slots
public static class Tooltip {
	public static Item item;
}
}
