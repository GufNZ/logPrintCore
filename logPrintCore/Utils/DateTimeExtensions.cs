using System;

namespace logPrintCore.Utils;

internal static class DateTimeExtensions {
	public static DateTime TruncateTo(this DateTime dateTime, TimeSpan timeSpan) {
		return new(dateTime.Ticks - dateTime.Ticks % timeSpan.Ticks, dateTime.Kind);
	}
}
