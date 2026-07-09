using System;
using System.Collections.Generic;
using System.Text;

namespace logPrintCore.Utils;

public static class StringBuilderExtensions {
	extension(StringBuilder builder) {
		public string ToStringAndClear() {
			var result = builder.ToString();
			builder.Clear();
			return result;
		}
	}
}
