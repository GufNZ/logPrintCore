using System.Collections.Generic;
using System.Drawing;

using logPrintCore.Utils;

namespace logPrintCore.Ansi;

internal sealed class ForegroundColourPart : ColourPart, IRentable<ForegroundColourPart> {
	private ForegroundColourPart() { }


	public ForegroundColourPart Init(Color colour) {
		Init(isForegroundColour: true, colour);
		return this;
	}


	public static ForegroundColourPart Create() {
		return new();
	}


	public LinkedListNode<ForegroundColourPart>? Node { get; set; }
}
