using System.ComponentModel.DataAnnotations;

using JetBrains.Annotations;

namespace logPrintCore.Config.Flags.Evaluator;

internal class Method {
	[Required]
	public string Name { get; [UsedImplicitly] set; } = null!;

	[Required]
	public string Code { get; [UsedImplicitly] set; } = null!;


	public override string ToString() {
		return $"{{{GetType().Name}: void {Name}() {{{Code}}}'";
	}
}
