#if DEBUG
//#define DEBUG_ASSEMBLY
#endif

using System;
using System.Drawing;

using logPrintCore.Utils;

namespace logPrintCore.Ansi;

internal sealed class PopPart : ColourPart, IRentable {
	private static readonly Color popMarker = Color.FromArgb(IS_BYTE, 0xF1, 0, 0);


	private PopPart() { }


#if DEBUG_ASSEMBLY
	private PushPart? _link;


#endif
	public PopPart Init(bool? isForegroundColour) {
		Init(isForegroundColour, popMarker);
		return this;
	}


	public static PopPart Create() {
		return new();
	}


	public void Link(PushPart pushPart) {
		if ((pushPart.isForeground ?? isForeground) != isForeground) {
			throw new InvalidOperationException($"Mismatched Push({pushPart.isForeground}) vs Pop({isForeground})!");
		}


#if DEBUG_ASSEMBLY
		_link = pushPart;
#endif
		if (HasForeground) {
			currentForeground = pushPart.pushedForeground;
		}

		if (HasBackground) {
			currentBackground = pushPart.pushedBackground;
		}
	}


	public override bool MergeWith(Part previous, out Part merged) {
		if (previous is not PopPart pop || pop.isForeground == isForeground || previous is not ColourPart prevColour) {
			merged = null!;
			return false;
		}


		merged = this;

		if (!HasBackground) {
			currentBackground = prevColour.currentBackground;
			isForeground = null;
		}

		// ReSharper disable once InvertIf
		if (!HasForeground) {
			currentForeground = prevColour.currentForeground;
			isForeground = null;
		}

		return true;
	}

#if DEBUG_ASSEMBLY
	protected override string DebugOutput() {
		return base.DebugOutput() + $" %%% {_link}";
	}
#endif
}
