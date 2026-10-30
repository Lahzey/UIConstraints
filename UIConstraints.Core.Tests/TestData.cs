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

// a struct with operators, like Unity's Vector2
public struct Vec {
	public float x;
	public float y;

	public Vec(float x, float y) {
		this.x = x;
		this.y = y;
	}

	public static Vec operator +(Vec a, Vec b) {
		return new Vec(a.x + b.x, a.y + b.y);
	}

	public static Vec operator *(Vec a, float factor) {
		return new Vec(a.x * factor, a.y * factor);
	}

	public static Vec operator -(Vec a) {
		return new Vec(-a.x, -a.y);
	}

	public override string ToString() {
		return $"({x}, {y})";
	}
}

// generic methods whose type arguments are inferred
public static class Generics {
	public static T Default<T>() {
		return default;
	}

	public static List<T> ListOf<T>(params T[] items) {
		return new List<T>(items);
	}

	public static T Pick<T>(T first, T second) {
		return second;
	}

	public static T First<T>(IEnumerable<T> items) {
		foreach (T item in items) return item;
		return default;
	}
}

// state that actions change
public class Counter {
	public static int total;

	public int count;
	public byte small;
	public readonly int fixedValue = 1;
	public string text = "";
	public Vec position;
	public List<int> list = new List<int> { 1, 2, 3 };
	public int[] array = { 1, 2, 3 };
	public int[,] grid = new int[2, 3];
	public Dictionary<string, int> scores = new Dictionary<string, int> { { "a", 1 } };
	public IReadOnlyList<int> readOnlyList = new List<int> { 1 };

	public string Label { get; private set; } = "label";
	public object content = new Item("boxed");

	// like GetComponent<T>(), a generic method that is called with an explicit type argument
	public T Get<T>() where T : class {
		return content as T;
	}

	public void Increment() {
		count++;
	}

	public void Add(int amount) {
		count += amount;
	}
}
}

// a second type named Player, to test ambiguous names and aliases
namespace UIConstraints.Core.Tests.OtherGame {
public class Player {
	public static string Kind = "other";
}
}
