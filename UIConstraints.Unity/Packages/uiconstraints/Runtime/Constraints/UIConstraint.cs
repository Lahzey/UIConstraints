using UIConstraints.Core.Data;
using UnityEngine;

namespace UIConstraints.Unity.Constraints {
public abstract class UIConstraint : MonoBehaviour {
	protected UINode node;

	protected virtual void Awake() {
		node = UINode.AddToNode(this);
	}

	protected virtual void OnDestroy() {
		node.Remove(this);
	}

	public abstract void Render(DataContext context);
}
}