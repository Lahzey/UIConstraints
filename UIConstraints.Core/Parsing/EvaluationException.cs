using System;
using UIConstraints.Core.Parsing.Expressions;

namespace UIConstraints.Core.Parsing {
public class EvaluationException : Exception {
	public EvaluationException(string message, Expression expressionToEvaluate, Exception innerException = null) : base(message, innerException) {
		ExpressionToEvaluate = expressionToEvaluate;
	}

	public Expression ExpressionToEvaluate { get; }
}
}
