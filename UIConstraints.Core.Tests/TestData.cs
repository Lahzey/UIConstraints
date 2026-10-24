using System;
using System.Collections.Generic;

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

public interface INamed {
	string Name { get; }
}

public class Animal : INamed {
	public virtual string Name => "animal";
}

public class Dog : Animal {
	public override string Name => "dog";

	public string Bark() {
		return "woof";
	}
}

// converts from int implicitly and to int explicitly, to test user-defined conversions
public struct Money {
	public int cents;

	public Money(int cents) {
		this.cents = cents;
	}

	public static implicit operator Money(int cents) {
		return new Money(cents);
	}

	public static explicit operator int(Money money) {
		return money.cents;
	}
}

// values typed as base classes, interfaces, object, generics and arrays, to test casts and type checks
public class Zoo {
	public Animal pet = new Dog();
	public Animal plainAnimal = new Animal();
	public INamed named = new Dog();
	public object boxed = new List<int> { 1, 2, 3 };
	public Dictionary<string, List<int>> groups = new Dictionary<string, List<int>>();
	public Animal[] animals = { new Animal(), new Dog() };
	public float health = 75.9f;
	public Money money = new Money(250);
}

public static class Overloads {
	public static string Pick(long a, int b) {
		return "long, int";
	}

	public static string Pick(int a, long b) {
		return "int, long";
	}

	// int converts to both, but long is the better conversion target (long converts to float, but not the other way around)
	public static string Widen(long value) {
		return "long";
	}

	public static string Widen(float value) {
		return "float";
	}

	public static string Describe(object value) {
		return "object";
	}

	public static string Describe(string value) {
		return "string";
	}

	public static int Sum(params int[] values) {
		int sum = 0;
		foreach (int value in values) sum += value;
		return sum;
	}

	public static int Cents(Money money) {
		return money.cents;
	}

	public static bool Both(bool a, bool b) {
		return a && b;
	}
}
}

// a second type named Player, to test ambiguous names and aliases
namespace UIConstraints.Core.Tests.OtherGame {
public class Player {
	public static string Kind = "other";
}
}
