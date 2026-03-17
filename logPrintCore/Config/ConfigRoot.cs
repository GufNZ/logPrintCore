using System.Collections.Generic;
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

	// ReSharper disable once InconsistentNaming
	// ReSharper disable once UnusedMember.Global - used to hold shared chunks of YAML.
	public Shared __shared { get; [UsedImplicitly] set; } = null!;
}


[UsedImplicitly]
internal class Shared {
	// ReSharper disable UnusedMember.Global
	public IDictionary<string, string?> DateVars { get; set; } = null!;
	public IDictionary<string, string?> LevelVars { get; set; } = null!;
	public IDictionary<string, string?> ExceptionVars { get; set; } = null!;
	public IDictionary<string, string?> StackTraceVars { get; set; } = null!;

	// ReSharper disable once InconsistentNaming
	public IDictionary<string, string?> JSONVars { get; set; } = null!;

	public IDictionary<string, string?> CommonVars { get; set; } = null!;
	public IDictionary<string, string?> ExceptionWithStackTraceVars { get; set; } = null!;
	public IDictionary<string, string?> AllVars { get; set; } = null!;

	public Rule[] DateRules { get; set; } = null!;
	public Rule[] ExceptionStackTraceRules { get; set; } = null!;
	// ReSharper restore UnusedMember.Global
}
