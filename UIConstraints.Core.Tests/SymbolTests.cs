using System;
using System.Collections.Generic;
using NUnit.Framework;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using UIConstraints.Core.Tests.TestGame;

namespace UIConstraints.Core.Tests {
// what identifiers resolve to, which editors use for syntax highlighting
public class SymbolTests {
	private const string TEST_GAME = "UIConstraints.Core.Tests.TestGame";

	[TestCase("GameState.player.name", "GameState", VariableKind.Type, TEST_GAME)]
	[TestCase("GameState.player.name", "player", VariableKind.Field, TEST_GAME)]
	[TestCase("GameState.player.name", "name", VariableKind.Field, TEST_GAME)]
	[TestCase("GameState.player.nope", "nope", VariableKind.Invalid, TEST_GAME)]
	[TestCase("GameState.player.GetItem(1)", "GetItem", VariableKind.Method, TEST_GAME)]
	[TestCase("GameState.player.Nope(1)", "Nope", VariableKind.Invalid, TEST_GAME)]
	[TestCase("GameState.player.inventory.Count", "Count", VariableKind.Property, TEST_GAME)]
	[TestCase("item.name", "item", VariableKind.ContextVariable)]
	[TestCase("item.name", "name", VariableKind.Field)] // context variables have a declared type
	[TestCase("GameState.player.name", "GameState", VariableKind.Unresolved)] // without the using
	[TestCase("item.name", "item", VariableKind.Unresolved, null, false)] // not a declared variable
	[TestCase("Math.PI", "Math", VariableKind.Type, "System")]
	[TestCase("Math.PI", "PI", VariableKind.Field, "System")]
	[TestCase("System.Math.PI", "System", VariableKind.Namespace)]
	[TestCase("System.Collections.Generic.List<int>", "Generic", VariableKind.Namespace)]
	[TestCase("System.Collections.Generic.List<int>", "List", VariableKind.Type)]
	[TestCase("Player.StaticCount", "StaticCount", VariableKind.Property, TEST_GAME)]
	[TestCase("Player.Nested", "Nested", VariableKind.Type, TEST_GAME)]
	public void IdentifierResolvesTo(string code, string name, VariableKind kind, string usedNamespace = null, bool declareItem = true) {
		Dictionary<string, Type> variables = declareItem ? new Dictionary<string, Type> { { "item", typeof(Item) } } : null;
		ParseResult parseResult = CodeParser.TryParse(code, new ParseContext(usedNamespace == null ? Array.Empty<string>() : new[] { usedNamespace }, variables), ParseTarget.Any);
		VariableExpression variable = parseResult.ExpressionAtPosition(code.LastIndexOf(name, StringComparison.Ordinal)) as VariableExpression;
		Assert.That(variable, Is.Not.Null);
		Assert.That(variable.Kind, Is.EqualTo(kind));
	}

	[TestCase("GameState.player", "player", true)]
	[TestCase("Player.Instance", "Instance", true)]
	[TestCase("GameState.player.name", "name", false)]
	public void StaticMembers(string code, string name, bool isStatic) {
		ParseResult parseResult = CodeParser.TryParse(code, new ParseContext(new[] { TEST_GAME }));
		VariableExpression variable = (VariableExpression)parseResult.ExpressionAtPosition(code.LastIndexOf(name, StringComparison.Ordinal));
		Assert.That(variable.IsStatic, Is.EqualTo(isStatic));
	}
}
}
