using System.Collections.Generic;
using System.Linq;

namespace logPrintCore.Utils;

internal static class DictionaryExtensions {
	// ReSharper disable UnusedMember.Global
	extension<TKey, TValue>(Dictionary<TKey, TValue> dictionary)
		where TKey : notnull {
		public int IndexOfKey(TKey key) {
			return dictionary.Keys
					.Select(
						(k, i) => dictionary.Comparer.Equals(k, key)
							? i
							: (int?)-1
					)
					.FirstOrDefault(i => i != -1)
				?? -1;
		}

		public int IndexOfValue(TValue value) {
			return dictionary.Values
					.Select(
						(v, i) => Equals(v, value)
							? i
							: (int?)-1
					)
					.FirstOrDefault(i => i != -1)
				?? -1;
		}
	}


	extension<TKey, TValue>(OrderedDictionary<TKey, TValue> dictionary)
		where TKey : notnull {
		public int IndexOfKey(TKey key) {
			return dictionary.Keys
					.Select(
						(k, i) => dictionary.Comparer.Equals(k, key)
							? i
							: (int?)-1
					)
					.FirstOrDefault(i => i != -1)
				?? -1;
		}

		public int IndexOfValue(TValue value) {
			return dictionary.Values
					.Select(
						(v, i) => Equals(v, value)
							? i
							: (int?)-1
					)
					.FirstOrDefault(i => i != -1)
				?? -1;
		}
	}
	// ReSharper restore UnusedMember.Global
}
