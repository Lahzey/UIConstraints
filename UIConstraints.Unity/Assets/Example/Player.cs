using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Example {
/**
 * The game state the example UI shows. Note that it has no references to the UI: the UI reads it through expressions (e.g. player.HealthPercent),
 * and changes it through actions (e.g. player.Heal(20) when a button is clicked).
 */
public class Player : MonoBehaviour {
	public static Player Instance { get; private set; }

	[FormerlySerializedAs("name")] public string playerName = "Player";
	public float maxHealth = 100;
	public float currentHealth = 100;
	public float healthRegen = 1;
	public int gold = 50;

	[SerializeField] private Item appleItem;
	[SerializeField] private Item orangeItem;
	[SerializeField] private Item bananaItem;

	public Dictionary<Item, int> inventory = new Dictionary<Item, int>();
	public bool showInventory = true;

	public float HealthPercent => currentHealth / maxHealth;
	public bool IsAlive => currentHealth > 0;

	private void Awake() {
		Instance = this;
	}

	private void Start() {
		GameLog.Add($"Welcome, {playerName}! Buy something in the shop, or take some damage.");
	}

	private void Update() {
		if (IsAlive) currentHealth = Mathf.Min(currentHealth + healthRegen * Time.deltaTime, maxHealth);

		// keyboard shortcuts, the UI follows whatever changes the state
		bool shiftDown = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		if (Input.GetKeyDown(KeyCode.I)) showInventory = !showInventory;
		if (Input.GetKeyDown(KeyCode.A)) PickupOrDrop(appleItem, shiftDown);
		if (Input.GetKeyDown(KeyCode.O)) PickupOrDrop(orangeItem, shiftDown);
		if (Input.GetKeyDown(KeyCode.B)) PickupOrDrop(bananaItem, shiftDown);
	}

	private void PickupOrDrop(Item item, bool drop) {
		if (drop) DropItem(item);
		else PickupItem(item);
	}

	public void Damage(float amount) {
		if (!IsAlive) return;
		currentHealth = Mathf.Max(currentHealth - amount, 0);
		GameLog.Add(IsAlive ? $"{playerName} took {amount} damage" : $"{playerName} died");
	}

	public void Heal(float amount) {
		currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
		GameLog.Add($"{playerName} was healed by {amount}");
	}

	public void PickupItem(Item item, int amount = 1) {
		inventory[item] = CountOf(item) + amount;
	}

	// returns false if there was no such item
	public bool DropItem(Item item) {
		int count = CountOf(item);
		if (count == 0) return false;

		if (count == 1) inventory.Remove(item);
		else inventory[item] = count - 1;
		return true;
	}

	public int CountOf(Item item) {
		return inventory.TryGetValue(item, out int count) ? count : 0;
	}
}
}
