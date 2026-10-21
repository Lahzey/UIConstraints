using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using TMPro;
using UnityEngine;

namespace UIConstraints.Unity.Constraints {
[RequireComponent(typeof(TextMeshProUGUI))]
public class TextConstraint : UIConstraint {
	[SerializeField] private CodeInput textConstraint;
	private TextMeshProUGUI text;

	private Expression textExpression;

	protected override void Awake() {
		base.Awake();
		text = GetComponent<TextMeshProUGUI>();

		ParseResult parseResult = textConstraint.Result;
		textExpression = parseResult is { Success: true } ? parseResult.Expression : null;
		if (textExpression == null) Debug.LogError("Failed to parse text expression '" + textConstraint.input + "', defaulting to empty string.", this);
	}

	public override void Render(DataContext context) {
		try {
			text.text = (textExpression?.Evaluate(context) ?? "") + ""; // using plus "" to convert to string because im lazy
		}
		catch (EvaluationException e) {
			Debug.LogException(e, this);
		}
	}
}
}