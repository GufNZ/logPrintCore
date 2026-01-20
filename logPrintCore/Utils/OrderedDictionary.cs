using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace logPrintCore.Utils;

internal sealed class OrderedDictionary<TKey, TValue> : OrderedDictionary, IDictionary<TKey, TValue>
	where TKey : notnull {
	// ReSharper disable UnusedMember.Global
	public OrderedDictionary() { }
	public OrderedDictionary(int capacity) : base(capacity) { }
	public OrderedDictionary(IEqualityComparer<TKey> comparer) : base(new UngenericComparer(comparer)) {
		Comparer = comparer;
	}
	public OrderedDictionary(int capacity, IEqualityComparer<TKey> comparer) : base(capacity, new UngenericComparer(comparer)) {
		Comparer = comparer;
	}


	public TValue this[TKey key] {
		get => (TValue)base[key]!;
		set => base[key] = value;
	}


	public IEqualityComparer<TKey> Comparer { get; } = EqualityComparer<TKey>.Default;

	public new ICollection<TKey> Keys => base.Keys.Cast<TKey>().ToList();
	public new ICollection<TValue> Values => base.Values.Cast<TValue>().ToList();


	public new IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() {
		// ReSharper disable once GenericEnumeratorNotDisposed - this one doesn't need to be disposed.
		var enumerator = base.GetEnumerator();
		while (enumerator.MoveNext()) {
			yield return new((TKey)enumerator.Key, (TValue)enumerator.Value!);
		}
	}


	public void Add(KeyValuePair<TKey, TValue> item) {
		(TKey key, TValue value) = item;
		base.Add(key, value);
	}

	public bool Contains(KeyValuePair<TKey, TValue> item) {
		(TKey key, TValue testValue) = item;
		return TryGetValue(key, out TValue? value) && Equals(testValue, value);
	}

	public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) {
		throw new NotSupportedException();
	}

	public bool Remove(KeyValuePair<TKey, TValue> item) {
		if (!Contains(item)) {
			return false;
		}


		base.Remove(item.Key);
		return true;
	}

	public bool ContainsKey(TKey key) {
		return Contains(key);
	}

	public bool ContainsValue(TValue value) {
		return Values.Contains(value);
	}

	public void Add(TKey key, TValue value) {
		base.Add(key, value);
	}

	public bool Remove(TKey key) {
		if (!ContainsKey(key)) {
			return false;
		}


		base.Remove(key);
		return true;
	}

	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) {
		if (ContainsKey(key)) {
			value = this[key];
			return true;
		}


		value = default;
		return false;
	}


	private sealed class UngenericComparer(IEqualityComparer<TKey> comparer) : IEqualityComparer {
		bool IEqualityComparer.Equals(object? x, object? y) {
			return comparer.Equals((TKey)x!, (TKey)y!);
		}

		public int GetHashCode(object obj) {
			return obj.GetHashCode();
		}
	}
}
