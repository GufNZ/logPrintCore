using System;
using System.IO;

using logPrintCore.Utils;

namespace logPrintCore.Ansi;

internal sealed class TextPart : Part, IEquatable<TextPart>, IRentable {
	public string text = null!;


	private TextPart() { }


	public TextPart Init(string plainText) {
		text = plainText;
		return this;
	}


	public static TextPart Create() {
		return new();
	}

#if DEBUG
	// ReSharper disable once UnusedMember.Global
	public string Here { get; set; } = null!;
#endif


	public override bool MergeWith(Part previous, out Part merged) {
		merged = null!;
		return false;
	}

	public override string ToAnsi() {
		return text;
	}

	public override void ToConsole(TextWriter writer) {
		writer.Write(text);
	}


	protected override string DebugOutput() {
		return text;
	}


	public bool Equals(TextPart? other) {
		if (ReferenceEquals(null, other)) {
			return false;
		}


		if (ReferenceEquals(this, other)) {
			return true;
		}


		return (text == other.text);
	}

	public override bool Equals(object? obj) {
		return (ReferenceEquals(this, obj) || obj is TextPart other && Equals(other));
	}

	public override int GetHashCode() {
		// ReSharper disable once NonReadonlyMemberInGetHashCode - Effectively readonly except that this can be rented from a Pool, so needs Init.
		// ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
		return text?.GetHashCode() ?? 0;
	}

	public static bool operator==(TextPart? left, TextPart? right) {
		return Equals(left, right);
	}

	public static bool operator!=(TextPart? left, TextPart? right) {
		return !Equals(left, right);
	}
}
