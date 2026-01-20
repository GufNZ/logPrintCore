using JetBrains.Annotations;

namespace logPrintCore.Ansi;

internal enum ClearMode {
	ToEnd = 0,
	[UsedImplicitly] ToStart = 1,
	[UsedImplicitly] All = 2
}
