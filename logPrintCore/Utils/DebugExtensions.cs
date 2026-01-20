using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using logPrintCore.Ansi;
using logPrintCore.Config.Flags;

namespace logPrintCore.Utils;

internal static class DebugExtensions {
	private static readonly ReferenceEqualityComparer<object?> referenceEqualityComparer = new();


	// ReSharper disable once MemberCanBePrivate.Global
	// ReSharper disable once UnusedMethodReturnValue.Global
	public static IEnumerable<T>? DumpList<T>(
		this IEnumerable<T>? input,
		string? title = null,
		bool multiLine = false,
		Func<Type, PropertyInfo, bool>? propFilter = null,
		Func<Type, PropertyInfo, bool>? recurseFilter = null,
		string? indent = null,
		Stack<object?>? history = null,
		bool? escapeColours = true,
		bool? stripColours = false,
		Func<T?, object?>? evalItem = null,
		[CallerFilePath] string path = null!,
		[CallerLineNumber] int line = 0,
		[CallerMemberName] string member = null!
	) {
		var titleString = "~c~[~C~".RCoalesce(title, "~c~] ~B~= ");
		var prefix = indent ?? $"~Y~{path}~w~:~C~{line} ~w~: ~g~{member}~w~: ";

		if (input == null) {
			Console.Error.WriteLineColours($"{prefix}{titleString}~R~#b#<null>");
			return null;
		}


		var list = input as IList<T> ?? input.ToList();
		if (propFilter == null) {
			var content = multiLine
				? Environment.NewLine.RCoalesce(
					indent + "\t",
					string
						.Join(
							$"~m~,~!~{Environment.NewLine}{indent}\t",
							list.Select(x => FormatThing(x, escapeColours, stripColours, evalItem))
						)
						.NullIfEmpty(),
					Environment.NewLine + indent
				)
				: string.Join("~m~,~!~ ", list.Select(x => FormatThing(x, escapeColours, stripColours, evalItem)));

			Console.Error.WriteLineColours($"{prefix}{titleString}~m~[~!~{content}~m~]");
		} else if (!list.Any()) {
			Console.Error.WriteLineColours($"{prefix}{titleString}~m~[]");
		} else {
			var subIndent = indent + " ";
			Console.Error.WriteLineColours($"{prefix}{titleString}~m~[");
			var i = 0;
			foreach (var subThing in list) {
				subThing.Dump($"{title}~g~[~G~{i++}~g~]", multiLine, propFilter, recurseFilter, subIndent, history, escapeColours, stripColours);
			}

			Console.Error.WriteLineColours($"{prefix}{titleString}~m~]");
		}

		return list;
	}

	// ReSharper disable once MemberCanBePrivate.Global
	// ReSharper disable once UnusedMethodReturnValue.Global
	public static T? Dump<T>(
		this T? thing,
		string? title = null,
		bool multiLine = false,
		Func<Type, PropertyInfo, bool>? propFilter = null,
		Func<Type, PropertyInfo, bool>? recurseFilter = null,
		string? indent = null,
		Stack<object?>? history = null,
		bool? escapeColours = true,
		bool? stripColours = false,
		Func<T?, object?>? eval = null,
		[CallerFilePath] string path = null!,
		[CallerLineNumber] int line = 0,
		[CallerMemberName] string member = null!
	) {
		var titleStr = "~c~[~C~".RCoalesce(title, "~c~] ~w~= ");
		var prefix = indent ?? $"~Y~{path}~w~:~C~{line} ~w~: ~g~{member}~w~: ";

		if (Equals(thing, default(T))) {
			Console.Error.WriteLineColours($"{prefix}{titleStr}~R~#b#<null>");
			return default;
		}


		history ??= new();
		if (history.Contains(thing, referenceEqualityComparer)) {
			Console.Error.WriteLineColours($"{prefix}{titleStr}~R~#y#<Circular>");
			return thing;
		}


		history.Push(thing);

		if (thing is not string) {
			var collection = thing as IEnumerable;
			collection?.Cast<object>()
				.ToList()
				.DumpList(title, multiLine, propFilter, recurseFilter, prefix, history, escapeColours, stripColours);
		}

		Console.Error.WriteLineColours($"{prefix}{titleStr}~y~'~W~{FormatThing(thing, escapeColours, stripColours, eval)}~y~'");

		if (multiLine) {
			foreach (
				var propertyInfo
				in thing!
					.GetType()
					.GetProperties(BindingFlags.Public | BindingFlags.Instance)
					.Where(propertyInfo => (propFilter ?? ((_, _) => true))(thing.GetType(), propertyInfo))
					.Where(propertyInfo => propertyInfo.GetMethod?.GetParameters().Length == 0)
			) {
				if (recurseFilter?.Invoke(thing.GetType(), propertyInfo) == true) {
					propertyInfo.GetValue(thing)
						.Dump($"{title}~R~/~y~{propertyInfo.Name}", multiLine: true, propFilter, recurseFilter, prefix, history, escapeColours, stripColours);
				} else {
					Console.Error.WriteLineColours(
						$"{
							prefix
						}{
							"~c~[~C~".RCoalesce(title, "~B~.~c~", propertyInfo.Name, "~c~]") ?? $"~B~.~c~{propertyInfo.Name}"
						} ~B~= ~y~'~W~{
							(
								escapeColours ?? true
									? propertyInfo.GetValue(thing)?.ToString().EscapeColourCodeChars()
									: propertyInfo.GetValue(thing)?.ToString()
							)
						}~y~'"
					);
				}
			}
		}

		history.Pop();

		return thing;
	}

	private static string? FormatThing<T>(T thing, bool? escapeColours, bool? stripColours, Func<T, object?>? eval = null) {
		eval ??= x => x;
		var it = eval(thing)?.ToString();

		return escapeColours ?? true
			? it.EscapeColourCodeChars()
			: stripColours ?? false
				? it.StripColourCodes()
				: it;
	}
}
