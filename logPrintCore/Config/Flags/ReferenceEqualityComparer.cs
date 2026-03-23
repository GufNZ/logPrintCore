using System.Collections.Generic;

namespace logPrintCore.Config.Flags;

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> {
	private ReferenceEqualityComparer() { }


	public static ReferenceEqualityComparer<T> Instance { get; } = new();


	public bool Equals(T? x, T? y) {
		return ReferenceEquals(x, y);
	}

	public int GetHashCode(T obj) {
		return obj!.GetHashCode();
	}
}
