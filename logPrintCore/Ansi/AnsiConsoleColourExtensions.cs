#if DEBUG
//#define DEBUG_ASSEMBLY
#endif

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using logPrintCore.Utils;

namespace logPrintCore.Ansi;

internal static partial class AnsiConsoleColourExtensions {
	private static bool debugAssembly
#if DEBUG_ASSEMBLY
			= true
#endif
		;

	public const char FOREGROUND = '~';
	public const char BACKGROUND = '#';

	// ReSharper disable MemberCanBePrivate.Global
	public const char RESET = '!';
	public const char PUSH = '<';
	public const char POP = '>';
	public const char HEX = '$';

	public const string PUSH_FG = "~<~";	//NOTE: char.ToString() isn't considered constant by the compiler...
	public const string PUSH_BG = "#<#";

	public const string POP_FG = "~>~";
	public const string POP_BG = "#>#";
	// ReSharper restore MemberCanBePrivate.Global

	public const string PUSH_COLOURS = PUSH_FG + PUSH_BG;
	public const string POP_COLOURS = POP_BG + POP_FG;


	private const string MATCH_FOREGROUND_SINGLE = "(?>~[^~]+~)";
	private const string MATCH_BACKGROUND_SINGLE = "(?>#[^#]+#)";

	public const string MATCH_FOREGROUND = $"(?>{MATCH_FOREGROUND_SINGLE}+)";
	public const string MATCH_BACKGROUND = $"(?>{MATCH_BACKGROUND_SINGLE}+)";
	public const string MATCH_ANY = $"(?>(?:{MATCH_FOREGROUND_SINGLE}|{MATCH_BACKGROUND_SINGLE})+)";

#pragma warning disable IDE0044 // Readonly modifier can be added -- needs to be mutable.
	// ReSharper disable once MemberCanBePrivate.Global - public API.
	public static int OutputWidth = 128;
#pragma warning restore IDE0044

	private static readonly char[] newlineChars = Environment.NewLine.ToCharArray();

	private static readonly Pool<TextPart> textPartPool;
	private static readonly Pool<ResetPart> resetPartPool;
	private static readonly Pool<ForegroundColourPart> foregroundColourPartPool;
	private static readonly Pool<BackgroundColourPart> backgroundColourPartPool;
	private static readonly Pool<PushPart> pushPartPool;
	private static readonly Pool<PopPart> popPartPool;

	private static readonly StringBuilder parseBuilder = new();
	private static readonly StringBuilder colouriseBuilder = new();


	static AnsiConsoleColourExtensions() {
		try {
			OutputWidth = Console.WindowWidth;
		} catch (Exception) {
			// Ignore.
		}


		textPartPool = new(128, 64, TextPart.Create);
		resetPartPool = new(2, 2, ResetPart.Create);
		foregroundColourPartPool = new(128, 64, ForegroundColourPart.Create);
		backgroundColourPartPool = new(128, 64, BackgroundColourPart.Create);
		pushPartPool = new(32, 32, PushPart.Create);
		popPartPool = new(32, 32, PopPart.Create);
	}

	private static void Return(Part part) {
		switch (part) {
			case TextPart textPart:
				textPartPool.Return(textPart);
				break;

			case ResetPart resetPart:
				resetPartPool.Return(resetPart);
				break;

			case ForegroundColourPart foregroundColourPart:
				foregroundColourPartPool.Return(foregroundColourPart);
				break;

			case BackgroundColourPart backgroundColourPart:
				backgroundColourPartPool.Return(backgroundColourPart);
				break;

			case PushPart pushPart:
				pushPartPool.Return(pushPart);
				break;

			case PopPart popPart:
				popPartPool.Return(popPart);
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(part), part, $"Unhandled Part subtype `{part.GetType().FullName}`!");
		}
	}


	public static ConsoleColourOutputMode OutputMode = ConsoleColourOutputMode.Ansi;


	public static string? EscapeColourCodeChars(this string? line) {
		if (line == null) {
			return null;
		}


		var tildeCount = line.Occurrences(FOREGROUND);
		var hashCount = line.Occurrences(BACKGROUND);

		if (tildeCount == 0 && hashCount == 0) {
			return line;	// Fast path: no escaping needed.
		}


		return string.Create(
			line.Length + tildeCount + hashCount,
			line,
			(span, str) => {
				int destIndex = 0;
				foreach (var c in str) {
					span[destIndex++] = c;
					if (c == FOREGROUND || c == BACKGROUND) {
						span[destIndex++] = c;	// Duplicate.
					}
				}
			}
		);
	}


	extension(TextWriter writer) {
		// ReSharper disable once UnusedMember.Global
		public void Clear(ClearMode clearMode = ClearMode.ToEnd) {
			switch (OutputMode) {
				case ConsoleColourOutputMode.ConsoleColor:
					Console.ResetColor();
					Console.Clear();
					break;

				case ConsoleColourOutputMode.Ansi:
					writer.Write($"{ResetPart.RESET_ALL}{ColourPart.PREFIX}{(int)clearMode}J");
					break;

				default:
					throw new ArgumentOutOfRangeException(nameof(clearMode), clearMode, $"Unhandled ClearMode: '{clearMode}'");
			}
		}

		public void ClearLine(ClearMode clearMode = ClearMode.ToEnd) {
			switch (OutputMode) {
				case ConsoleColourOutputMode.ConsoleColor:
					Console.ResetColor();
					Console.Out.Write($"\r{new string(' ', OutputWidth - 1)}\r");
					break;

				case ConsoleColourOutputMode.Ansi:
					writer.Write($"{ResetPart.RESET_ALL}{ColourPart.PREFIX}{(int)clearMode}K");
					break;

				default:
					throw new ArgumentOutOfRangeException(nameof(clearMode), clearMode, $"Unhandled ClearMode: '{clearMode}'");
			}
		}

		// ReSharper disable UnusedMember.Global
		// ReSharper disable once MemberCanBePrivate.Global
		public void WriteColours(string? text, bool? resetAtEnd = null) {
			if (OutputMode == ConsoleColourOutputMode.Ansi) {
				writer.Write(text.Colourise(resetAtEnd));
				return;
			}


			var parts = Normalise(Parse(text, resetAtEnd)).ToList();
			if (debugAssembly) {
				var debug = debugAssembly;
				using var _ = Deferred.Defer(() => debugAssembly = debug);
				debugAssembly = false;

				parts.DumpList(multiLine: true);
			}

			foreach (var part in parts) {
				part.ToConsole(writer);
				Return(part);
			}
		}
		public void WriteColours(bool? resetAtEnd = null, string? format = null, params ReadOnlySpan<object> args) {
			ArgumentNullException.ThrowIfNull(format);


			writer.WriteColours(string.Format(format, args), resetAtEnd);
		}
		public void WriteColours(FormattableString format, bool? resetAtEnd = null) {
			writer.WriteColours(format.ToString(), resetAtEnd);
		}

		// ReSharper disable once MemberCanBePrivate.Global
		public void WriteLineColours(string? text, bool? resetAtEnd = null) {
			writer.WriteColours(text, resetAtEnd);
			writer.WriteLine();
		}
		public void WriteLineColours(bool? resetAtEnd = null, string? format = null, params ReadOnlySpan<object> args) {
			ArgumentNullException.ThrowIfNull(format);


			writer.WriteLineColours(string.Format(format, args), resetAtEnd);
		}
		public void WriteLineColours(FormattableString format, bool? resetAtEnd = null) {
			writer.WriteColours(format.ToString(), resetAtEnd);
			writer.WriteLine();
		}
		// ReSharper restore UnusedMember.Global
	}


	[GeneratedRegex(
		@"(?x)
				~
				(?:
						(?<unescape>~)
					|
						[^~]+
						~
				)
			|
				\#
				(?:
						(?<unescape>\#)
					|
						[^\#]+
						\#
				)
		"
	)]
	private static partial Regex FindColourCodesRE();


	extension(string? str) {
		public string StripColourCodes() {
			return FindColourCodesRE().Replace(str ?? "", "${unescape}");
		}

		// ReSharper disable once MemberCanBePrivate.Global
		public string Colourise(bool? resetAtEnd = null) {
			if (OutputMode == ConsoleColourOutputMode.None) {
				return str.StripColourCodes();
			}


			IEnumerable<Part> parts;
			if (debugAssembly) {
				var inputParts = Parse(str, resetAtEnd).ToList().DumpList(multiLine: true);
#pragma warning disable CS8604// Possible null reference argument. -- not true: the generator at minimum yield-breaks!
#pragma warning disable CS8600// Converting null literal or possible null value to non-nullable type. -- also not true!
				parts = Normalise(inputParts).ToList().DumpList(multiLine: true);
#pragma warning restore CS8600// Converting null literal or possible null value to non-nullable type.
#pragma warning restore CS8604// Possible null reference argument.
			} else {
				parts = Normalise(Parse(str, resetAtEnd));
			}

			var debug = debugAssembly;
			using var _ = Deferred.Defer(() => debugAssembly = debug);
			debugAssembly = false;

			return parts!.Aggregate(
				colouriseBuilder.Clear(),
				debugAssembly
					? (sb, part) => {
						Return(part);
						return sb.Append(part.ToAnsi().Dump());
					}
					: (sb, part) => {
						Return(part);
						return sb.Append(part.ToAnsi());
					},
				sb => sb.ToString()
			);
		}
	}


	public static IEnumerable<Part> Parse(string? text, bool? resetAtEnd) {
		if (string.IsNullOrEmpty(text)) {
			yield break;
		}


		if (debugAssembly) {
			Console.Error.WriteLine("----[Parse]-" + new string('-', OutputWidth - 13));
			Console.Error.WriteLine(text);
			Console.Error.WriteLine(new string('-', OutputWidth - 1));
		}

		var sawColourCodes = false;

		parseBuilder.Clear();

		static Func<string> LazyCalculateLocation(int index, string str) {
			return () => {
				var lineNum = str.Occurrences('\n');
				var truncated = str[..index];
				var position = truncated.Length - truncated.LastIndexOf('\n') - 1;
				truncated = str[(truncated.Length - position)..].TrimStart(newlineChars);
				var endOfLine = truncated.IndexOf("\n", StringComparison.Ordinal);
				if (endOfLine < 0) {
					endOfLine = Math.Min(truncated.Length, position + 10);
				}

				var line = truncated[..endOfLine].Trim(newlineChars).Replace('\t', ' ');
				return $"{
					lineNum + 1
				}:{
					position + 1
				}{
					Environment.NewLine
				}{
					line
				}{
					(endOfLine == truncated.Length
						? ""
						: "...")
				}{
					Environment.NewLine
				}{
					new('-', position)
				}^{
					Environment.NewLine
				}";
			};
		}

		for (var i = 0; i < text.Length;) {
			int j;

			var c = text[i++];
			switch (c) {
				case FOREGROUND:
					j = text.IndexOf(FOREGROUND, i);
					if (j == -1 || j == i) {
						parseBuilder.Append(c);
						i++;
					} else {
						sawColourCodes = true;
						if (parseBuilder.Length > 0) {
							yield return textPartPool.Rent().Init(parseBuilder.ToString());


							parseBuilder.Clear();
						}


						var code = text[i..j];
						if (j == i + 1) {
							yield return text[i] switch {
								RESET => resetPartPool.Rent().Init(isForegroundColour: true),
								PUSH => pushPartPool.Rent().Init(isForegroundColour: true),
								POP => popPartPool.Rent().Init(isForegroundColour: true),
								_ => foregroundColourPartPool.Rent().Init(CodeToAnsi(code, LazyCalculateLocation(i, text)))
							};
						} else if (code.Equals("RESET", StringComparison.OrdinalIgnoreCase)) {
							yield return resetPartPool.Rent().Init(isForegroundColour: true);
						} else {
							yield return foregroundColourPartPool.Rent().Init(CodeToAnsi(code, LazyCalculateLocation(i, text)));
						}


						i = j + 1;
					}


					break;

				case BACKGROUND:
					j = text.IndexOf(BACKGROUND, i);
					if (j == -1 || j == i) {
						parseBuilder.Append(c);
						i++;
					} else {
						sawColourCodes = true;
						if (parseBuilder.Length > 0) {
							yield return textPartPool.Rent().Init(parseBuilder.ToString());


							parseBuilder.Clear();
						}


						var code = text[i..j];
						if (j == i + 1) {
							yield return text[i] switch {
								RESET => resetPartPool.Rent().Init(isForegroundColour: false),
								PUSH => pushPartPool.Rent().Init(isForegroundColour: false),
								POP => popPartPool.Rent().Init(isForegroundColour: false),
								_ => backgroundColourPartPool.Rent().Init(CodeToAnsi(code, LazyCalculateLocation(i, text)))
							};
						} else if (code.Equals("RESET", StringComparison.OrdinalIgnoreCase)) {
							yield return resetPartPool.Rent().Init(isForegroundColour: false);
						} else {
							yield return backgroundColourPartPool.Rent().Init(CodeToAnsi(code, LazyCalculateLocation(i, text)));
						}


						i = j + 1;
					}


					break;

				default:
					parseBuilder.Append(c);
					break;
			}
		}


		if (parseBuilder.Length > 0) {
			yield return textPartPool.Rent().Init(parseBuilder.ToString());
		}


		if (resetAtEnd ?? sawColourCodes) {
			yield return resetPartPool.Rent().Init();
		}
	}

	private static Color ParseHex(ReadOnlySpan<char> code) {
		return code.Length switch {
			3 => Color.FromArgb(
				Convert.ToByte(new string(code[0], 1), 16) * 0x11,
				Convert.ToByte(new string(code[1], 1), 16) * 0x11,
				Convert.ToByte(new string(code[2], 1), 16) * 0x11
			),
			6 => Color.FromArgb(
				Convert.ToByte(code[..2].ToString(), 16),
				Convert.ToByte(code[2..4].ToString(), 16),
				Convert.ToByte(code[4..6].ToString(), 16)
			),
			_ => throw new FormatException($"Invalid hex colour code length: '{code}'!")
		};
	}

	private static Color CodeToAnsi(ReadOnlySpan<char> code, Func<string> calculateLocation) {
		try {
			return code[0] == HEX
				? ParseHex(code[1..])
				: Color.FromArgb(ColourPart.IS_BYTE, ColourPart.CodeToAnsiMap[code.ToString()], 0, 0);
		} catch (KeyNotFoundException exception) {
			throw new KeyNotFoundException($"Unknown colour code '{code}' at {calculateLocation()}", exception);
		}
	}

	public static IEnumerable<Part> Normalise(IEnumerable<Part> inputParts) {
		var parts = inputParts.ToList();
		if (debugAssembly) {
			var debug = debugAssembly;
			using var _ = Deferred.Defer(() => debugAssembly = debug);
			debugAssembly = false;

			Console.Error.WriteLine("----[Normalise]" + new string('-', OutputWidth - 16));
			parts.DumpList(multiLine: true);
			Console.Error.WriteLine(new string('-', OutputWidth - 1));
		}

		if (parts.Count == 0) {
			yield break;
		}


		ColourPart lastColourPart = resetPartPool.Rent().Init();
		if (debugAssembly) {
			Console.Error.WriteLine($"Starting with {lastColourPart}");
		}

		var pushStack = new Stack<PushPart>();
		int i;
		for (i = 0; i < parts.Count; i++) {
			Part part = parts[i];
			if (part is PushPart pushPart) {
				if (debugAssembly) {
					Console.Error.WriteLine($":<: Got: {pushPart}");
				}

				if (pushPart.HasForeground) {
					pushPart.pushedForeground = lastColourPart.currentForeground;
				}

				if (pushPart.HasBackground) {
					pushPart.pushedBackground = lastColourPart.currentBackground;
				}

				pushStack.Push(pushPart);
				if (debugAssembly) {
					var debug = debugAssembly;
					using var _ = Deferred.Defer(() => debugAssembly = debug);
					debugAssembly = false;

					Console.Error.WriteLine($"\tpushing {pushPart} => {pushStack.Count}");
					pushStack.ToList().DumpList(multiLine: true);
				}
			} else {
				var popPart = part as PopPart;
				if (debugAssembly) {
					var debug = debugAssembly;
					using var _ = Deferred.Defer(() => debugAssembly = debug);
					debugAssembly = false;

					if (popPart != null) {
						Console.Error.WriteLine($":>: Got: {popPart}; popping <- {pushStack.Count}");
						var push = pushStack.Pop();
						popPart.Link(push);
						Console.Error.WriteLine($"\t={popPart}");
						pushStack.ToList().DumpList(multiLine: true);
					}
				} else if (popPart != null) {
					var push = pushStack.Pop();
					popPart.Link(push);
				}

				// Do this for pop as well:
				if (part is not ColourPart colourPart) {
					continue;
				}


				if (part is ResetPart && i == parts.Count - 1) {
					break;
				}


				if (colourPart.HasForeground) {
					lastColourPart.currentForeground = colourPart.currentForeground;
				}

				if (colourPart.HasBackground) {
					lastColourPart.currentBackground = colourPart.currentBackground;
				}

				if (debugAssembly) {
					Console.Error.WriteLine($"Updating with {colourPart} ==> {lastColourPart}");
				}
			}
		}

		if (debugAssembly) {
			var debug = debugAssembly;
			using var _ = Deferred.Defer(() => debugAssembly = debug);
			debugAssembly = false;

			parts.DumpList(multiLine: true);
		}

		var length = parts.Count;
		if (parts[^1] is ResetPart { isForeground: null } reset && lastColourPart.currentForeground == reset.currentForeground && lastColourPart.currentBackground == reset.currentBackground) {
			length--;
			Return(parts[^1]);
		}

		Return(lastColourPart);

		i = 0;
		while (i < length) {
			var previousPart = parts[i];
			if (debugAssembly) {
				Console.Error.WriteLine($"Previous := {previousPart}");
			}

			while (++i < length) {
				var nextPart = parts[i];
				if (debugAssembly) {
					Console.Error.WriteLine($"Next     := {nextPart}");
					Console.Error.WriteLine($"Merging\t  {previousPart}{Environment.NewLine}\t& {nextPart}");
				}

				if (nextPart.MergeWith(previousPart, out Part mergedPart)) {
					Return(previousPart);
					previousPart = mergedPart;
					if (debugAssembly) {
						Console.Error.WriteLine($"\t=>{mergedPart}");
					}
				} else {
					if (debugAssembly) {
						Console.Error.WriteLine("\t=> No Merge.");
					}

					break;
				}
			}

			if (debugAssembly) {
				Console.Error.WriteLine($"<<< {previousPart}");
			}

			yield return previousPart;
		}
	}
}
