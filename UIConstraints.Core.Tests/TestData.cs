using System;
using System.Collections.Generic;
using UIConstraints.Core.Data;

namespace UIConstraints.Core.Tests.TestGame {
public class Item {
	public string name;

	public Item(string name) {
		this.name = name;
	}

	public override string ToString() {
		return name;
	}
}

public class Player {
	public static Player Instance = new Player { name = "Static" };

	public string name = "Hero";
	public float currentHealth = 75;
	public float maxHealth = 100;
	public int level = 3;
	public Dictionary<Item, int> inventory = new Dictionary<Item, int>();
	public Item weapon = new Item("Sword");

	public static int StaticCount => 42;

	public float GetHealthPercent() {
		return currentHealth / maxHealth;
	}

	public int Add(int a, int b) {
		return a + b;
	}

	public float Add(float a, float b) {
		return a + b + 0.5f; // distinguishable from the int overload
	}

	public string Greet(string who, string greeting = "Hello") {
		return greeting + " " + who;
	}

	public void DoNothing() { }

	public Item GetItem(int i) {
		return new Item("Item" + i);
	}

	public int Throws() {
		throw new InvalidOperationException("boom");
	}

	public class Nested {
		public static string Info = "nested";
	}
}

public static class GameState {
	public static Player player = new Player { name = "FromGameState" };
}

[DataKeyRegistry]
public class TestDataKeys {
	public static DataKey<Player> player = new DataKey<Player>("player");
	public static DataKey<bool> showInventory = new DataKey<bool>("showInventory");
	public static DataKey<object> anything = new DataKey<object>("anything");
}
}
