using System.Collections.Generic;
using UnityEngine;

namespace Example {
// sells items to the player and buys them back for half the price
public class Shop : MonoBehaviour {
	public static Shop Instance { get; private set; }

	public List<Item> items = new List<Item>();

	private void Awake() {
		Instance = this;
	}

	public bool CanBuy(Item item, int amount) {
		return Player.Instance.IsAlive && Player.Instance.gold >= item.price * amount;
	}

	public void Buy(Item item, int amount) {
		if (!CanBuy(item, amount)) return;

		Player.Instance.gold -= item.price * amount;
		Player.Instance.PickupItem(item, amount);
		GameLog.Add($"Bought {amount} {item} for {item.price * amount} gold");
	}

	public int SellPrice(Item item) {
		return item.price / 2;
	}

	public void Sell(Item item) {
		if (!Player.Instance.DropItem(item)) return;

		Player.Instance.gold += SellPrice(item);
		GameLog.Add($"Sold 1 {item} for {SellPrice(item)} gold");
	}
}
}
