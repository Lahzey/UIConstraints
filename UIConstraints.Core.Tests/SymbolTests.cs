using System;
using NUnit.Framework;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;

namespace UIConstraints.Core.Tests {
// what identifiers resolve to, which editors use for syntax highlighting
public class SymbolTests {
	private const string TEST_GAME = "UIConstraints.Core.Tests.TestGame";

	[TestCase("player.name", "player", VariableKind.DataKey)]
	[TestCase("player.name", "name", VariableKind.Field)]
	[TestCase("player.nope", "nope", VariableKind.Invalid)]
	[TestCase("player.GetItem(1)", "GetItem", VariableKind.Method)]
	[TestCase("player.Nope(1)", "Nope", VariableKind.Invalid)]
	[TestCase("item.name", "item", VariableKind.ContextVariable)]
	[TestCase("item.name", "name", VariableKind.Unknown)]
	[TestCase("anything.x", "x", VariableKind.Unknown)]
	[TestCase("player.inventory.Count", "Count", VariableKind.Property)]
	[TestCase("Math.PI", "Math", VariableKind.Type, "System")]
	[TestCase("Math.PI", "PI", VariableKind.Field, "System")]
	[TestCase("Player.StaticCount", "StaticCount", VariableKind.Property, TEST_GAME)]
	[TestCase("Player.Nested", "Nested", VariableKind.Type, TEST_GAME)]
	public void IdentifierResolvesTo(string code, string name, VariableKind kind, string usedNamespace = null) {
		ParseResult parseResult = CodeParser.TryParse(code, new ParseContext(usedNamespace == null ? Array.Empty<string>() : new[] { usedNamespace }));
		VariableExpression variable = parseResult.ExpressionAtPosition(code.LastIndexOf(name, StringComparison.Ordinal)) as VariableExpression;
		Assert.That(variable, Is.Not.Null);
		Assert.That(variable.Kind, Is.EqualTo(kind));
	}
}
}
