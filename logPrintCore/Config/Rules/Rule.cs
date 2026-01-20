#if DEBUG
//#define DEBUG_MATCHING
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
		nameof(SubRules)
	}.Contains(p.Name);

	private readonly Func<Type, PropertyInfo, bool> _recurseFilter = (_, p) => p.Name == nameof(SubRules);

#endif
	private string _replaceStr = null!;

	private JObject? _json;
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
			_replaceStr = value
				.Replace("\\a", "\a")
				.Replace("\\b", "\b")
				.Replace("\\e", "\e")
				.Replace("\\f", "\f")
				.Replace("\\n", "\n")
				.Replace("\\r", "\r")
				.Replace("\\t", "\t")
				.Replace("\\v", "\v")
				.Replace("\\0", "\0");
	}

	public bool Repeat { get; set; }

	public Rule[] Groups { get; [UsedImplicitly] set; } = [];
	// ReSharper restore MemberCanBePrivate.Global


	private Rule? Parent { get; set; }

	private List<Rule> SubRules =>
		Groups
			.Select(
				rule => {
					rule.Parent = this;
					return rule;
				}
			)
			.ToList();

	private List<ReplacePart> ProcessedReplace {
		get {
			if (field != null) {
				return field;
			}


			field = [];
			var i = 0;
			var j = Replace.IndexOf('$');
			while (j != -1) {
				if (i < j) {
					field.Add(new(Replace[i..j]));
				}

				if (Replace[j + 1] == '{') {
					i = j + 2;
					j = Replace.IndexOf('}', i);
					var substring = Replace[i..j];
					field.Add(
						(Parse != ParseType.None && substring.StartsWith("JSON.", StringComparison.Ordinal))
							? new JsonReplacePart(substring)
							: new ReplacePart(substring, isGroup: true)
					);

					i = j + 1;
				} else {
					j = i = j + 1;
					while (++j < Replace.Length && Replace[j] >= '0' && Replace[j] <= '9') { }

					field.Add(new(int.Parse(Replace[i..j])));
					i = j;
				}

				j = Replace.IndexOf('$', i);
			}

			if (i < Replace.Length) {
				field.Add(new(Replace[i..]));
			}

#if DEBUG_MATCHING
			field.Dump(multiLine: true);

#endif
			return field;
		}
	}


	public Rule ProcessVars(IDictionary<string, string?> vars) {
		_jsonLookup = new(SafeDictionary<string, string>.MissingKeyOperation.ReturnDefault);
		vars
			.Where(var => var.Key.StartsWith("JSON", StringComparison.Ordinal))
			.ToList()
			.ForEach(var => _jsonLookup.Add(var.Key[4..], var.Value));

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
					AnsiConsoleColourExtensions.PUSH_COLOURS
						.RCoalesce(Replace.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t").NullIfEmpty(), AnsiConsoleColourExtensions.POP_COLOURS)
					?? ""
				);
			}

			return line;
		}


		return (Test?.IsMatch(line) ?? true)
			? Match.Replace(
				line,
				AnsiConsoleColourExtensions.PUSH_COLOURS
					.RCoalesce(Replace.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t").NullIfEmpty(), AnsiConsoleColourExtensions.POP_COLOURS)
				?? ""
			)
			: line;
	}

	private string ProcessJson(Match match) {
		if (Parse == ParseType.Parent) {
			_json = Parent!._json;
			_originalJson = Parent._originalJson;
		} else {
			var jsonStr = match.Groups["JSON"].Value.NullIfEmpty() ?? match.Value;
			try {
				_json = (JObject?)JsonConvert.DeserializeObject(jsonStr, jsonSerializerSettings);
			} catch (JsonReaderException jsonReaderException) {
				var lines = jsonStr.Replace("\r", "").Split('\n');
				for (var line = 1; line <= jsonReaderException.LineNumber; line++) {
					Console.Error.WriteLineColours(
						$"~{
							((line == jsonReaderException.LineNumber)
								? 'Y'
								: 'w')
						}~#R#"
						+ lines[line - 1]
					);
				}

				Console.Error.WriteLineColours($"~C~#R#{new string('-', jsonReaderException.LinePosition - 1)}^");
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
		var result = new StringBuilder();
#if DEBUG_MATCHING
		match.Dump("match", true, (_, p) => new[] { "Success", "Groups", "Name", "Value" }.Contains(p.Name), (_, p) => p.Name == "Groups");
#endif
		foreach (var part in ProcessedReplace) {
#if DEBUG_MATCHING
			part.Dump("part ");
#endif
			if (part is JsonReplacePart jsonReplacePart) {
				result.Append(ProcessGroup(FormatJson(jsonReplacePart.Evaluate(_json, match)), part.GroupName!));
			} else if (part.GroupName != null) {
				result.Append(ProcessGroup(match.Groups[part.GroupName], part.GroupName));
			} else if (part.GroupNumber != null) {
				// ReSharper disable once ArgumentsStyleOther
				result.Append(ProcessGroup(match.Groups[part.GroupNumber.Value], name: part.GroupNumber.Value.ToString()));
			} else {
				result.Append(part.Text);
			}
		}

#if DEBUG_MATCHING
		result.Dump("result");
#endif
		return AnsiConsoleColourExtensions.PUSH_COLOURS
				.RCoalesce(result.ToString().NullIfEmpty(), AnsiConsoleColourExtensions.POP_COLOURS)
			?? "";
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
		return SubRules
			.Where(rule => rule.Name.Split('.').First() == name)
			.Aggregate(
				str,
				(value, subRule) => subRule
					.Process(value)
#if DEBUG_MATCHING
					.Dump("groupResult")!
#endif
			);
	}


	public (string? line, string marker, LogLevel level) Summarise((string? line, string marker, LogLevel level) result) {
		var level = (LogLevel)Enum.Parse(typeof(LogLevel), Name);
		return (Test?.IsMatch(result.line ?? "") ?? true)
			? Match.IsMatch(result.line ?? "")
				? (
					result.line,
					Match.Replace(
						((char)level).ToString(),
						AnsiConsoleColourExtensions.PUSH_COLOURS
							.RCoalesce(Replace.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t").NullIfEmpty(), AnsiConsoleColourExtensions.POP_COLOURS)
						?? ""
					),
					level
				)
				: result
			: result;
	}


	private string FormatJson(JToken? jToken, bool coloursSimpleValues = false, string indent = "") {
		if (jToken == null) {
			return "";
		}


		var newIndent = indent + (_jsonLookup["Indent"] ?? "\t");
		var sb = new StringBuilder();

		// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault - see default case trivia
		switch (jToken.Type) {
			case JTokenType.Object:
				sb.Append(AnsiConsoleColourExtensions.PUSH_COLOURS).Append(_jsonLookup["Brace"]).Append('{');

				var props = ((JObject)jToken).Properties().ToList();
				if (props.Any()) {
					sb.AppendLine();
					for (var i = 0; i < props.Count; i++) {
						JProperty jProperty = props[i];
						sb.Append(newIndent)
							.Append(_jsonLookup["Prop"])
							.Append(jProperty.Name)
							.Append(_jsonLookup["Colon"])
							.Append(": ")
							.Append(FormatJson(jProperty.Value, coloursSimpleValues: true, newIndent));

						if (i < props.Count - 1) {
							sb.Append(_jsonLookup["Comma"]).Append(',');
						}

						sb.AppendLine();
					}

					sb.Append(indent).Append(_jsonLookup["Brace"]);
				} else if (!coloursSimpleValues) {
					return "{}";
				}


				sb.Append('}').Append(AnsiConsoleColourExtensions.POP_COLOURS);
				break;

			case JTokenType.Array:
				sb.Append(AnsiConsoleColourExtensions.PUSH_COLOURS).Append(_jsonLookup["Bracket"]).Append('[');

				var arr = ((JArray)jToken).ToList();
				if (arr.Any()) {
					sb.AppendLine();
					for (var i = 0; i < arr.Count; i++) {
						sb.Append(newIndent)
							.Append(FormatJson(arr[i], coloursSimpleValues: true, newIndent));

						if (i == arr.Count) {
							sb.Append(_jsonLookup["Comma"]).Append(',');
						}

						sb.AppendLine();
					}

					sb.Append(indent).Append(_jsonLookup["Bracket"]);
				} else if (!coloursSimpleValues) {
					return "[]";
				}


				sb.Append(']').Append(AnsiConsoleColourExtensions.POP_COLOURS);
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


		return sb.ToString();


		void ConditionalFormat(string? format) {
			if (coloursSimpleValues) {
				sb.Append(AnsiConsoleColourExtensions.PUSH_COLOURS).Append(format);
			}

			sb.Append(jToken);

			if (coloursSimpleValues) {
				sb.Append(AnsiConsoleColourExtensions.POP_COLOURS);
			}
		}

		void AlwaysFormat(string? format) {
			sb.Append(AnsiConsoleColourExtensions.PUSH_COLOURS).Append(format).Append(jToken).Append(AnsiConsoleColourExtensions.POP_COLOURS);
		}
	}


	public override string ToString() {
		return $"{
			GetType().Name
		}: {
			Name
		}, {
			(Parse == ParseType.None
				? ""
				: $"{nameof(Parse)}: {Parse}, ")
		}{
			(Repeat
				? $"{nameof(Repeat)}, "
				: "")
		}{
			nameof(SubRules)
		}: [{
			string.Join(", ", SubRules)
		}]";
	}


	private class ReplacePart {
		public ReplacePart(string text, bool isGroup = false) {
			if (isGroup) {
				GroupName = text;
			} else {
				Text = text.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
			}
		}
		public ReplacePart(int groupNumber) {
			GroupNumber = groupNumber;
		}


		public string? Text { get; }
		public int? GroupNumber { get; }
		public string? GroupName { get; protected init; }


		public override string ToString() {
			return $"{{{GetType().Name}: {"Text=".RCoalesce(Text)}{"Group#=".RCoalesce(GroupNumber.ToString().NullIfEmpty())}{"GroupName=".RCoalesce(GroupName)}}}";
		}
	}


	private sealed partial class JsonReplacePart : ReplacePart {
		public Func<JObject?, Match, JToken?> Evaluate { get; }


		public JsonReplacePart(string jsonFunc) : base(jsonFunc, isGroup: true) {
			var i = jsonFunc.IndexOf('(');
			var func = jsonFunc[5..i];
			GroupName = jsonFunc[(i + 1)..^1];

			var allowMissing = func.EndsWith("?", StringComparison.Ordinal);
			switch (func) {
				case "read":
				case "read?":
					Evaluate = (jObject, match) => ReadAndRemove(jObject, GroupName, match, allowMissing);
					break;

				case "value":
					Evaluate = (jObject, match) => Read(jObject, GroupName, match, allowMissing);
					break;

				case "unread":
					GroupName = "*";
					// ReSharper disable once ImplicitlyCapturedClosure - don't care.
					Evaluate = (jObject, _) => (jObject?.Children().Any() == true)
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

			return jsonPath
				.Split('.')
				.Select(
					p => p.Split('[')
						.Select(q => q.EndsWith("]", StringComparison.Ordinal)
							? '[' + q
							: q
						)
				)
				.SelectMany(x => x)
				.ToList();
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
