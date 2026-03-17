using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

// {Ctrl+M, O} is your friend...

// ReSharper disable UnusedMember.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable MissingBlankLines - I like my overloads unspaced.

namespace logPrintCore.Utils;

internal static class StringExtensions {
	extension<T>(T thing)
		where T : struct {
		public T? NullIfDefault() {
			return Equals(thing, default)
				? null
				: thing;
		}

		public T? NullIf(T nullWhen) {
			return Equals(thing, nullWhen)
				? null
				: thing;
		}
	}


	extension<T>(T thing)
		where T : class {
		public T? NullWhen(T nullWhen) {
			return Equals(thing, nullWhen)
				? null
				: thing;
		}
	}


	/// <param name="str">The first part.</param>
	extension(string? str) {
		public string? NullIfEmpty() {
			// ReSharper disable once NullIfEmpty
			return string.IsNullOrEmpty(str)
				? null
				: str;
		}

		public string? NullIfWhitespace() {
			// ReSharper disable once NullIfWhitespace
			return string.IsNullOrWhiteSpace(str)
				? null
				: str;
		}

		public string? SafeTrim(params char[]? chars) {
			return str?.Trim(chars);
		}

		public string? TrimToNull(params char[]? chars) {
			return str?.Trim(chars).NullIfEmpty();
		}

		public bool FuzzyContains(string substr, StringComparison stringComparison) {
			return ((str?.IndexOf(substr, stringComparison) ?? -1) != -1);
		}

		/// <summary>Like <see cref="string.Concat(IEnumerable{string})"/> except that if any part is null, the entire result is null.</summary>
		/// <param name="parts">The rest of the parts.</param>
		/// <returns>The concatenated result, or null.</returns>
		/// <example>var greeting = "Hello ".RCoalesce(FirstName, "!") ?? "Hi,";</example>
		public string? RCoalesce(params ReadOnlySpan<string?> parts) {
			List<string?> all = [str];

			all.AddRange(parts);

			return all.Any(s => s == null)
				? null
				: string.Concat(all);
		}
	}


	private static readonly MethodInfo stringGetNonRandomizedHashCode = typeof(string).GetMethod("GetNonRandomizedHashCode", BindingFlags.NonPublic | BindingFlags.Instance)!;


	extension(string str) {
		public string Unescape(params (string oldValue, string newValue)[] replacements) {
			// ReSharper disable once StringLiteralTypo
			const string CHARS = "abefnrtv0\\";
			const string REPLACES = "\a\b\e\f\n\r\t\v\0\\";
			return Regex.Replace(
				str,
				@"(?<esc>\\)?\\(?<ch>.)",
				match => {
					if (match.Groups["esc"].Success) {
						return match.Value[1..];
					}


					var index = CHARS.IndexOf(match.Groups["ch"].Value, StringComparison.Ordinal);
					if (index > -1) {
						return REPLACES[index].ToString();
					}


					var other = replacements
						.FirstOrDefault(pair => pair.oldValue == match.Groups["ch"].Value)
						.NullIfDefault();

					return other?.newValue ?? match.Value;
				}
			);
		}

		public int GetStableHashCode() {
			return (int)stringGetNonRandomizedHashCode.Invoke(str, [])!;
		}

		public int Occurrences(char item) {
			return str.AsSpan().Occurrences(item);
		}

		public int Occurrences(params ReadOnlySpan<char> items) {
			return str.AsSpan().Occurrences(items);
		}
	}


	extension(ReadOnlySpan<char> str) {
		public int Occurrences(char item) {
			int count = 0;
			int index;
			while ((index = str.IndexOf(item)) >= 0) {
				count++;
				str = str[(index + 1)..];
			}

			return count;
		}
		public int Occurrences(params ReadOnlySpan<char> items) {
			int count = 0;
			int index;
			while ((index = str.IndexOfAny(items)) >= 0) {
				count++;
				str = str[(index + 1)..];
			}

			return count;
		}
	}


	extension(string? str) {
		#region Integers

		public byte? TryParseByte(NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return byte.TryParse(str, numberStyle, formatProvider ?? NumberFormatInfo.CurrentInfo, out byte value)
				? value
				: null;
		}
		public byte TryParseByte(byte defaultValue, NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return str.TryParseByte(numberStyle, formatProvider) ?? defaultValue;
		}

		public short? TryParseShort(NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return short.TryParse(str, numberStyle, formatProvider ?? NumberFormatInfo.CurrentInfo, out short value)
				? value
				: null;
		}
		public short TryParseShort(short defaultValue, NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return str.TryParseShort(numberStyle, formatProvider) ?? defaultValue;
		}

		public ushort? TryParseUShort(NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return ushort.TryParse(str, numberStyle, formatProvider ?? NumberFormatInfo.CurrentInfo, out ushort value)
				? value
				: null;
		}
		public ushort TryParseUShort(ushort defaultValue, NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return str.TryParseUShort(numberStyle, formatProvider) ?? defaultValue;
		}

		public int? TryParseInt(NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return int.TryParse(str, numberStyle, formatProvider ?? NumberFormatInfo.CurrentInfo, out int value)
				? value
				: null;
		}
		public int TryParseInt(int defaultValue, NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return str.TryParseInt(numberStyle, formatProvider) ?? defaultValue;
		}

		public uint? TryParseUInt(NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return uint.TryParse(str, numberStyle, formatProvider ?? NumberFormatInfo.CurrentInfo, out uint value)
				? value
				: null;
		}
		public uint TryParseUInt(uint defaultValue, NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return str.TryParseUInt(numberStyle, formatProvider) ?? defaultValue;
		}

		public long? TryParseLong(NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return long.TryParse(str, numberStyle, formatProvider ?? NumberFormatInfo.CurrentInfo, out long value)
				? value
				: null;
		}
		public long TryParseLong(long defaultValue, NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return str.TryParseLong(numberStyle, formatProvider) ?? defaultValue;
		}

		public ulong? TryParseULong(NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return ulong.TryParse(str, numberStyle, formatProvider ?? NumberFormatInfo.CurrentInfo, out ulong value)
				? value
				: null;
		}
		public ulong TryParseULong(ulong defaultValue, NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return str.TryParseULong(numberStyle, formatProvider) ?? defaultValue;
		}

		#endregion

		#region Floating Point

		public float? TryParseFloat(NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return float.TryParse(str, numberStyle, formatProvider ?? NumberFormatInfo.CurrentInfo, out float value)
				? value
				: null;
		}
		public float TryParseFloat(float defaultValue, NumberStyles numberStyle = NumberStyles.Float | NumberStyles.AllowThousands, IFormatProvider? formatProvider = null) {
			return str.TryParseFloat(numberStyle, formatProvider) ?? defaultValue;
		}

		public double? TryParseDouble(NumberStyles numberStyle = NumberStyles.Integer, IFormatProvider? formatProvider = null) {
			return double.TryParse(str, numberStyle, formatProvider ?? NumberFormatInfo.CurrentInfo, out double value)
				? value
				: null;
		}
		public double TryParseDouble(double defaultValue, NumberStyles numberStyle = NumberStyles.Float | NumberStyles.AllowThousands, IFormatProvider? formatProvider = null) {
			return str.TryParseDouble(numberStyle, formatProvider) ?? defaultValue;
		}

		#endregion
	}


	extension(string? str) {
		public TimeSpan? TryParseTimeSpan(IFormatProvider? formatProvider = null) {
			return TimeSpan.TryParse(str, formatProvider, out TimeSpan value)
				? value
				: null;
		}
		public TimeSpan TryParseTimeSpan(TimeSpan defaultValue, IFormatProvider? formatProvider = null) {
			return str.TryParseTimeSpan(formatProvider) ?? defaultValue;
		}

		public DateTimeOffset? TryParseDateTimeOffset(IFormatProvider? formatProvider = null, DateTimeStyles dateTimeStyles = DateTimeStyles.None) {
			return DateTimeOffset.TryParse(str, formatProvider, dateTimeStyles, out DateTimeOffset value)
				? value
				: null;
		}
		public DateTimeOffset TryParseDateTimeOffset(DateTimeOffset defaultValue, IFormatProvider? formatProvider = null, DateTimeStyles dateTimeStyles = DateTimeStyles.None) {
			return str.TryParseDateTimeOffset(formatProvider, dateTimeStyles) ?? defaultValue;
		}

		public DateTime? TryParseDateTime(IFormatProvider? formatProvider = null, DateTimeStyles dateTimeStyles = DateTimeStyles.None) {
			return DateTime.TryParse(str, formatProvider, dateTimeStyles, out DateTime value)
				? value
				: null;
		}
		public DateTime TryParseDateTime(DateTime defaultValue, IFormatProvider? formatProvider = null, DateTimeStyles dateTimeStyles = DateTimeStyles.None) {
			return str.TryParseDateTime(formatProvider, dateTimeStyles) ?? defaultValue;
		}

		public DateTime? TryParseDateTimeExact(string format, IFormatProvider? formatProvider = null, DateTimeStyles style = DateTimeStyles.None) {
			return DateTime.TryParseExact(str, format, formatProvider, style, out DateTime value)
				? value
				: null;
		}
		public DateTime? TryParseDateTimeExact(string[] formats, IFormatProvider? formatProvider = null, DateTimeStyles style = DateTimeStyles.None) {
			return DateTime.TryParseExact(str, formats, formatProvider, style, out DateTime value)
				? value
				: null;
		}
		public DateTime TryParseDateTimeExact(DateTime defaultValue, string format, IFormatProvider? formatProvider = null, DateTimeStyles dateTimeStyles = DateTimeStyles.None) {
			return str.TryParseDateTimeExact(format, formatProvider, dateTimeStyles) ?? defaultValue;
		}
		public DateTime TryParseDateTimeExact(DateTime defaultValue, string[] formats, IFormatProvider? formatProvider = null, DateTimeStyles dateTimeStyles = DateTimeStyles.None) {
			return str.TryParseDateTimeExact(formats, formatProvider, dateTimeStyles) ?? defaultValue;
		}
	}


	#region Uri

	extension(string? str) {
		public Uri? TryParseUri(UriKind uriKind = UriKind.RelativeOrAbsolute) {
			return Uri.TryCreate(str, uriKind, out Uri? value)
				? value
				: null;
		}
	}

	#endregion
}
