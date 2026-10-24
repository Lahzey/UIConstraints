using System;
using System.Collections.Generic;
using System.Linq;
using UIConstraints.Core.Completion;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using UnityEditor;
using UnityEngine;

namespace UIConstraints.Unity.Inspector {
public class CodeArea {
	private static readonly string ENGLISH_CHARACTERS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~ \t\n\r";
	private static readonly Dictionary<int, Font> CONSOLAS_FONTS = new Dictionary<int, Font>(); // <size, font>

	private static readonly Vector2 PADDING = new Vector2(5, 2); // left, top and right, bottom both have this padding
	public static readonly int MAX_AUTO_COMPLETE_OPTIONS = 5;
	private static readonly string[] EDIT_COMMANDS = { "Copy", "Cut", "Paste", "SelectAll" }; // Unity sends these shortcuts as commands instead of key events

	private static readonly GUIStyle CODE_SEGMENT_STYLE;

	static CodeArea() {
		CODE_SEGMENT_STYLE = new GUIStyle(GUI.skin.label);
		CODE_SEGMENT_STYLE.margin = new RectOffset(0, 0, 0, 0);
		CODE_SEGMENT_STYLE.border = new RectOffset(0, 0, 0, 0);
		CODE_SEGMENT_STYLE.padding = new RectOffset(0, 0, 0, 0);
		CODE_SEGMENT_STYLE.alignment = TextAnchor.UpperLeft;
	}

	// usings may be modified, as accepting an auto complete option can add the namespace it requires
	public static string ScrolledArea(Rect position, string input, List<string> usings, ParseResult parseResult, GUIStyle style, out Vector2 preferredSize) {
		int controlId = GUIUtility.GetControlID(FocusType.Keyboard);
		bool hasFocus = GUIUtility.keyboardControl == controlId;
		CodeAreaState state = (CodeAreaState)GUIUtility.GetStateObject(typeof(CodeAreaState), controlId);

		CODE_SEGMENT_STYLE.font = GetConsolasFont(style.fontSize > 0 ? style.fontSize : style.font.fontSize);
		CODE_SEGMENT_STYLE.fontSize = CODE_SEGMENT_STYLE.font.fontSize; // just to be sure
		state.ContentStyle = CODE_SEGMENT_STYLE;
		state.Theme = EditorGUIUtility.isProSkin ? CodeAreaTheme.DEFAULT_DARK : CodeAreaTheme.DEFAULT_LIGHT;
		state.SetLines(input.Split('\n'));

		Rect inputBounds = new Rect(position.position + PADDING, position.size - PADDING * 2);
		switch (Event.current.GetTypeForControl(controlId)) {
			case EventType.KeyDown:
				if (!hasFocus) break; // key events are sent to every control, only the focused one may handle them
				string newInput = HandleKeyDownEvent(input, usings, state, out bool canShowAutoComplete);
				if (newInput != input) {
					if (canShowAutoComplete) state.CreateAutoComplete(newInput, new ParseContext(usings), false);
					else state.HideAutoComplete();
					GUI.changed = true;
				}

				input = newInput;
				break;
			case EventType.ValidateCommand:
				if (hasFocus && IsEditCommand(Event.current.commandName)) Event.current.Use(); // tells Unity that this control handles the command, which it then sends as ExecuteCommand
				break;
			case EventType.ExecuteCommand:
				if (!hasFocus || !IsEditCommand(Event.current.commandName)) break;
				string commandInput = HandleEditCommand(input, state, Event.current.commandName);
				if (commandInput != input) GUI.changed = true;
				input = commandInput;
				state.HideAutoComplete();
				Event.current.Use();
				break;
			case EventType.MouseDown:
				if (!inputBounds.Contains(Event.current.mousePosition)) {
					// clicking elsewhere removes the focus, but the event must be left for whatever was clicked (e.g. another code area)
					if (hasFocus) {
						GUIUtility.keyboardControl = 0;
						state.HideAutoComplete();
					}

					break;
				}

				GUIUtility.keyboardControl = controlId;
				GUIUtility.hotControl = controlId; // keeps drag events coming to this control, even outside of its bounds
				int clickedIndex = state.GetTextIndexAtPosition(Event.current.mousePosition - inputBounds.position);
				if (Event.current.clickCount == 2) SelectWordAt(input, state, clickedIndex);
				else if (Event.current.clickCount >= 3) SelectLineAt(input, state, clickedIndex);
				else if (Event.current.shift) state.selectionEndInternal = clickedIndex;
				else state.SetCursor(clickedIndex);
				Event.current.Use();
				state.HideAutoComplete();
				break;
			case EventType.MouseDrag:
				if (GUIUtility.hotControl != controlId) break;
				state.selectionEndInternal = state.GetTextIndexAtPosition(Event.current.mousePosition - inputBounds.position);
				Event.current.Use();
				state.HideAutoComplete();
				break;
			case EventType.MouseUp:
				if (GUIUtility.hotControl != controlId) break;
				GUIUtility.hotControl = 0;
				Event.current.Use();
				break;
			case EventType.Repaint:
				Draw(position, input, parseResult, hasFocus, state);
				break;
		}

		preferredSize = GetCodeAreaSize(state, position.width);

		return input;
	}

	private static void Draw(Rect position, string input, ParseResult parseResult, bool hasFocus, CodeAreaState state) {
		Color oldColor = GUI.color;
		GUIDrawUtils.FillRoundRect(position, state.Theme.BackgroundColor, 5);
		GUIDrawUtils.DrawRoundRect(position, hasFocus ? state.Theme.SelectedBorderColor : state.Theme.NormalBorderColor, 2, 5);
		Rect codeAreaBounds = new Rect(Vector2.zero, GetCodeAreaSize(state, position.width));
		state.scrollPosition = GUI.BeginScrollView(position, state.scrollPosition, codeAreaBounds);
		Rect contentBounds = new Rect(codeAreaBounds.position + PADDING, codeAreaBounds.size - PADDING * 2);
		GUI.BeginGroup(contentBounds);

		EditorGUIUtility.AddCursorRect(new Rect(Vector2.zero, contentBounds.size), MouseCursor.Text);

		#region Selection

		if (hasFocus) {
			Vector2 cursorPosition = state.GetPositionAtTextIndex(state.selectionEndInternal);

			// draw a vertical line for the cursor
			GUIDrawUtils.FillRoundRect(new Rect(cursorPosition, new Vector2(1, state.LineHeight)), state.Theme.CursorColor, 1);

			// if there is a selection, draw a colored rectangle over it (text is drawn later, so it acts as a background)
			if (state.SelectionStart != state.SelectionEnd) {
				string selection = input.Substring(state.SelectionStart, state.SelectionEnd - state.SelectionStart);
				string[] lines = selection.Split('\n');
				int index = state.SelectionStart;
				GUI.color = state.Theme.SelectionTextBackgroundColor;
				foreach (string line in lines) {
					Vector2 selectionStartPosition = state.GetPositionAtTextIndex(index);
					Vector2 selectionEndPosition = state.GetPositionAtTextIndex(index + line.Length);
					GUI.DrawTexture(new Rect(selectionStartPosition, new Vector2(selectionEndPosition.x - selectionStartPosition.x, state.LineHeight)), EditorGUIUtility.whiteTexture);
					index += line.Length + 1;
				}
			}
		}

		#endregion

		#region Errors

		if (parseResult != null && parseResult.Exception != null)
			for (int i = 0; i < parseResult.Exception.MessageCount(); i++) {
				// Debug.LogWarning("Error in Code Input Field: " + parseResult.Exception.GetMessage(i));
				GUIDrawUtils.DrawSquigglyLine(parseResult.Exception.GetStartPosition(i), parseResult.Exception.GetEndPosition(i), state);
			}

		#endregion

		#region CodeElements

		int charCountBeforeLine = 0;
		for (int lineIndex = 0; lineIndex < state.Lines.Length; lineIndex++) {
			Expression currentExpression = null;
			int currentExpressionStart = 0;
			int y = lineIndex * state.LineHeight;
			for (int indexInLine = 0; indexInLine < state.lineLengths[lineIndex]; indexInLine++) {
				Expression expression = parseResult?.ExpressionAtPosition(charCountBeforeLine + indexInLine); // expressions are mapped by their index in the whole source

				if (expression != currentExpression) {
					if (currentExpressionStart < indexInLine)
						// finish up current expression
						StylizedTextField(new Vector2(currentExpressionStart * state.CharWidth, y), state.Lines[lineIndex].Substring(currentExpressionStart, indexInLine - currentExpressionStart), currentExpression, state);

					currentExpression = expression;
					currentExpressionStart = indexInLine;
				}
			}

			StylizedTextField(new Vector2(currentExpressionStart * state.CharWidth, y), state.Lines[lineIndex].Substring(currentExpressionStart, state.lineLengths[lineIndex] - currentExpressionStart), currentExpression, state);
			charCountBeforeLine += state.lineLengths[lineIndex] + 1; // +1 for the newline character
		}

		#endregion


		GUI.EndGroup();
		GUI.EndScrollView();

		#region AutoComplete

		if (hasFocus && state.ShowAutoComplete) {
			List<AutoCompleteOption> visibleOptions = state.autoCompleteOptions.Skip(state.AutoCompleteIndex).Take(MAX_AUTO_COMPLETE_OPTIONS).ToList();
			int longestLabel = visibleOptions.Aggregate(0, (current, option) => Mathf.Max(current, option.label.Length));
			int longestDetail = visibleOptions.Aggregate(0, (current, option) => Mathf.Max(current, option.detail.Length));

			Vector2 positionAtCursor = position.position + contentBounds.position + state.GetPositionAtTextIndex(state.selectionEndInternal);
			Vector2 autoCompleteWindowSize = new Vector2((longestLabel + 3 + longestDetail) * state.CharWidth, visibleOptions.Count * state.LineHeight);
			float maxY = position.y + position.height;
			float maxWindowY = positionAtCursor.y + autoCompleteWindowSize.y;
			if (maxWindowY > maxY) positionAtCursor.y -= maxWindowY - maxY;
			Rect bounds = new Rect(positionAtCursor, autoCompleteWindowSize);

			GUI.BeginClip(bounds);
			bounds.position = Vector2.zero;
			GUIDrawUtils.FillRoundRect(bounds, state.Theme.AutoCompleteNormalBackgroundColor, 0);
			GUIDrawUtils.DrawRoundRect(bounds, state.Theme.AutoCompleteBorderColor, 1, 0);

			for (int i = 0; i < visibleOptions.Count; i++) {
				AutoCompleteOption option = visibleOptions[i];
				float y = bounds.y + i * state.LineHeight;

				if (state.AutoCompleteIndex + i == state.AutoCompleteSelection) GUIDrawUtils.FillRoundRect(new Rect(bounds.x, y, bounds.width, state.LineHeight), state.Theme.AutoCompleteSelectedBackgroundColor, 0);

				GUI.color = Color.black;
				GUI.Label(new Rect(bounds.x, y, (longestLabel + 3) * state.CharWidth, state.LineHeight), option.label, state.ContentStyle);
				GUI.color = Color.gray;
				GUI.Label(new Rect(bounds.x + (longestLabel + 3) * state.CharWidth, y, longestDetail * state.CharWidth, state.LineHeight), option.detail, state.ContentStyle);
			}

			GUI.EndClip();
		}

		#endregion

		GUI.color = oldColor;
	}

	private static string HandleKeyDownEvent(string input, List<string> usings, CodeAreaState state, out bool canShowAutoComplete) {
		// ok, this is so stupid, on Windows we get a correct KeyCode with a null key char and a None KeyCode with the correct char for each input character
		// so in a press of 'a' would result in the events [KeyCode.A, char \0] and [KeyCode.None, char 'a']
		// char \0 is very problematic because inserting it into a string will end that string at that position
		canShowAutoComplete = false;
		switch (Event.current.keyCode) {
			case KeyCode.Backspace:
				if (state.SelectionStart == state.SelectionEnd) {
					if (state.SelectionStart > 0) {
						input = input.Remove(state.SelectionStart - 1, 1);
						state.SetCursor(state.SelectionStart - 1);
					}
				}
				else {
					input = input.Remove(state.SelectionStart, state.SelectionEnd - state.SelectionStart);
					state.SetCursor(state.SelectionStart);
				}

				canShowAutoComplete = state.ShowAutoComplete; // keep updating the options while they are shown
				Event.current.Use();
				break;
			case KeyCode.Delete:
				if (state.SelectionStart == state.SelectionEnd) {
					if (state.SelectionStart < input.Length) input = input.Remove(state.SelectionStart, 1);
				}
				else {
					input = input.Remove(state.SelectionStart, state.SelectionEnd - state.SelectionStart);
					state.SetCursor(state.SelectionStart);
				}

				canShowAutoComplete = state.ShowAutoComplete; // keep updating the options while they are shown
				Event.current.Use();
				break;
			case KeyCode.Return: // enter
			case KeyCode.Tab:
				if (state.ShowAutoComplete) {
					input = AcceptAutoComplete(input, usings, state);
				}
				else {
					input = ReplaceSelection(input, state, Event.current.keyCode == KeyCode.Return ? "\n" : "\t");
				}

				Event.current.Use();
				break;
			case KeyCode.Escape:
				if (state.ShowAutoComplete) {
					state.HideAutoComplete();
					Event.current.Use();
				}

				break;
			case KeyCode.LeftArrow:
			case KeyCode.RightArrow:
				int direction = Event.current.keyCode == KeyCode.LeftArrow ? -1 : 1;
				if (Event.current.shift) state.selectionEndInternal += direction;
				else state.SetCursor(state.selectionEndInternal + direction);
				state.ClampSelection(input.Length);
				state.HideAutoComplete(); // the options are only valid for the position they were created at
				Event.current.Use();
				break;
			case KeyCode.UpArrow:
			case KeyCode.DownArrow:
				bool up = Event.current.keyCode == KeyCode.UpArrow;
				if (state.ShowAutoComplete) {
					state.AutoCompleteSelection = Math.Clamp(state.AutoCompleteSelection + (up ? -1 : 1), 0, state.autoCompleteOptions.Count - 1);
					if (state.AutoCompleteSelection > Math.Min(state.AutoCompleteIndex + MAX_AUTO_COMPLETE_OPTIONS - 1, state.autoCompleteOptions.Count - 1)) state.AutoCompleteIndex++;
					else if (state.AutoCompleteSelection < state.AutoCompleteIndex) state.AutoCompleteIndex--;
				}
				else {
					int index = 0;
					for (int i = 0; i < state.Lines.Length; i++) {
						int lineLength = state.lineLengths[i];
						if (index + lineLength >= state.selectionEndInternal) {
							int lineIndex = state.selectionEndInternal - index;
							int targetLine = Math.Clamp(up ? i - 1 : i + 1, 0, state.Lines.Length - 1);
							if (targetLine == i) break;

							int targetLineIndex = Math.Clamp(lineIndex, 0, state.lineLengths[targetLine]);
							int targetLineStartIndex = up ? index - (state.lineLengths[targetLine] + 1) : index + lineLength + 1; // +1 for the newline character
							state.SetCursor(targetLineStartIndex + targetLineIndex);
							state.ClampSelection(input.Length); // just in case, should not be necessary
							break;
						}

						index += lineLength + 1;
					}
				}

				Event.current.Use();
				break;
			default:
				char c = Event.current.character;

				// with ctrl held keys are shortcuts, copy, paste etc. arrive as commands (see HandleEditCommand). AltGr is reported as ctrl + alt on Windows, but types characters like '|' on some layouts
				if ((Event.current.control || Event.current.command) && !Event.current.alt) {
					if (c == ' ' || Event.current.keyCode == KeyCode.Space) {
						state.CreateAutoComplete(input, new ParseContext(usings), true); // this will refresh even if the autocomplete is already shown, to allow the user to refresh options if the tool bugs out
						Event.current.Use();
					}

					break;
				}

				if (c >= 32 && !char.IsControl(c)) { // visible characters
					input = ReplaceSelection(input, state, c.ToString());
					Event.current.Use();
					canShowAutoComplete = !char.IsWhiteSpace(c);
				}

				break;
		}

		return input;
	}

	private static string AcceptAutoComplete(string input, List<string> usings, CodeAreaState state) {
		AutoCompleteOption option = state.autoCompleteOptions[state.AutoCompleteSelection];
		int cursor = state.SelectionEnd;
		state.HideAutoComplete();
		if (state.AutoCompleteReplaceStart > cursor) return input; // the cursor moved in front of the completed identifier, the option no longer makes sense

		// replace the identifier typed so far with the option
		state.selectionStartInternal = state.AutoCompleteReplaceStart;
		state.selectionEndInternal = cursor;
		input = ReplaceSelection(input, state, option.insertText);
		state.SetCursor(state.SelectionEnd + option.cursorOffset);

		if (option.requiredUsing != null && !usings.Contains(option.requiredUsing)) usings.Add(option.requiredUsing);
		return input;
	}

	// selects the identifier (or number) at a cursor position, like IDEs do on double click. Next to anything else, only that character is selected
	private static void SelectWordAt(string input, CodeAreaState state, int index) {
		// the cursor position is between two characters, prefer the one after it unless only the one before is part of a word (e.g. clicking right of the last letter)
		int charIndex = index < input.Length && IsWordCharacter(input[index]) ? index : index - 1;
		if (charIndex < 0 || charIndex >= input.Length || !IsWordCharacter(input[charIndex])) {
			charIndex = Math.Min(index, input.Length - 1);
			if (charIndex < 0 || input[charIndex] == '\n') state.SetCursor(index);
			else {
				state.selectionStartInternal = charIndex;
				state.selectionEndInternal = charIndex + 1;
			}

			return;
		}

		int start = charIndex;
		int end = charIndex + 1;
		while (start > 0 && IsWordCharacter(input[start - 1])) start--;
		while (end < input.Length && IsWordCharacter(input[end])) end++;
		state.selectionStartInternal = start;
		state.selectionEndInternal = end;
	}

	private static void SelectLineAt(string input, CodeAreaState state, int index) {
		state.selectionStartInternal = index > 0 ? input.LastIndexOf('\n', index - 1) + 1 : 0;
		int lineEnd = input.IndexOf('\n', index);
		state.selectionEndInternal = lineEnd < 0 ? input.Length : lineEnd;
	}

	private static bool IsWordCharacter(char c) {
		return char.IsLetterOrDigit(c) || c == '_';
	}

	private static bool IsEditCommand(string commandName) {
		return Array.IndexOf(EDIT_COMMANDS, commandName) >= 0;
	}

	private static string HandleEditCommand(string input, CodeAreaState state, string commandName) {
		state.ClampSelection(input.Length);
		string selection = input.Substring(state.SelectionStart, state.SelectionEnd - state.SelectionStart);
		switch (commandName) {
			case "Copy":
				if (selection.Length > 0) GUIUtility.systemCopyBuffer = selection;
				break;
			case "Cut":
				if (selection.Length > 0) {
					GUIUtility.systemCopyBuffer = selection;
					input = ReplaceSelection(input, state, "");
				}

				break;
			case "Paste":
				input = ReplaceSelection(input, state, GUIUtility.systemCopyBuffer.Replace("\r\n", "\n")); // the code area only uses \n for line breaks
				break;
			case "SelectAll":
				state.selectionStartInternal = 0;
				state.selectionEndInternal = input.Length;
				break;
		}

		return input;
	}

	private static string ReplaceSelection(string input, CodeAreaState state, string replacement) {
		if (state.SelectionStart != state.SelectionEnd) input = input.Remove(state.SelectionStart, state.SelectionEnd - state.SelectionStart);
		input = input.Insert(state.SelectionStart, replacement);
		state.SetCursor(state.SelectionStart + replacement.Length);
		return input;
	}

	private static Vector2 GetCodeAreaSize(CodeAreaState state, float minWidth) {
		int maxLineLength = state.lineLengths.Prepend(0).Max();
		Vector2 size = new Vector2(maxLineLength * state.CharWidth, state.Lines.Length * state.LineHeight) + PADDING * 2;
		if (size.x < minWidth) size.x = minWidth;
		return size;
	}

	private static void StylizedTextField(Vector2 position, string text, Expression expression, CodeAreaState state) {
		GUI.color = state.Theme.GetExpressionColor(expression);
		GUI.Label(new Rect(position, new Vector2(text.Length * state.CharWidth, state.LineHeight)), text, state.ContentStyle);
	}

	// the draw functions using handles are generated by ChatGPT
	private static Font GetConsolasFont(int size) {
		Font font;
		if (!CONSOLAS_FONTS.ContainsKey(size) || CONSOLAS_FONTS[size] == null) {
			font = Font.CreateDynamicFontFromOSFont("Consolas", size);
			CONSOLAS_FONTS[size] = font;
		} else font = CONSOLAS_FONTS[size];
		// according to Unity we should request the characters every frame to ensure they stay loaded
		font.RequestCharactersInTexture(ENGLISH_CHARACTERS, size, FontStyle.Normal);
		return font;
	}
}
}
