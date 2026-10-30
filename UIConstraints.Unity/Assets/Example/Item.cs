using UnityEngine;
using UnityEngine.Serialization;

namespace Example {
[CreateAssetMenu(fileName = "Item", menuName = "ScriptableObjects/Item", order = 1)]
public class Item : ScriptableObject {
	[FormerlySerializedAs("name")] public string displayName;
	public Sprite sprite;
	public int price = 10;
	[TextArea] public string description;

	// used by the messages of the log, e.g. "Bought 1 Apple"
	public override string ToString() {
		return displayName;
	}
}
}
