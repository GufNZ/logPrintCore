using System.ComponentModel.DataAnnotations;

using JetBrains.Annotations;

namespace logPrintCore.Config.Flags.Evaluator;

internal sealed class Property : Method {
	[Required]
	public string Type { get; [UsedImplicitly] set; } = null!;


	public override string ToString() {
		return $"{{{GetType().Name}: {Type} {Name}='{Code}'";
	}
}
