using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace logPrintCore.Utils;

internal sealed class SafeDictionary<TKey, TValue> : IDictionary<TKey, TValue?>
	where TKey : notnull {
	private readonly MissingKeyOperation _missingKeyOperation;
	private readonly Func<TKey, SafeDictionary<TKey, TValue>, TValue>? _missingValueFunc;
	private readonly Dictionary<TKey, TValue?> _dict = new();


	// ReSharper disable once MemberCanBePrivate.Global
	public SafeDictionary(MissingKeyOperation missingKeyOperation) {
		_missingKeyOperation = missingKeyOperation;

		if (missingKeyOperation == MissingKeyOperation.ReturnKey && typeof(TKey) != typeof(TValue)) {
			throw new ArgumentOutOfRangeException(nameof(missingKeyOperation), "Cannot use MissingKeyOperation.ReturnKey unless TKey == TValue!");
		}
	}
	public SafeDictionary(Func<TKey, SafeDictionary<TKey, TValue>, TValue> missingValueFunc) : this(MissingKeyOperation.EvaluateFunc) {
		_missingValueFunc = missingValueFunc;
	}


	public int Count => _dict.Count;
	public bool IsReadOnly => false;

	public ICollection<TKey> Keys => _dict.Keys;
	public ICollection<TValue?> Values => _dict.Values;


	public IEnumerator<KeyValuePair<TKey, TValue?>> GetEnumerator() {
		return _dict.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator() {
		return GetEnumerator();
	}

	public void Add(KeyValuePair<TKey, TValue?> item) {
		(TKey key, TValue? value) = item;
		_dict.Add(key, value);
	}

	public void Clear() {
		_dict.Clear();
	}

	public bool Contains(KeyValuePair<TKey, TValue?> item) {
		(TKey key, TValue? value) = item;
		return (_dict.ContainsKey(key) && Equals(_dict[key], value));
	}

	public void CopyTo(KeyValuePair<TKey, TValue?>[] array, int arrayIndex) {
		throw new NotSupportedException();
	}

	public bool Remove(KeyValuePair<TKey, TValue?> item) {
		return _dict.Remove(item.Key);
	}

	public bool ContainsKey(TKey key) {
		return _dict.ContainsKey(key);
	}

	public void Add(TKey key, TValue? value) {
		_dict.Add(key, value);
	}

	public void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> values) {
		foreach ((TKey key, TValue value) in values) {
			_dict.Add(key, value);
		}
	}

	public bool Remove(TKey key) {
		return _dict.Remove(key);
	}

	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) {
		return _dict.TryGetValue(key, out value);
	}

	public TValue? this[TKey key] {
		get {
			return ContainsKey(key)
				? _dict[key]
				: _missingKeyOperation switch {
					MissingKeyOperation.ReturnDefault => default,
					MissingKeyOperation.ReturnKey => (TValue)Convert.ChangeType(key, typeof(TValue)),
					MissingKeyOperation.EvaluateFunc => _missingValueFunc!(key, this),
#pragma warning disable CA2208	// Instantiate argument exceptions correctly
					_ => throw new ArgumentOutOfRangeException(nameof(_missingKeyOperation), _missingKeyOperation, $"Unhandled MissingKeyOperation value: '{_missingKeyOperation}'"),
#pragma warning restore CA2208
				};
		}
		set => _dict[key] = value;
	}


	public enum MissingKeyOperation {
		ReturnDefault,
		ReturnKey,
		EvaluateFunc
	}
}
