using System.Collections.Generic;
using System.Linq;

namespace logPrintCore.Utils;

internal static class CollectionExtensions {
	public static int IndexOf<T>(this ICollection<T>? collection, T item, IEqualityComparer<T> comparer, int start = 0, int? count = null) {
		return collection
				?.Skip(start)
				.Take(count ?? collection.Count)
				.Select((entry, index) => (entry, index))
				.FirstOrDefault(pair => comparer.Equals(pair.entry, item))
				.NullIfDefault()
				?.index
			?? -1;
	}
}
