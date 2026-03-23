using System.Drawing;

using logPrintCore.Utils;

namespace logPrintCore.Ansi;

internal sealed class BackgroundColourPart : ColourPart, IRentable {
	private BackgroundColourPart() { }


	public BackgroundColourPart Init(Color colour) {
		Init(isForegroundColour: false, colour);
		return this;
	}


	public static BackgroundColourPart Create() {
		return new();
	}
}
