#if DEBUG
//#define DEBUG_MATCHING
#endif

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;

using JetBrains.Annotations;

#if DEBUG_MATCHING
using logPrintCore.Ansi;
#endif
using logPrintCore.Utils;

namespace logPrintCore.Config.Rules;

internal sealed class RuleSet {
	private List<Rule>? _rules;


	[Required]
	public string Name { get; [UsedImplicitly] set; } = null!;

	public Regex? RecordStart { get; [UsedImplicitly] set; }

	public Regex? Reset { get; [UsedImplicitly] set; }

	private readonly SafeDictionary<string, string> _vars = new((key, _) => $"%MISSING: {key}%");
	public IDictionary<string, string?> Vars {
		get => _vars;
		[UsedImplicitly]
		set {
			_vars.Clear();
			_vars.AddRange(value!);
		}
	}

	// ReSharper disable once MemberCanBePrivate.Global
	[Required]
	public Rule[] Rules { get; [UsedImplicitly] set; } = null!;


	public List<Rule> RulesList =>
		_rules ??= Rules
			.Select(rule => rule.ProcessVars(Vars))
			.ToList();

	public bool DidHighlight { get; set; }


	public void SetHighlight(Regex grepRE, bool grepStrippedOutput) {
		_rules = RulesList;
		var highlightRule = new HighlightRule(this, grepRE, grepStrippedOutput);
		_rules.Add(highlightRule);
	}

	public string? Process(string? line) {
		DidHighlight = false;
#if DEBUG_MATCHING
		Console.Error.WriteLine(new string('=', AnsiConsoleColourExtensions.OutputWidth - 1));
		line.Dump("<<<<");
#endif
		RulesList.ForEach(rule => line = rule.Process(line));
#if DEBUG_MATCHING
		line.Dump(">>>>");
#endif
		return line;
	}

	public (LogLevel, string) Summarise(string? line) {
		var result = (line, marker: ((char)LogLevel.None).ToString(), level: LogLevel.None);

		RulesList
			.Where(rule => Enum.IsDefined(typeof(LogLevel), rule.Name))
			.ToList()
			.ForEach(rule => result = rule.Summarise(result));

		return (result.level, result.marker);
	}
}
