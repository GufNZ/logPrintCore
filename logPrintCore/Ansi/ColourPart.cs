using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace logPrintCore.Ansi;

internal abstract class ColourPart : Part, IEquatable<ColourPart> {
	#region Constants and Lookups

	private const byte FOREGROUND_FIELD = 3;
	private const byte BACKGROUND_FIELD = 4;

	private const byte BRIGHT_ADD = 6;

	private const string JOINER = ";";

	private const byte BRIGHT_BIT = 32;

	protected internal const string PREFIX = "\e[";
	protected const string SUFFIX = "m";

	protected const int ANSI_RESET_COLOUR = 9;
	protected internal const byte IS_BYTE = 0x7F;

	private static readonly CompositeFormat rgbFormat = CompositeFormat.Parse("8;2;{0};{1};{2}");


	protected internal static readonly ReadOnlyDictionary<string, byte> CodeToAnsiMap = new Dictionary<string, byte> {
		{ "k", 0 },
		{ "black", 0 },
		{ "r", 1 },
		{ "red", 1 },
		{ "g", 2 },
		{ "green", 2 },
		{ "y", 3 },
		{ "yellow", 3 },
		{ "b", 4 },
		{ "blue", 4 },
		{ "m", 5 },
		{ "magenta", 5 },
		{ "c", 6 },
		{ "cyan", 6 },
		{ "w", 7 },
		{ "white", 7 },

		{ "!", ANSI_RESET_COLOUR },
		{ "RESET", ANSI_RESET_COLOUR },

		{ "K", 0 | BRIGHT_BIT },
		{ "BLACK", 0 | BRIGHT_BIT },
		{ "R", 1 | BRIGHT_BIT },
		{ "RED", 1 | BRIGHT_BIT },
		{ "G", 2 | BRIGHT_BIT },
		{ "GREEN", 2 | BRIGHT_BIT },
		{ "Y", 3 | BRIGHT_BIT },
		{ "YELLOW", 3 | BRIGHT_BIT },
		{ "B", 4 | BRIGHT_BIT },
		{ "BLUE", 4 | BRIGHT_BIT },
		{ "M", 5 | BRIGHT_BIT },
		{ "MAGENTA", 5 | BRIGHT_BIT },
		{ "C", 6 | BRIGHT_BIT },
		{ "CYAN", 6 | BRIGHT_BIT },
		{ "W", 7 | BRIGHT_BIT },
		{ "WHITE", 7 | BRIGHT_BIT }
	}.AsReadOnly();

	private static readonly ReadOnlyDictionary<byte, ConsoleColor> ansiToConsoleColorMap = new Dictionary<byte, ConsoleColor> {
		{ 0, ConsoleColor.Black },
		{ 1, ConsoleColor.DarkRed },
		{ 2, ConsoleColor.DarkGreen },
		{ 3, ConsoleColor.DarkYellow },
		{ 4, ConsoleColor.DarkBlue },
		{ 5, ConsoleColor.DarkMagenta },
		{ 6, ConsoleColor.DarkCyan },
		{ 7, ConsoleColor.Gray },
		{ 0 | BRIGHT_BIT, ConsoleColor.DarkGray },
		{ 1 | BRIGHT_BIT, ConsoleColor.Red },
		{ 2 | BRIGHT_BIT, ConsoleColor.Green },
		{ 3 | BRIGHT_BIT, ConsoleColor.Yellow },
		{ 4 | BRIGHT_BIT, ConsoleColor.Blue },
		{ 5 | BRIGHT_BIT, ConsoleColor.Magenta },
		{ 6 | BRIGHT_BIT, ConsoleColor.Cyan },
		{ 7 | BRIGHT_BIT, ConsoleColor.White }
	}.AsReadOnly();

	private static readonly Dictionary<ConsoleColor, byte> consoleColorToAnsiMap = ansiToConsoleColorMap.ToDictionary(pair => pair.Value, pair => pair.Key);

	#endregion

	private static readonly byte defaultForeground = consoleColorToAnsiMap[Console.ForegroundColor];
	private static readonly byte defaultBackground = consoleColorToAnsiMap[Console.BackgroundColor];

	protected internal Color currentForeground = Color.FromArgb(IS_BYTE, defaultForeground, 0, 0);
	protected internal Color currentBackground = Color.FromArgb(IS_BYTE, defaultBackground, 0, 0);


	protected void Init(bool? isForegroundColour, Color colour) {
		currentForeground = Color.FromArgb(IS_BYTE, defaultForeground, 0, 0);
		currentBackground = Color.FromArgb(IS_BYTE, defaultBackground, 0, 0);

		isForeground = isForegroundColour;
		if (!isForegroundColour.HasValue) {
			return;
		}


		if (isForegroundColour.Value) {
			currentForeground = colour;
		} else {
			currentBackground = colour;
		}
	}


	protected internal bool? isForeground;
	protected internal bool HasForeground => isForeground != false;
	protected internal bool HasBackground => isForeground != true;


	private readonly StringBuilder _builder = new();


	public override bool MergeWith(Part previous, out Part merged) {
		if (previous is TextPart or PushPart or PopPart) {
			merged = null!;
			return false;
		}


		merged = this;
		if (previous is not ColourPart previousColour) {
			return true;
		}


		if (previousColour.HasBackground && !HasBackground) {
			currentBackground = previousColour.currentBackground;
			isForeground = null;
		}

		// ReSharper disable once InvertIf
		if (previousColour.HasForeground && !HasForeground) {
			currentForeground = previousColour.currentForeground;
			isForeground = null;
		}

		return true;
	}

	public override string ToAnsi() {
		_builder.Clear().Append(PREFIX);

		if (HasForeground) {
			_builder.Append(ToAnsiPart(FOREGROUND_FIELD, currentForeground));
			if (HasBackground) {
				_builder.Append(JOINER);
			}
		}

		if (HasBackground) {
			_builder.Append(ToAnsiPart(BACKGROUND_FIELD, currentBackground));
		}

		_builder.Append(SUFFIX);
		return _builder.ToString();
	}

	public override void ToConsole(TextWriter writer) {
		_builder.Clear();

		if (HasForeground) {
			if (currentForeground.A == IS_BYTE) {
				Console.ForegroundColor = (currentForeground.R == ANSI_RESET_COLOUR)
					? (ConsoleColor)defaultForeground
					: ansiToConsoleColorMap[currentForeground.R];
			} else {
				_builder.Append(ToAnsiPart(FOREGROUND_FIELD, currentForeground));
			}
		}

		if (HasBackground) {
			if (currentBackground.A == IS_BYTE) {
				Console.BackgroundColor = (currentBackground.R == ANSI_RESET_COLOUR)
					? (ConsoleColor)defaultBackground
					: ansiToConsoleColorMap[currentBackground.R];
			} else {
				if (_builder.Length > 0) {
					_builder.Append(JOINER);
				}

				_builder.Append(ToAnsiPart(BACKGROUND_FIELD, currentBackground));
			}
		}

		if (_builder.Length > 0) {
			writer.Write($"{PREFIX}{_builder}{SUFFIX}");
		}
	}


	private static string ToAnsiPart(byte colourField, Color colour) {
		return colour.A == IS_BYTE
			? $"{
				(
					((colour.R & BRIGHT_BIT) > 0)
						? (colourField + BRIGHT_ADD)
						: colourField
				)
			}{
				colour.R & ~BRIGHT_BIT
			}"
			: $"{colourField}{string.Format(null, rgbFormat, colour.R, colour.G, colour.B)}";
	}


	protected override string DebugOutput() {
		return $"(iF={
			isForeground.ToString()?.PadRight(5)
		}) F={
			(HasForeground
				? currentForeground.ToDebugString()
				: "  ")
		}, B={
			(HasBackground
				? currentBackground.ToDebugString()
				: "  ")
		}";
	}


	public bool Equals(ColourPart? other) {
		if (other is null) {
			return false;
		}


		if (ReferenceEquals(this, other)) {
			return true;
		}


		return currentForeground.Equals(other.currentForeground)
			&& currentBackground.Equals(other.currentBackground)
			&& isForeground == other.isForeground;
	}

	public override bool Equals(object? obj) {
		if (obj is null) {
			return false;
		}


		if (ReferenceEquals(this, obj)) {
			return true;
		}


		if (obj.GetType() != GetType()) {
			return false;
		}


		return Equals((ColourPart)obj);
	}

	public override int GetHashCode() {
		// ReSharper disable NonReadonlyMemberInGetHashCode - Effectively readonly except that this can be rented from a Pool, so needs Init.
		return HashCode.Combine(currentForeground, currentBackground, isForeground);
		// ReSharper restore NonReadonlyMemberInGetHashCode
	}

	public static bool operator==(ColourPart? left, ColourPart? right) {
		return Equals(left, right);
	}

	public static bool operator!=(ColourPart? left, ColourPart? right) {
		return !Equals(left, right);
	}
}
