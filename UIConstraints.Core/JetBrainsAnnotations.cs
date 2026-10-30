using System;

// Core does not reference any engine, so it can't use the JetBrains annotations Unity ships with UnityEngine.
// Rider recognizes annotations by their full name, so a copy of the ones used here is enough. Being internal, they never clash with other copies.
namespace JetBrains.Annotations;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Delegate | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.GenericParameter)]
internal sealed class CanBeNullAttribute : Attribute { }