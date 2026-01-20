using System.ComponentModel.DataAnnotations;

using JetBrains.Annotations;

// ReSharper disable MemberCanBeInternal

namespace logPrintCore.Config;

public sealed class Docs {
	[Required]
	public string Usage { get; [UsedImplicitly] set; } = null!;

	[Required]
	public string Definition { get; [UsedImplicitly] set; } = null!;
}
