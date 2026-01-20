using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

using logPrintCore.Utils;

namespace logPrintCore.Ansi;

internal sealed class PushPart : ColourPart, IEquatable<PushPart>, IRentable<PushPart> {
	private static readonly Color pushMarker = Color.FromArgb(IS_BYTE, 0xF0, 0, 0);
	private static readonly Color unsetMarker = Color.FromArgb(IS_BYTE, 0xCC, 0, 0);


	private PushPart() { }


	public PushPart Init(bool? isForegroundColour) {
		Init(isForegroundColour, pushMarker);
		pushedForeground = unsetMarker;
		pushedBackground = unsetMarker;
		return this;
	}


	public static PushPart Create() {
		return new();
	}


	public Color pushedForeground = unsetMarker;
	public Color pushedBackground = unsetMarker;


	public LinkedListNode<PushPart>? Node { get; set; }


	public override bool MergeWith(Part previous, out Part merged) {
		var push = previous as PushPart;
		if (push?.isForeground == null || push.isForeground == isForeground) {
			merged = null!;
			return false;
		}


		merged = this;

		if (HasForeground) {
			pushedBackground = push.pushedBackground;
		} else {
			pushedForeground = push.pushedForeground;
		}

		isForeground = null;
		currentBackground = currentForeground = pushMarker;

		return true;
	}


	public override string ToAnsi() {
		return "";
	}

	public override void ToConsole(TextWriter writer) { }


	protected override string DebugOutput() {
		return $"{
			base.DebugOutput()
		} PF={
			(HasForeground
				? pushedForeground.ToDebugString()
				: "  ")
		}, PB={
			(HasBackground
				? pushedBackground.ToDebugString()
				: "  ")
		}";
	}


	/// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
	/// <param name="other">An object to compare with this object.</param>
	/// <returns>
	/// <see langword="true" /> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <see langword="false" />.</returns>
	public bool Equals(PushPart? other) {
		if (ReferenceEquals(null, other)) {
			return false;
		}


		if (ReferenceEquals(this, other)) {
			return true;
		}


		return base.Equals(other) && pushedForeground == other.pushedForeground && pushedBackground == other.pushedBackground;
	}

	/// <summary>Determines whether the specified object is equal to the current object.</summary>
	/// <param name="obj">The object to compare with the current object.</param>
	/// <returns>
	/// <see langword="true" /> if the specified object  is equal to the current object; otherwise, <see langword="false" />.</returns>
	public override bool Equals(object? obj) {
		return ReferenceEquals(this, obj) || obj is PushPart other && Equals(other);
	}

	/// <summary>Serves as the default hash function.</summary>
	/// <returns>A hash code for the current object.</returns>
	public override int GetHashCode() {
		// ReSharper disable NonReadonlyMemberInGetHashCode
		return HashCode.Combine(base.GetHashCode(), pushedForeground, pushedBackground);
		// ReSharper restore NonReadonlyMemberInGetHashCode
	}

	/// <summary>Returns a value that indicates whether the values of two <see cref="T:logPrintCore.Ansi.PushPart" /> objects are equal.</summary>
	/// <param name="left">The first value to compare.</param>
	/// <param name="right">The second value to compare.</param>
	/// <returns>true if the <paramref name="left" /> and <paramref name="right" /> parameters have the same value; otherwise, false.</returns>
	public static bool operator==(PushPart? left, PushPart? right) {
		return Equals(left, right);
	}

	/// <summary>Returns a value that indicates whether two <see cref="T:logPrintCore.Ansi.PushPart" /> objects have different values.</summary>
	/// <param name="left">The first value to compare.</param>
	/// <param name="right">The second value to compare.</param>
	/// <returns>true if <paramref name="left" /> and <paramref name="right" /> are not equal; otherwise, false.</returns>
	public static bool operator!=(PushPart? left, PushPart? right) {
		return !Equals(left, right);
	}
}
