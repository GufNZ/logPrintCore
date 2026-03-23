#if DEBUG
//B#define DEBUG_MATCHING
#endif

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
#if DEBUG_MATCHING
using System.Reflection;
#endif
using System.Text;
using System.Text.RegularExpressions;

using JetBrains.Annotations;

using logPrintCore.Ansi;
using logPrintCore.Utils;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace logPrintCore.Config.Rules;

internal partial class Rule {
	private static readonly JsonSerializerSettings jsonSerializerSettings = new() {
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Double
	};


#if DEBUG_MATCHING
	private readonly Func<Type, PropertyInfo, bool> _propFilter = (_, p) => new[] {
		nameof(Name),
		nameof(Test),
		nameof(Match),
		nameof(SubRules),
		nameof(Parse),
		nameof(Repeat),
		nameof(Push),
	}.Contains(p.Name);

	private readonly Func<Type, PropertyInfo, bool> _recurseFilter = (_, p) => p.Name == nameof(SubRules);

#endif
	private string _replaceStr = null!;

	private JContainer? _json;
	private JToken? _originalJson;
	private SafeDictionary<string, string> _jsonLookup = null!;


	[Required]
	public string Name { get; [UsedImplicitly] set; } = null!;

	// ReSharper disable MemberCanBePrivate.Global
	public Regex? Test { get; [UsedImplicitly] set; } = null!;

	// ReSharper disable once MemberCanBeProtected.Global
	[Required]
	public Regex Match { get; init; } = null!;

	public ParseType Parse { get; set; }

	[Required(AllowEmptyStrings = true)]
	public string Replace {
		get => _replaceStr;
		[UsedImplicitly]
		set =>
			_replaceStr = value.Unescape();
	}

	public bool Repeat { get; set; }

	public bool Push { get; [UsedImplicitly] set; } = true;

	public Rule[] Groups { get; [UsedImplicitly] set; } = [];
	// ReSharper restore MemberCanBePrivate.Global


	private Rule? Parent { get; set; }

	private List<Rule> SubRules {
		get {
			if (field != null) {
				return field;
			}


			field = new List<Rule>(Groups.Length);
			foreach (var rule in Groups) {
				rule.Parent = this;
				field.Add(rule);
			}

			return field;
		}
	}

	private List<ReplacePart> ProcessedReplace {
		get {
			if (field != null) {
				return field;
			}


			var replaceSpan = Replace.AsSpan();
			field = new(replaceSpan.Occurrences('$') + 1);

			var i = 0;
			var j = replaceSpan.IndexOf('$');
			while (j != -1) {
				if (i < j) {
					field.Add(new(replaceSpan[i..j].ToString()));
				}

				if (replaceSpan[j + 1] == '{') {
					i = j + 2;

					var closeBraceIndex = replaceSpan[i..].IndexOf('}');
					j = (closeBraceIndex == -1)
						? -1
						: i + closeBraceIndex;

					var substring = replaceSpan[i..j];
					field.Add(
						(Parse != ParseType.None && substring.StartsWith("JSON.", StringComparison.Ordinal))
							? new JsonReplacePart(substring.ToString())
							: new ReplacePart(substring.ToString(), isGroup: true)
					);

					i = j + 1;
				} else {
					j = i = j + 1;
					while (++j < replaceSpan.Length && replaceSpan[j] >= '0' && replaceSpan[j] <= '9') { }

					field.Add(new(int.Parse(replaceSpan[i..j])));
					i = j;
				}

				var nextDollarIndex = replaceSpan[i..].IndexOf('$');
				j = (nextDollarIndex == -1)
					? -1
					: i + nextDollarIndex;
			}

			if (i < replaceSpan.Length) {
				field.Add(new(replaceSpan[i..].ToString()));
			}

#if DEBUG_MATCHING
			field.Dump(multiLine: true);

#endif
			return field;
		}
	}


	private readonly StringBuilder _resultBuilder = new();
	private readonly StringBuilder _jsonBuilder = new();


	public Rule ProcessVars(IDictionary<string, string?> vars) {
		_jsonLookup = new(SafeDictionary<string, string>.MissingKeyOperation.ReturnDefault);
		vars
			.Where(var => var.Key.StartsWith("JSON", StringComparison.Ordinal))
			.ToList()
			.ForEach(var => _jsonLookup.Add(var.Key[4..], var.Value?.Unescape()));

		_replaceStr = VarReplaceRE().Replace(Replace, match => vars[match.Groups[1].Value] ?? "");

		SubRules.ForEach(rule => rule.ProcessVars(vars));

		return this;
	}

	public virtual string Process(string? line) {
		line ??= "";
		if (Parse != ParseType.None) {
			// ReSharper disable once InvertIf
			if (Repeat) {
				while ((Test?.IsMatch(line) ?? true) && Match.IsMatch(line)) {
					line = Match.Replace(line, ProcessJson);
				}

				return line;
			}


			return (Test?.IsMatch(line) ?? true)
				? Match.Replace(line, ProcessJson)
				: line;
		}


		if (SubRules.Count > 0) {
			return ProcessWithSubRules(line);
		}


#if DEBUG_MATCHING
		bool isMatch = Test?.IsMatch(line) ?? true;
		Test.Dump(Name + "|TestRE ");
		isMatch.Dump(Name + "|Test");
		if (isMatch) {
			this.Dump(Name + "=", true, _propFilter, _recurseFilter);
			Match.IsMatch(line).Dump(Name + "|Match");
			Match.Replace(line, $"~<~#<#{Replace}#>#~>~").Dump(Name + "|=result");
		}

#endif
		// ReSharper disable once InvertIf
		if (Repeat) {
			while ((Test?.IsMatch(line) ?? true) && Match.IsMatch(line)) {
				line = Match.Replace(
					line,
					Push
						? AnsiConsoleColourExtensions.PUSH_COLOURS
							.RCoalesce(Replace.NullIfEmpty(), AnsiConsoleColourExtensions.POP_COLOURS)
						?? ""
						: Replace
				);
			}

			return line;
		}


		return (Test?.IsMatch(line) ?? true)
			? Match.Replace(
				line,
				Push
					? AnsiConsoleColourExtensions.PUSH_COLOURS
						.RCoalesce(Replace.NullIfEmpty(), AnsiConsoleColourExtensions.POP_COLOURS)
					?? ""
					: Replace
			)
			: line;
	}

	private string ProcessJson(Match match) {
		if (Parse == ParseType.Parent) {
			_json = Parent!._json;
			_originalJson = Parent._originalJson;
		} else {
			var jsonStr = match.Groups["JSON"].Value.NullIfEmpty() ?? match.Value;
			if (jsonStr.Contains('\\') && jsonStr.IndexOf('\\') < jsonStr.IndexOf('"')) {
				jsonStr = jsonStr.Unescape(("\\\"", "\""));
			}

			try {
				_json = (JContainer?)JsonConvert.DeserializeObject(jsonStr, jsonSerializerSettings);
			} catch (JsonReaderException jsonReaderException) {
				var lines = jsonStr.Replace("\r", "").Split('\n');
				for (var line = 1; line <= jsonReaderException.LineNumber; line++) {
					Console.Error.WriteLineColours(
						$"~{
							((line == jsonReaderException.LineNumber)
								? 'Y'
								: 'w')
						}~#R#"
						+ lines[line - 1].EscapeColourCodeChars()
					);
				}

				Console.Error.WriteLineColours($"~C~#R#{new string('-', jsonReaderException.LinePosition)}^");
				_json = null;
			}


			_originalJson = _json?.DeepClone();
		}

		return ProcessMatch(match);
	}


	private string ProcessWithSubRules(string line) {
#if DEBUG_MATCHING
		bool isMatch = Test?.IsMatch(line) ?? true;
		Test.Dump(Name + "|TestRE ");
		isMatch.Dump(Name + "|Test");
		if (isMatch) {
			this.Dump(Name + "=", true, _propFilter, _recurseFilter);
			Match.IsMatch(line).Dump(Name + "|Match");
			Match.Replace(line, $"~<~#<#{Replace}#>#~>~").Dump(Name + "|=result");
		}

#endif
		// ReSharper disable once InvertIf
		if (Repeat) {
			while ((Test?.IsMatch(line) ?? true) && Match.IsMatch(line)) {
				line = Match.Replace(line, ProcessMatch);
			}

			return line;
		}


		return (Test?.IsMatch(line) ?? true)
			? Match.Replace(line, ProcessMatch)
			: line;
	}

	private string ProcessMatch(Match match) {
		_resultBuilder.Clear();
#if DEBUG_MATCHING
		match.Dump("match", true, (_, p) => new[] { "Success", "Groups", "Name", "Value" }.Contains(p.Name), (_, p) => p.Name == "Groups");
#endif
		foreach (var part in ProcessedReplace) {
#if DEBUG_MATCHING
			part.Dump("part ");
#endif
			switch (part) {
				case JsonReplacePart jsonReplacePart:
					FormatJson(jsonReplacePart.evaluate(_json, match), jsonReplacePart.compactJson);
					_resultBuilder.Append(ProcessGroup(_jsonBuilder.ToString(), part.groupName!));
					_jsonBuilder.Clear();
					break;

				case { groupName: not null }:
					_resultBuilder.Append(ProcessGroup(match.Groups[part.groupName], part.groupName));
					break;

				case { groupNumber: not null }:
					// ReSharper disable once ArgumentsStyleOther
					_resultBuilder.Append(ProcessGroup(match.Groups[part.groupNumber.Value], part.groupNumber.Value.ToString()));
					break;

				default:
					_resultBuilder.Append(part.text);
					break;
			}
		}

#if DEBUG_MATCHING
		result.Dump("result");
#endif
		return Push
			? AnsiConsoleColourExtensions.PUSH_COLOURS
				.RCoalesce(_resultBuilder.ToString().NullIfEmpty(), AnsiConsoleColourExtensions.POP_COLOURS)
			?? ""
			: _resultBuilder.ToString();
	}

	private string ProcessGroup(Group matchGroup, string name) {
#if DEBUG_MATCHING
		matchGroup.Dump("group", true);
#endif
		return matchGroup.Success
			? ProcessGroup(matchGroup.Value, name)
			: "";
	}

	private string ProcessGroup(string str, string name) {
		foreach (var subRule in SubRules) {
			var dotIndex = subRule.Name.IndexOf('.');
			var firstName = dotIndex == -1
				? subRule.Name
				: subRule.Name.Substring(0, dotIndex);

			if (firstName == name) {
				str = subRule.Process(str)
#if DEBUG_MATCHING
					.Dump("groupResult")!
#endif
					;
			}
		}


		return str;
	}


	public (string? line, string marker, LogLevel level) Summarise((string? line, string marker, LogLevel level) result) {
		var level = (LogLevel)Enum.Parse(typeof(LogLevel), Name);
		return (Test?.IsMatch(result.line ?? "") ?? true)
			? Match.IsMatch(result.line ?? "")
				? (
					result.line,
					Match.Replace(
						((char)level).ToString(),
						Push
							? AnsiConsoleColourExtensions.PUSH_COLOURS
								.RCoalesce(Replace.NullIfEmpty(), AnsiConsoleColourExtensions.POP_COLOURS)
							?? ""
							: Replace
					),
					level
				)
				: result
			: result;
	}


	private void FormatJson(JToken? jToken, JsonStyle style, bool coloursSimpleValues = false, string indent = "") {
		if (jToken == null) {
			return;
		}


		var newStyle = style switch {
			JsonStyle.InitialCompact => JsonStyle.Expanded,
			_ => style
		};
		var newIndent = style == JsonStyle.Expanded
			? indent + (_jsonLookup["Indent"] ?? "\t")
			: "";

		// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault - see default case trivia
		switch (jToken.Type) {
			case JTokenType.Object:
				_jsonBuilder.Append(AnsiConsoleColourExtensions.PUSH_COLOURS).Append(_jsonLookup["Brace"]).Append('{');

				var propsEnumerable = ((JObject)jToken).Properties();
				var props = propsEnumerable as IList<JProperty> ?? propsEnumerable.ToList();
				if (props.Any()) {
					if (style == JsonStyle.CompactUnlessComplex && props.Count > 1) {
						newStyle = JsonStyle.Expanded;
						newIndent = indent + (_jsonLookup["Indent"] ?? "\t");
					}

					var expanded = (style == JsonStyle.Expanded || style == JsonStyle.CompactUnlessComplex && newStyle == JsonStyle.Expanded);
					if (expanded) {
						_jsonBuilder.Append(_jsonLookup["Newline"]);
					}

					for (var i = 0; i < props.Count; i++) {
						JProperty jProperty = props[i];
						_jsonBuilder.Append(newIndent)
							.Append(_jsonLookup["Prop"])
							.Append(jProperty.Name)
							.Append(
								expanded
									? _jsonLookup["Colon"]
									: _jsonLookup["Colon"]?.TrimEnd()
							);

						FormatJson(jProperty.Value, newStyle, coloursSimpleValues: true, newIndent);

						if (i < props.Count - 1) {
							_jsonBuilder.Append(
								expanded
									? _jsonLookup["Comma"]
									: _jsonLookup["Comma"]?.TrimEnd()
							);
						}

						if (expanded) {
							_jsonBuilder.Append(_jsonLookup["Newline"]);
						}
					}

					_jsonBuilder.Append(indent).Append(_jsonLookup["Brace"]);
				} else if (!coloursSimpleValues) {
					_jsonBuilder.Append("{}");
					return;
				}


				_jsonBuilder.Append('}').Append(AnsiConsoleColourExtensions.POP_COLOURS);
				break;

			case JTokenType.Array:
				_jsonBuilder.Append(AnsiConsoleColourExtensions.PUSH_COLOURS).Append(_jsonLookup["Bracket"]).Append('[');

				var arr = ((JArray)jToken);
				if (arr.Any()) {
					if (style == JsonStyle.CompactUnlessComplex && arr.Count > 1) {
						newStyle = JsonStyle.Expanded;
						newIndent = indent + (_jsonLookup["Indent"] ?? "\t");
					}

					var expanded = (style == JsonStyle.Expanded || style == JsonStyle.CompactUnlessComplex && newStyle == JsonStyle.Expanded);
					if (expanded) {
						_jsonBuilder.Append(_jsonLookup["Newline"]);
					}

					for (var i = 0; i < arr.Count; i++) {
						_jsonBuilder.Append(newIndent);

						FormatJson(arr[i], newStyle, coloursSimpleValues: true, newIndent);

						if (i < arr.Count - 1) {
							_jsonBuilder.Append(expanded
								? _jsonLookup["Comma"]
								: _jsonLookup["Comma"]?.TrimEnd()
							);
						}

						if (expanded) {
							_jsonBuilder.Append(_jsonLookup["Newline"]);
						}
					}

					_jsonBuilder.Append(indent).Append(_jsonLookup["Bracket"]);
				} else if (!coloursSimpleValues) {
					_jsonBuilder.Append("[]");
					return;
				}


				_jsonBuilder.Append(']').Append(AnsiConsoleColourExtensions.POP_COLOURS);
				break;

			case JTokenType.Integer:
				ConditionalFormat(_jsonLookup["Int"] ?? _jsonLookup["Float"]);
				break;

			case JTokenType.Float:
				ConditionalFormat(_jsonLookup["Float"] ?? _jsonLookup["Int"]);
				break;

			case JTokenType.String:
				ConditionalFormat(_jsonLookup["String"]);
				break;

			case JTokenType.Boolean:
				ConditionalFormat(_jsonLookup["Bool"]);
				break;

			case JTokenType.Null:
				AlwaysFormat(_jsonLookup["Null"]);
				break;

			case JTokenType.Undefined:
				AlwaysFormat(_jsonLookup["Undefined"]);
				break;

			/*
			case JTokenType.None:
			case JTokenType.Constructor:
			case JTokenType.Property:
			case JTokenType.Comment:
			case JTokenType.Raw:
			case JTokenType.Bytes:
			case JTokenType.Date:
			case JTokenType.Guid:
			case JTokenType.Uri:
			case JTokenType.TimeSpan:
			*/
			default:
				throw new ArgumentOutOfRangeException(nameof(jToken), jToken.Type, $"Unhandled JToken.Type: `{jToken.Type}`!");
		}


		return;


		void ConditionalFormat(string? format) {
			if (coloursSimpleValues) {
				_jsonBuilder.Append(AnsiConsoleColourExtensions.PUSH_COLOURS).Append(format);
			}

			_jsonBuilder.Append(jToken);

			if (coloursSimpleValues) {
				_jsonBuilder.Append(AnsiConsoleColourExtensions.POP_COLOURS);
			}
		}

		void AlwaysFormat(string? format) {
			_jsonBuilder.Append(AnsiConsoleColourExtensions.PUSH_COLOURS).Append(format).Append(jToken).Append(AnsiConsoleColourExtensions.POP_COLOURS);
		}
	}


	public override string ToString() {
		return $"{
			GetType().Name
		}: {
			Name
		}{
			(Parse == ParseType.None
				? ""
				: $", {nameof(Parse)}: {Parse}"
			)
		}{
			(Repeat
				? $", {nameof(Repeat)}"
				: ""
			)
		}{
			(Push
				? ""
				: $", !{nameof(Push)}"
			)
		}{
			", ".RCoalesce(nameof(SubRules), ": [", string.Join(", ", SubRules).NullIfEmpty(), "]")
		}";
	}


	private class ReplacePart {
		public ReplacePart(string text, bool isGroup = false) {
			if (isGroup) {
				groupName = text;
			} else {
				this.text = text;
			}
		}
		public ReplacePart(int groupNumber) {
			this.groupNumber = groupNumber;
		}


		public readonly string? text;
		public int? groupNumber;
		public string? groupName;


		public override string ToString() {
			return $"{{{GetType().Name}: {"Text=".RCoalesce(text)}{"Group#=".RCoalesce(groupNumber.ToString().NullIfEmpty())}{"GroupName=".RCoalesce(groupName)}}}";
		}
	}


	// ReSharper disable UnusedMember.Local
	private enum JsonStyle {
		Expanded = 0,
		Compact = '!',
		InitialCompact = '^',
		CompactUnlessComplex = '%'
	}
	// ReSharper restore UnusedMember.Local


	private sealed partial class JsonReplacePart : ReplacePart {
		public readonly JsonStyle compactJson;

		public readonly Func<JContainer?, Match, JToken?> evaluate;


		public JsonReplacePart(string jsonFunc) : base(jsonFunc, isGroup: true) {
			var i = jsonFunc.IndexOf('(');
			var func = jsonFunc[5..i];
			groupName = jsonFunc[(i + 1)..^1];

			if ("!^%".Contains(func[^1])) {
				compactJson = (JsonStyle)func[^1];
				func = func.TrimEnd('!', '^', '%');
			}

			var allowMissing = func.EndsWith('?');
			switch (func) {
				case "read":
				case "read?":
					evaluate = (jObject, match) => ReadAndRemove(jObject, groupName, match, allowMissing);
					break;

				case "value":
					evaluate = (jObject, match) => Read(jObject, groupName, match, allowMissing);
					break;

				case "unread":
					groupName = "*";
					// ReSharper disable once ImplicitlyCapturedClosure - don't care.
					evaluate = (jObject, _) => (jObject?.Children().Any() == true)
						? jObject
						: "";

					break;

				default:
#pragma warning disable CA2208	// Instantiate argument exceptions correctly
					throw new ArgumentOutOfRangeException(nameof(func), func, $"Unhandled JSON func: `{func}`!");
#pragma warning restore CA2208
			}
		}


		private static JToken? ReadAndRemove(JToken? json, string jsonPath, Match match, bool allowMissing) {
			return JsonPath(json, ResolvePath(jsonPath, match), allowMissing || json is null, remove: true);
		}

		private static JToken? Read(JToken? json, string jsonPath, Match match, bool allowMissing) {
			return JsonPath(json, ResolvePath(jsonPath, match), allowMissing || json is null);
		}

		private static List<string> ResolvePath(string jsonPath, Match match) {
			if (jsonPath[0] == '$') {
				jsonPath = match.Groups[jsonPath[1..]].Value;
			}

			var result = new List<string>();
			var pathSpan = jsonPath.AsSpan();

			var dotIndex = 0;
			while (dotIndex < pathSpan.Length) {
				var nextDot = pathSpan[dotIndex..].IndexOf('.');
				var segmentSpan = (nextDot == -1)
					? pathSpan[dotIndex..]
					: pathSpan[dotIndex..(dotIndex + nextDot)];

				var bracketIndex = 0;
				while (bracketIndex < segmentSpan.Length) {
					var nextBracket = segmentSpan[bracketIndex..].IndexOf('[');
					if (nextBracket == -1) {
						if (bracketIndex < segmentSpan.Length) {
							result.Add(segmentSpan[bracketIndex..].ToString());
						}

						break;
					}


					if (nextBracket > 0) {
						result.Add(segmentSpan[bracketIndex..(bracketIndex + nextBracket)].ToString());
					}

					bracketIndex += nextBracket + 1;
					var nextBracketOrEnd = segmentSpan[bracketIndex..].IndexOf('[');
					var partSpan = (nextBracketOrEnd == -1)
						? segmentSpan[bracketIndex..]
						: segmentSpan[bracketIndex..(bracketIndex + nextBracketOrEnd)];

					if (partSpan.Length > 0 && partSpan[^1] == ']') {
						result.Add('[' + partSpan.ToString());
					} else if (partSpan.Length > 0) {
						result.Add(partSpan.ToString());
					}

					if (nextBracketOrEnd == -1) {
						break;
					}


					bracketIndex += nextBracketOrEnd;
				}

				if (nextDot == -1) {
					break;
				}


				dotIndex += nextDot + 1;
			}

			return result;
		}

		private static JToken? JsonPath(JToken? json, IReadOnlyList<string> path, bool allowMissing, bool remove = false) {
			JToken? result = json;
			for (var i = 0; i < path.Count; i++) {
				var s = path[i];
				result = result?.SelectToken(s)
					?? result?.SelectToken(QuoteDynamicObjectKeysRE().Replace(s, "['$1']"))
					?? result?.SelectToken(StripPrefixSuffixRE().Replace(s, "$1"));		// Finally, try minus a @ prefix or :format suffix.

				if (result == null) {
					return allowMissing
						? ""
						: $"MISSING:{s}";
				}


				if (remove && i == path.Count - 1) {
					result.Parent?.Remove();
				}
			}

			return result;
		}


		[GeneratedRegex(@"^\[([^'""].*)]$")]
		private static partial Regex QuoteDynamicObjectKeysRE();

		[GeneratedRegex("^@?([^:]+)(?::.+)?$")]
		private static partial Regex StripPrefixSuffixRE();
	}


	[GeneratedRegex("%([^%]+)%")]
	private static partial Regex VarReplaceRE();
}
