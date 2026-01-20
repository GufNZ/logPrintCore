using System.ComponentModel.DataAnnotations;

using JetBrains.Annotations;

using logPrintCore.Config.Flags;
using logPrintCore.Config.Rules;

namespace logPrintCore.Config;

[UsedImplicitly]
internal sealed class ConfigRoot {
	[Required]
	public Docs Docs { get; [UsedImplicitly] set; } = null!;

	[Required]
	public FlagSet[] FlagSets { get; [UsedImplicitly] set; } = null!;

	[Required]
	public RuleSet[] RuleSets { get; [UsedImplicitly] set; } = null!;
}
