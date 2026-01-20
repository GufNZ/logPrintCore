using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

using JetBrains.Annotations;

namespace logPrintCore.Config.Flags.Evaluator;

internal sealed class Define {
	[Required]
	public string Name { get; [UsedImplicitly] set; } = null!;

	[Required]
	public string Type { get; [UsedImplicitly] set; } = null!;

	// ReSharper disable once MemberCanBePrivate.Global
	[Required]
	public Dictionary<string, string> Values { get; [UsedImplicitly] set; } = null!;


	public override string ToString() {
		return $"{{{GetType().Name}: {Type} {Name}=[{string.Join(", ", Values)}]";
	}


	public string Value(IEnumerable<string> selectedDefines) {
		return Values[
			selectedDefines
				.Select(selectedDefine => Values.Keys.FirstOrDefault(k => k.StartsWith(selectedDefine, StringComparison.OrdinalIgnoreCase)))
				.FirstOrDefault(key => key != null)
			?? Values.Keys.First()
		];
	}
}
