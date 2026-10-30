using System.Collections.Generic;

namespace Example {
// the messages shown in the log of the example UI, static so any expression can use it without a reference
public static class GameLog {
	private const int MAX_MESSAGES = 30;

	public static readonly List<string> messages = new List<string>();
	public static bool scrolledUp; // set by the scroll view of the log, which shows a hint while newer messages are out of view

	public static void Add(string message) {
		messages.Add(message);
		if (messages.Count > MAX_MESSAGES) messages.RemoveAt(0);
	}
}
}
