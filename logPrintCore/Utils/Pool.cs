using System;
using System.Collections.Generic;

using logPrintCore.Config.Flags;

namespace logPrintCore.Utils;

public class Pool<T>
	where T : IRentable {
	private readonly Stack<T> _free;
	private readonly HashSet<T> _used;
	private readonly int _growthSize;
	private readonly Func<T> _factory;


	public Pool(int initialCapacity, int growthSize, Func<T> factory) {
		_growthSize = growthSize;
		_factory = factory;
		_free = new Stack<T>(initialCapacity);
		_used = new HashSet<T>(ReferenceEqualityComparer<T>.Instance);

		for (int i = 0; i < initialCapacity; i++) {
			_free.Push(_factory());
		}
	}


	// ReSharper disable once UnusedMember.Global
	public bool IsEmpty => _used.Count == 0;


	public T Rent() {
		if (_free.Count == 0) {
			for (int i = 0; i < _growthSize; i++) {
				_free.Push(_factory());
			}
		}

		var item = _free.Pop();
		_used.Add(item);

		return item;
	}

	public void Return(T item) {
		_used.Remove(item);
		_free.Push(item);
	}
}
