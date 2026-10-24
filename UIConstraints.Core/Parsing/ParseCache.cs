using System.Collections.Generic;
using System.Threading;

namespace UIConstraints.Core.Parsing {
/**
 * Parse results of all sources, so a given source does not need to be parsed more than once (parse results should not be serialized by engines).
 */
public static class ParseCache {
	private static readonly Dictionary<string, ParseResult> PARSE_RESULTS = new Dictionary<string, ParseResult>(); // <cache key, result>

	private static readonly List<string> CURRENTLY_PARSING = new List<string>(); // cache keys

	private static string GetCacheKey(string input, ParseContext context, ParseTarget target) {
		return target + "\0" + context.key + "\0" + input;
	}

	/// <summary>
	///     Get the parse result for a given input string, or wait for it to be parsed if it's not already parsed.<br />
	///     THIS WILL BLOCK NOT ONLY THE CURRENT THREAD, BUT ALSO ANY OTHER THREAD TRYING TO GET A PARSE RESULT, DO NOT USE
	///     FROM UI!
	/// </summary>
	public static ParseResult GetParseResultAwait(string input, ParseContext context, ParseTarget target = ParseTarget.Value) {
		string cacheKey = GetCacheKey(input, context, target);
		lock (PARSE_RESULTS) {
			if (!PARSE_RESULTS.ContainsKey(cacheKey)) PARSE_RESULTS.Add(cacheKey, CodeParser.TryParse(input, context, target));
			return PARSE_RESULTS[cacheKey];
		}
	}

	// returns null if the input has not been parsed yet, in which case it is parsed in the background
	public static ParseResult GetParseResult(string input, ParseContext context, ParseTarget target = ParseTarget.Value) {
		string cacheKey = GetCacheKey(input, context, target);
		lock (PARSE_RESULTS) {
			if (PARSE_RESULTS.ContainsKey(cacheKey)) return PARSE_RESULTS[cacheKey];
		}

		ParseAsync(input, context, target);
		return null;
	}

	public static void ParseAsync(string input, ParseContext context, ParseTarget target = ParseTarget.Value) {
		string cacheKey = GetCacheKey(input, context, target);
		lock (CURRENTLY_PARSING) {
			if (CURRENTLY_PARSING.Contains(cacheKey)) return;
			CURRENTLY_PARSING.Add(cacheKey);
		}

		Thread thread = new Thread(() => {
			ParseResult result = CodeParser.TryParse(input, context, target);
			lock (PARSE_RESULTS) {
				if (!PARSE_RESULTS.ContainsKey(cacheKey)) PARSE_RESULTS.Add(cacheKey, result); // the contains key check should be redundant, but just in case
			}

			lock (CURRENTLY_PARSING) {
				CURRENTLY_PARSING.Remove(cacheKey);
			}
		});
		thread.Start();
	}
}
}
