using System.Collections.Generic;

namespace logPrintCore;

internal static class Precedence {
	public static readonly List<LogLevel> LogLevels = [
		LogLevel.None,
		LogLevel.Trace,
		LogLevel.Debug,
		LogLevel.Info,
		LogLevel.Warn,
		LogLevel.Error,
		LogLevel.Fatal
	];
}
