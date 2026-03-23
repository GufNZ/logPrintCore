using System.Drawing;

using logPrintCore.Utils;

namespace logPrintCore.Ansi;

internal sealed class ForegroundColourPart : ColourPart, IRentable {
	private ForegroundColourPart() { }


	public ForegroundColourPart Init(Color colour) {
		Init(isForegroundColour: true, colour);
		return this;
	}


	public static ForegroundColourPart Create() {
		return new();
	}
}
