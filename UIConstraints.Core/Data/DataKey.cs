using System;
using System.Collections.Generic;
using System.Reflection;

namespace UIConstraints.Core.Data {
public class DataKeyBase {
	private static readonly object REGISTRY_LOCK = new object(); // the registry may be initialized from parsing threads
	private static Dictionary<string, DataKeyBase> dataKeyRegistry;
	public readonly string key;

	public readonly Type type;

	internal DataKeyBase(Type type, string key) {
		this.type = type;
		this.key = key;
	}

	public static DataKeyBase Get(string key) {
		lock (REGISTRY_LOCK) {
			if (dataKeyRegistry == null) InitRegistry();
		}

		return dataKeyRegistry.ContainsKey(key) ? dataKeyRegistry[key] : null;
	}

	public static DataKeyBase[] GetAll() {
		lock (REGISTRY_LOCK) {
			if (dataKeyRegistry == null) InitRegistry();
		}

		DataKeyBase[] dataKeys = new DataKeyBase[dataKeyRegistry.Count];
		dataKeyRegistry.Values.CopyTo(dataKeys, 0);
		return dataKeys;
	}

	private static void InitRegistry() {
		Dictionary<string, DataKeyBase> registry = new Dictionary<string, DataKeyBase>();

		// use reflection to get all subclasses of DataKey<T> and add all their static fields of type DataKey<T> to the registry
		Type dataKeyType = typeof(DataKeyBase);
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
			Type[] types = assembly.GetTypes();
			foreach (Type type in types) {
				if (type.GetCustomAttributes(typeof(DataKeyRegistryAttribute), true).Length == 0) continue;
				foreach (FieldInfo field in type.GetFields()) {
					if (!field.FieldType.IsSubclassOf(dataKeyType)) continue;

					DataKeyBase dataKey = (DataKeyBase)field.GetValue(null);
					registry.Add(dataKey.key, dataKey);
				}
			}
		}

		dataKeyRegistry = registry; // only assign once complete, so other threads never see a partial registry
	}
}

public class DataKey<T> : DataKeyBase {
	public DataKey(string key) : base(typeof(T), key) { }
}

/// <summary>
///     Use this attribute on a class to mark it as a registry for DataKey subclasses.<br />
///     This will cause all static fields (of type DataKey) in the class to be added to the DataKey registry.<br />
///     Having data keys in the registry will enable the inspector to provide autocomplete options and code validation.
/// </summary>
public class DataKeyRegistryAttribute : Attribute { }
}