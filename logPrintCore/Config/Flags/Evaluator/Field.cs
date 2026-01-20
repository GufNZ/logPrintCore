using System.ComponentModel.DataAnnotations;

using JetBrains.Annotations;

namespace logPrintCore.Config.Flags.Evaluator;

internal sealed class Field {
	[Required]
	public string Name { get; [UsedImplicitly] set; } = null!;

	[Required]
	public string Type { get; [UsedImplicitly] set; } = null!;

	[Required]
	public string Value { get; [UsedImplicitly] set; } = null!;


	public override string ToString() {
		return $"{{{GetType().Name}: {Type} {Name}='{Value}'";
	}
}
