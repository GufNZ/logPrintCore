using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

namespace logPrintCore.Ansi;

public static partial class ColorExtensions {
	[GeneratedRegex(@"^\$(?:(.)\1){3}$")]
	private static partial Regex IsShort();


	extension(Color colour) {
		public string ToDebugString() {
			if (colour.A == ColourPart.IS_BYTE) {
				return colour.R.ToString("X2");
			}


			var longStr = $"${colour.R:X2}{colour.G:X2}{colour.B:X2}";
			var match = IsShort().Match(longStr);
			return match.Success
				? "$" + string.Join("", match.Groups[1].Captures.Select(c => c.Value))
				: longStr;
		}
	}
}
