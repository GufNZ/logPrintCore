using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using logPrintCore.Utils;

namespace logPrintCore.Config.Flags;

internal sealed partial class TimeMarker(int? size, bool outputTimeSpan, TimeDeltaMode timingPerThread) : FlagSet {
	[GeneratedRegex(@"^(?:\d{4}(?:-\d\d){2} |(?:\d\d/){2}\d{4}\|)?(?:\d\d:){2}\d\d.\d{3,4}|\d\d-...-\d{4} (?:\d\d:){2}\d\d.\d{3}|\d{4}(?:-\d\d){2}T(?:\d\d:){2}\d\d\.\d+Z")]
	private static partial Regex TimeRE();

	[GeneratedRegex(@"\[ ?(?<tID>\d+)\](?= \[.\])|TID: 0*(?<tID>\d+)")]
	private static partial Regex ThreadIdRE();

	private static readonly Regex timeRE = TimeRE();
	private static readonly Regex threadIdRE = ThreadIdRE();
	private static readonly Dictionary<int, DateTime> lastPerThreadTimes = new();


	public static DateTime? GetTime(string line) {
		return timeRE.Match(line).Value.TryParseDateTimeExact(["yyyy-MM-ddTHH:mm:ss.fffffffZ", "yyyy-MM-dd HH:mm:ss.ffff", "MM/dd/yyyy|HH:mm:ss.fff", "HH:mm:ss.fff", "dd-MMM-yyyy HH:mm:ss.fff"]);
	}

	private static DateTime GetTime(string line, DateTime defaultValue) {
		return GetTime(line) ?? defaultValue;
	}


	private readonly int _size = size ?? 6;

	private DateTime _last = DateTime.MinValue;


	public TimeSpan? LastDelta { get; private set; }


	public override string Name =>
		$"#y#TimeMarker{
			(
				outputTimeSpan
					? "~M~<TimeSpan>"
					: $"~m~(size #Y#{_size}#y#)#!#"
			)
		}{
			timingPerThread switch {
				TimeDeltaMode.PerThread => " ~C~Per Thread",
				TimeDeltaMode.PerVisible => " ~Y~Per Visible",
				_ => ""
			}
		}";


	public override Flag[] Flags { get; protected set; } = [];


	public override string Process(string line) {
		var at = GetTime(line, DateTime.MinValue);

		Func<DateTime> getLast = () => _last;
		Action<DateTime> setLast = value => _last = value;
		switch (timingPerThread) {
			case TimeDeltaMode.PerThread: {
				var threadIDMatch = threadIdRE.Match(line);
				var threadID = int.Parse(threadIDMatch.Groups["tID"].Value.NullIfEmpty() ?? "-1");

				getLast = () => lastPerThreadTimes.TryGetValue(threadID, out var time)
					? time
					: DateTime.MinValue;

				setLast = value => lastPerThreadTimes[threadID] = value;
				break;
			}

			case TimeDeltaMode.PerVisible:
				getLast = () => Program.lastPrintedTime ?? DateTime.MinValue;
				setLast = _ => { };
				break;

			case TimeDeltaMode.PerAll:
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(timingPerThread), timingPerThread, $"Unhandled {nameof(TimeDeltaMode)} value: {timingPerThread}");
		}


		var last = getLast();
		if (at == DateTime.MinValue || last == DateTime.MinValue) {
			setLast(at);
			return new string(' ', _size) + SEPARATOR;
		}


		string prefix;
		char markOn;
		var markOff = ' ';
		int onSize;

		TimeSpan delta;
		Func<string, int, char, string> pad;

		LastDelta = at - last;
		if (at >= last) {
			delta = LastDelta.Value;
			pad = (str, size, ch) => str.PadRight(size, ch);
		} else {
			delta = -LastDelta.Value;
			pad = (str, size, ch) => str.PadLeft(size, ch);
			markOff = '-';
		}

		setLast(at);

		if (delta.Ticks == 0) {
			return outputTimeSpan
				? string.Concat("~g~#K# ", delta.ToString("G"), SEPARATOR)
				: string.Concat("~g~#K#", "0".PadLeft(_size / 2).PadRight(_size), SEPARATOR);
		}


		if (delta.TotalMilliseconds <= 1000.0) {
			prefix = "~w~#K#";
			markOn = 'm';
			onSize = (int)Math.Ceiling(_size * delta.TotalMilliseconds / 1000.0);
		} else if (delta.TotalSeconds < 60.0) {
			prefix = "~Y~#y#";
			markOn = 's';
			onSize = (int)Math.Ceiling(_size * delta.TotalSeconds / 60.0);
		} else if (delta.TotalMinutes < 60.0) {
			prefix = "~M~#m#";
			markOn = 'M';
			onSize = (int)Math.Ceiling(_size * delta.TotalMinutes / 60.0);
		} else if (delta.TotalHours < 24.0) {
			prefix = "~R~#r#";
			markOn = 'H';
			onSize = (int)Math.Ceiling(_size * delta.TotalHours / 24.0);
		} else {
			return outputTimeSpan
				? string.Concat("#R#~Y~", markOff.ToString().Replace(" ", "+"), delta.ToString("G"), SEPARATOR)
				: string.Concat("#R#~Y~", delta.TotalDays, "days!").PadRight(_size, '!') + SEPARATOR;
		}


		return outputTimeSpan
			? string.Concat(prefix, markOff.ToString().Replace(" ", "+"), delta.ToString("G"), SEPARATOR)
			: string.Concat(prefix, pad(new(markOn, onSize), _size, markOff), SEPARATOR);
	}

	public override string Reset(string line) {
		base.Reset(line);

		lastPerThreadTimes.Clear();
		return Process(line);
	}


	public override string ToString() {
		return $"{{{GetType().Name} size={_size}}}";
	}
}
