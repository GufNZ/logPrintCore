using JetBrains.Annotations;

namespace logPrintCore.Config.Rules;

internal enum ParseType {
	None,
	[UsedImplicitly] Json,
	Parent
}
