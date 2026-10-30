using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing;

public class ParseException : Exception {
    private readonly List<int> endPositions = new List<int>();
    private readonly List<string> messages = new List<string>();
    private readonly List<int> startPositions = new List<int>();
    private readonly List<string> unresolvedIdentifiers = new List<string>();

    // unresolvedIdentifier is set for errors caused by a name that could not be resolved, so editors can suggest usings that would resolve it
    public void AddMessage(string message, int startPosition, int endPosition, [CanBeNull] string unresolvedIdentifier = null) {
        messages.Add(message);
        startPositions.Add(startPosition);
        endPositions.Add(endPosition);
        unresolvedIdentifiers.Add(unresolvedIdentifier);
    }

    public int MessageCount() {
        return messages.Count;
    }

    public string GetMessage(int index) {
        return messages[index];
    }

    public int GetStartPosition(int index) {
        return startPositions[index];
    }

    public int GetEndPosition(int index) {
        return endPositions[index];
    }

    [CanBeNull]
    public string GetUnresolvedIdentifier(int index) {
        return unresolvedIdentifiers[index];
    }
}