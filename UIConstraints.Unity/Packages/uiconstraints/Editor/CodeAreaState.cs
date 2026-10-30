using System;
using System.Collections.Generic;
using UIConstraints.Core.Completion;
using UIConstraints.Core.Parsing;
using UnityEngine;

namespace UIConstraints.Unity.Inspector {
public class CodeAreaState {
	public readonly List<AutoCompleteOption> autoCompleteOptions = new List<AutoCompleteOption>();
	public readonly List<int> lineLengths = new List<int>();

	private GUIStyle contentStyle;
	public Vector2 scrollPosition;
	public int selectionEndInternal;

	public int selectionStartInternal;
	public int SelectionStart => Mathf.Min(selectionStartInternal, selectionEndInternal);
	public int SelectionEnd => Mathf.Max(selectionStartInternal, selectionEndInternal);
	public bool ShowAutoComplete { get; private set; }
	public int AutoCompleteIndex { get; set; }
	public int AutoCompleteSelection { get; set; }
	public int AutoCompleteReplaceStart { get; private set; } // the text from here to the cursor is replaced when an option is accepted

	public GUIStyle ContentStyle {
		get => contentStyle;
		set {
			contentStyle = value;
			LineHeight = ContentStyle.font.lineHeight;
			CharWidth = MeasureCharWidth(value);
		}
	}

	public int LineHeight { get; private set; }
	public float CharWidth { get; private set; }

	private static readonly Dictionary<(Font, int), float> CHAR_WIDTHS = new Dictionary<(Font, int), float>();

	/**
	 * The width of a character in GUI points, as the style draws it (all characters of a monospace font are equally wide).
	 * Measured over many characters, as the width is not a whole number: the advance of the characters in the font is rounded (e.g. 7 for Consolas at size 12, which is drawn 6.6 wide),
	 * which put the cursor further and further right of the text the longer the line was.
	 */
	private static float MeasureCharWidth(GUIStyle style) {
		(Font, int) key = (style.font, style.fontSize);
		if (CHAR_WIDTHS.TryGetValue(key, out float width)) return width;

		const int count = 100;
		width = style.CalcSize(new GUIContent(new string('m', count))).x / count;
		CHAR_WIDTHS[key] = width;
		return width;
	}
	public string[] Lines { get; private set; }

	public CodeAreaTheme Theme { get; set; }

	public void SetCursor(int index) {
		selectionStartInternal = index;
		selectionEndInternal = index;
	}

	public void ClampSelection(int inputLength) {
		selectionStartInternal = Math.Clamp(selectionStartInternal, 0, inputLength);
		selectionEndInternal = Math.Clamp(selectionEndInternal, 0, inputLength);
	}

	public void SetLines(string[] lines) {
		Lines = lines;
		lineLengths.Clear();
		foreach (string line in lines) lineLengths.Add(line.Length);
	}

	public void CreateAutoComplete(string input, ParseContext context, bool explicitRequest, ParseTarget target) {
		HideAutoComplete();

		autoCompleteOptions.AddRange(AutoComplete.GetOptions(input, SelectionEnd, context, explicitRequest, out int replaceStart, target));
		AutoCompleteReplaceStart = replaceStart;
		ShowAutoComplete = autoCompleteOptions.Count > 0;
	}

	public void HideAutoComplete() {
		ShowAutoComplete = false;
		autoCompleteOptions.Clear();
		AutoCompleteIndex = 0;
		AutoCompleteSelection = 0;
	}

	public int GetTextIndexAtPosition(Vector2 position) {
		int lineIndex = Math.Clamp(Mathf.FloorToInt(position.y / LineHeight), 0, lineLengths.Count - 1);
		int lineLength = lineLengths[lineIndex];
		int charIndex = Math.Clamp(Mathf.RoundToInt(position.x / CharWidth), 0, lineLength);
		int textIndex = 0;
		for (int i = 0; i < lineIndex; i++) textIndex += lineLengths[i] + 1; // +1 for newline
		textIndex += charIndex;
		return textIndex;
	}

	public Vector2 GetPositionAtTextIndex(int textIndex) {
		int index = 0;
		for (int i = 0; i < lineLengths.Count; i++) {
			int lineLength = lineLengths[i];
			if (textIndex <= index + lineLength) // textIndex == index+lineLength means position is to the right of the last character on the line
			{
				int indexInLine = textIndex - index;
				return new Vector2(indexInLine * CharWidth, i * LineHeight);
			}

			index += lineLength + 1; // +1 for newline
		}

		return new Vector2(0, lineLengths.Count * LineHeight); // shouldn't happen, but default return is required
	}
}
}
