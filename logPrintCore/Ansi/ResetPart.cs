using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

using logPrintCore.Utils;

namespace logPrintCore.Ansi;

internal sealed class ResetPart : ColourPart, IRentable<ResetPart> {
	internal const string RESET_ALL = $"{PREFIX}0{SUFFIX}";

	private static readonly Color resetMarker = Color.FromArgb(IS_BYTE, ANSI_RESET_COLOUR, 0, 0);


	private ResetPart() { }


	public ResetPart Init(bool? isForegroundColour = null) {
		Init(isForegroundColour, resetMarker);

		return this;
	}


	public static ResetPart Create() {
		return new();
	}


	public override void ToConsole(TextWriter writer) {
		Console.ResetColor();
	}

	public override string ToAnsi() {
		return isForeground.HasValue
			? base.ToAnsi()
			: RESET_ALL;
	}


	public LinkedListNode<ResetPart>? Node { get; set; }
}
