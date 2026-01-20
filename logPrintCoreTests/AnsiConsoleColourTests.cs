using System.Diagnostics;
using System.Drawing;
using System.Text;

using logPrintCore.Ansi;

namespace logPrintCoreTests;

public class AnsiConsoleColourTests {
	[Theory]
	[InlineData("~w~#k#", false)]
	[InlineData("~$F08~#$123456#", false)]
	public void TestParse(string input, bool resetAtEnd) {
		var parts = AnsiConsoleColourExtensions.Parse(input, resetAtEnd).ToList();
		var output = ReEncode(parts);
		Assert.Equal(input, output);
	}

	[Theory]
	[InlineData("~w~#k#", false, "%kw%")]
	[InlineData("~w~#k#", true, "%kw%")]
	[InlineData("a~w~b#k#c", true, "a~w~b#k#c")]
	[InlineData("a~<~#<#b~w~c#k#d#>#~>~e", true, "a%<%b~w~c#k#d%>%e")]
	[InlineData("a~w~#k#~Y~#b#b", false, "a%bY%b")]
	[InlineData("a~w~b#k#~Y~#b#c", false, "a~w~b%bY%c")]
	[InlineData("a~w~#k#~Y~#b#b", true, "a%bY%b%!%")]
	[InlineData("a~w~b#k#~Y~#b#c", true, "a~w~b%bY%c%!%")]
	[InlineData("a~$FFE~b#k#~$432155~#b#c", true, "a~$FFE~b%b$432155%c%!%")]
	public void TestNormalise(string input, bool resetAtEnd, string expectedOutput) {
		var parts = AnsiConsoleColourExtensions.Parse(input, resetAtEnd).ToList();

		var newParts = AnsiConsoleColourExtensions.Normalise(parts).ToList();

		var output = ReEncode(newParts);

		Assert.Equal(expectedOutput, output);
	}


	private static string ReEncode(IEnumerable<Part> newParts) {
		string Unmap(Color c) {
			return (c.A == ColourPart.IS_BYTE)
				? ColourPart.CodeToAnsiMap.First(pair => pair.Value == c.R).Key
				: c.ToDebugString();
		}

		return newParts.Aggregate(
			new StringBuilder(),
			(b, part) => {
				b.Append(
					part switch {
						TextPart t => t.text,

						ResetPart r => r.HasBackground
							? r.HasForeground
								? "%!%"
								: "#!#"
							: r.HasForeground
								? "~!~"
								: throw new UnreachableException("Reset has no colours!"),

						PushPart p => p.HasBackground
							? p.HasForeground
								? "%<%"
								: "#<#"
							: p.HasForeground
								? "~<~"
								: throw new UnreachableException("Push has no colours!"),

						PopPart p => p.HasBackground
							? p.HasForeground
								? "%>%"
								: "#>#"
							: p.HasForeground
								? "~>~"
								: throw new UnreachableException("Pop has no colours!"),

						ColourPart c => c.HasBackground
							? c.HasForeground
								? $"%{Unmap(c.currentBackground)}{Unmap(c.currentForeground)}%"
								: $"#{Unmap(c.currentBackground)}#"
							: c.HasForeground
								? $"~{Unmap(c.currentForeground)}~"
								: throw new UnreachableException(c.GetType().Name + " has no colours!"),

						_ => throw new UnreachableException("Fix the tests!")
					}
				);

				return b;
			},
			b => b.ToString()
		);
	}
}
