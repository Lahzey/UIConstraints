using System.Text;

namespace UIConstraints.Core.Parsing;

public class Tokenizer {
    public static ParseResult Tokenize(string source, ParseContext context) {
        ParseResult parseResult = new ParseResult(source, context);

        StringBuilder currentText = new StringBuilder();
        bool inString = false;

        for (int i = 0; i < source.Length; i++) {
            char c = source[i];
            if (c == '"') {
                // if we are in a string, we have reached the end of the string
                if (inString) {
                    // add the string as a token
                    parseResult.Add(new StringToken(currentText.ToString()), i + 1);
                    currentText.Clear();
                    inString = false;
                }
                // otherwise, we are not in a string, so we have reached the start of a string
                else {
                    // if we have any text, add it as a token
                    if (currentText.Length > 0) {
                        parseResult.Add(new IdentifierToken(currentText.ToString()), i);
                        currentText.Clear();
                    }

                    inString = true;
                }
            }
            else {
                // if we are in a string, add the character to the current text
                if (inString) {
                    currentText.Append(c);
                }
                // otherwise, we are not in a string, so we have reached the end of a token
                else {
                    if (char.IsWhiteSpace(c)) {
                        // if we have any text, finalize the identifier token
                        if (currentText.Length > 0) {
                            parseResult.Add(new IdentifierToken(currentText.ToString()), i);
                            currentText.Clear();
                        }

                        parseResult.Add(new WhitespaceToken(c), i + 1);
                    }
                    else if (c == '.' && IsDigits(currentText) && i + 1 < source.Length && char.IsDigit(source[i + 1])) {
                        // a dot between digits is a decimal point (e.g. 0.5), not a member access
                        currentText.Append(c);
                    }
                    else {
                        SingleCharToken token = SingleCharToken.Get(c);
                        if (token != null) {
                            // if we have any text, finalize the identifier token
                            if (currentText.Length > 0) {
                                parseResult.Add(new IdentifierToken(currentText.ToString()), i);
                                currentText.Clear();
                            }

                            // add the single character token
                            parseResult.Add(token, i + 1);
                        }
                        else {
                            // not a single char token, so we are in an identifier
                            currentText.Append(c);
                        }
                    }
                }
            }
        }

        if (inString) parseResult.AddExceptionMessage("Unterminated string", parseResult.GetSourceStartIndex(parseResult.Count), source.Length);

        if (currentText.Length > 0) parseResult.Add(new IdentifierToken(currentText.ToString()), source.Length);

        MultiCharToken.ReplaceTokens(parseResult);
        NumberToken.ReplaceTokens(parseResult);

        return parseResult;
    }

    private static bool IsDigits(StringBuilder text) {
        if (text.Length == 0) return false;
        for (int i = 0; i < text.Length; i++)
            if (!char.IsDigit(text[i]))
                return false;
        return true;
    }
}