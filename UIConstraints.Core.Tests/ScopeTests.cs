using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Tests.TestGame;

namespace UIConstraints.Core.Tests {
// what engines need to provide variables to other expressions: variable names, element types of collections and result types of inputs
public class ScopeTests {
	[TestCase("item", true)]
	[TestCase("_item2", true)]
	[TestCase("2item", false)]
	[TestCase("int", false)] // keyword
	[TestCase("my-item", false)]
	[TestCase("", false)]
	[TestCase(null, false)]
	public void VariableNames(string name, bool valid) {
		Assert.That(IdentifierToken.IsValidIdentifier(name), Is.EqualTo(valid));
	}

	[TestCase(typeof(Dictionary<Item, int>), typeof(KeyValuePair<Item, int>))]
	[TestCase(typeof(List<int>), typeof(int))]
	[TestCase(typeof(Item[]), typeof(Item))]
	[TestCase(typeof(IEnumerable<string>), typeof(string))]
	[TestCase(typeof(string), typeof(char))]
	[TestCase(typeof(ArrayList), typeof(object))] // not generic
	public void ElementTypes(Type collectionType, Type elementType) {
		Assert.That(TypeRules.GetElementType(collectionType), Is.EqualTo(elementType));
	}

	[TestCase("player.level > 1", typeof(bool), null)]
	[TestCase("player.level", typeof(float), null)] // implicitly convertible
	[TestCase("player.inventory", typeof(IEnumerable), null)]
	[TestCase("null", typeof(Item), null)]
	[TestCase("player.level", typeof(bool), "The result has to be bool, but is int")]
	[TestCase("player.level", typeof(IEnumerable), "The result has to be IEnumerable, but is int")]
	[TestCase("2.5", typeof(int), "The result has to be int, but is float")]
	public void ResultTypes(string code, Type resultType, string error) {
		ParseResult parseResult = CodeParser.TryParse(code, new ParseContext(null, EvaluationTests.VARIABLES), ParseTarget.Value, resultType);
		Assert.That(parseResult.Success, Is.EqualTo(error == null));
		if (error != null) Assert.That(parseResult.Exception.GetMessage(0), Is.EqualTo(error));
	}
}
}
