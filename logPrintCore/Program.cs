#if DEBUG
//#define DEBUG_CONFIG_PARSE
//#define DEBUG_MATCHING
//#define DEBUG_INPUT
//#define DEBUG_END
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using logPrintCore.Ansi;
using logPrintCore.Config;
using logPrintCore.Config.Flags;
using logPrintCore.Config.Rules;
using logPrintCore.Utils;

using Newtonsoft.Json;

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NodeDeserializers;

[assembly: InternalsVisibleTo("logPrintCoreTests")]

namespace logPrintCore;

internal static partial class Program {
	private const string DATE_TIME_FORMAT = "yyyy-MM-dd HH:mm:ss.ffff: ";


	private static ConfigRoot config = null!;

	internal static readonly List<FlagSet> flagSets = [];

	private static readonly TimeSpan progressThrottle = TimeSpan.FromSeconds(0.5);
	private static readonly char[] lineBreakChars = Environment.NewLine.ToCharArray();

	private static readonly Dictionary<char, TimeSpan> timeScale = new() {
		{ 'S', TimeSpan.FromSeconds(1) },
		{ 'M', TimeSpan.FromMinutes(1) },
		{ 'H', TimeSpan.FromHours(1) }
	};

	private static readonly List<(LogLevel level, string marker)> bucket = [];


	private static readonly char pathSeparator = OperatingSystem.IsWindows()
		? ';'
		: ':';

	private static readonly string[] executableExtensions = OperatingSystem.IsWindows()		//QUESTION: what else do I care about here?
		? ["exe", "com"]
		: ["sh", "pl", "awk"];


	#region Args-related fields

	internal static string compileTemp = Environment.GetEnvironmentVariable("TEMP") ?? ".";
	internal static bool forceCompile;

	private static string? ruleSetName;
	private static RuleSet? ruleSet;
	private static RuleSet? timeOutputRuleSet;

	private static string? fileName;
	private static bool follow;
	private static bool followDir;
	private static bool breakForNextFile;
	private static FileSystemWatcher? watcher;

	private static TimeSpan? startTime;
	private static TimeSpan? endTime;

	private static DateTime? start;
	private static DateTime? end;

	private static StepMode stepMode;
	private static uint? stepCount;
	private static uint stepProgress;
	private static TimeSpan stepTime;
	private static DateTime? stepStartTime;

	private static bool summariseMode;
	private static StringBuilder? summarised;

	private static bool firstLine;
	private static bool argsError;
	private static bool verifyArgs;
	private static bool shownSkip;

	private static DateTime? lastTime;

	private static bool flagQuery;
	private static bool queriedFlagStateChanged;

	private static bool exceptionQuery;

	private static TimeSpan? timeQuery;
	private static TimeDeltaMode timingMode;

	private static bool grepRawLine = true;
	private static bool grepStrippedOutput;
	private static string? grep;
	private static bool grepGrouped;
	private static Regex? grepRE;
	private static string? invGrep;
	private static bool invGrepGrouped;
	private static Regex? invGrepRE;
	private static string? exclGrep;
	private static bool exclGrepGrouped;
	private static Regex? exclGrepRE;

	private static Process? wrappedProcess;
	private static ProcessReader? processReader;

	#endregion

	private static DateTime? progressDrawn;

	internal static DateTime? lastPrintedTime;


	private static void WriteException(Exception exception, TextWriter @out) {
		@out.Write(exception);
		//NOTE: YamlDotNet exceptions override ToString, hiding any InnerException details!.
		if (exception is YamlException { InnerException: not null } yx) {
			@out.Write($"{Environment.NewLine}-> {yx.InnerException}");
		}

		@out.WriteLine();
	}

	private static void Main(string[] args) {
		Console.OutputEncoding = Encoding.UTF8;
		var exitCode = 0;
		try {
			Run(args);
		} catch (Exception exception) {
			Console.ForegroundColor = ConsoleColor.White;
			Console.BackgroundColor = ConsoleColor.Red;
			WriteException(exception, Console.Out);
#if DEBUG
			if (Console.IsOutputRedirected) {
				Console.Error.WriteLine(new string('-', 128));
				WriteException(exception, Console.Error);
			}
#endif
			Console.ResetColor();
			exitCode = 1;
		}

#if DEBUG_END
		if (System.Diagnostics.Debugger.IsAttached)
		{
			Console.WriteLine("---ProcessEND---");
			System.Diagnostics.Debugger.Break();
		}

#endif
		Environment.Exit(exitCode);
	}


	private static void Run(string[] args) {
		if (args.Contains("-D")) {
			verifyArgs = true;
			args = args.Where(arg => arg != "-D").ToArray();
#if DEBUG
			if (Debugger.IsAttached) {
				Debugger.Break();
			} else {
				Debugger.Launch();
			}
#endif
		}

		LoadConfig();

		if (!ProcessArgs(args)) {
			return;
		}


		ruleSet = GetRuleSet();

		if (summariseMode) {
			var tmp = ruleSetName;
			ruleSetName = null;
			timeOutputRuleSet = GetRuleSet();
			ruleSetName = tmp;
		}

		if (grepRE != null) {
			ruleSet.SetHighlight(grepRE, grepStrippedOutput);		//NOTE: doesn't support grepRawLine mode as we can't translate the match through the formatting.
		}

		if (followDir) {
			// ReSharper disable once AssignNullToNotNullAttribute - too bad if this returns null.
			watcher = new(Path.GetDirectoryName(Path.GetFullPath(fileName!))!, "*" + Path.GetExtension(fileName)) { EnableRaisingEvents = true };
			watcher.Created += HandleNewFile;
			watcher.Error += (sender, eventArgs) => {
				Console.Error.WriteLineColours($"#R#~W~FileSystemWatcher Error: {sender}; {eventArgs}");
				eventArgs.Dump(multiLine: true);
			};
		}

		if (summariseMode) {
			summarised = new();
		}

		do {
			breakForNextFile = false;

			using (var reader = GetLineSource()) {
				var timeout = TimeSpan.FromSeconds(1);
				firstLine = true;

				if (ruleSet.RecordStart == null) {
					ProcessLines(reader, timeout);
				} else {
					ProcessRecords(reader, timeout);
				}
			}

			if (summarised?.Length > 0) {
				AppendAndClearBucket();
				// ReSharper disable once PossibleInvalidOperationException
				Console.Out.WriteLineColours(timeOutputRuleSet!.Process(stepStartTime!.Value.ToString(DATE_TIME_FORMAT)) + summarised);
				summarised.Clear();
			}

			if (!shownSkip) {
				continue;
			}


			Console.Out.WriteLineColours($"~K~------------------------------------~c~[~C~{lastTime:yyyy-MM-dd HH:mm:ss.ffff}~c~]~K~----------------------------------------------");
			shownSkip = false;
			progressDrawn = null;
		} while (followDir);

		if (progressDrawn.HasValue) {
			Console.Out.ClearLine();
		}
	}

	private static void LoadConfig() {
		var deserializer = new DeserializerBuilder()
			.WithTypeConverter(
				new RegexFromStringConverter(
#if DEBUG_CONFIG_PARSE
					debug: true
#endif
				)
			)
			.WithNodeDeserializer(
				innerNodeDeserializer => new ValidatingNodeDeserializer(
					innerNodeDeserializer
#if DEBUG_CONFIG_PARSE
					, debug: true
#endif
				),
				selectionSyntax => selectionSyntax.InsteadOf<ObjectNodeDeserializer>()
			)
			.Build();

		// ReSharper disable once AssignNullToNotNullAttribute
		config = deserializer.Deserialize<ConfigRoot>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "appSettings.yaml")));
	}

	private static bool ProcessArgs(string[] args) {
		AnsiConsoleColourExtensions.OutputMode = ConsoleColourOutputMode.ConsoleColor;

		args = ProcessArgsRepeats();

		for (var i = 0; i < args.Length; i++) {
			var arg = args[i];
			var argValue = arg[1..];
			switch (arg[0]) {
				case '-':
					if (!ProcessDashArg(arg, argValue, ref i)) {
						return false;
					}


					break;

				case '@':
					ProcessAtArg(argValue);

					break;

				case '~':
					if (endTime.HasValue) {
						Console.Error.WriteLineColours($"#R#~W~Multiple endTimes supplied!  Dropping `~Y~{endTime}~W~`!");
						argsError = true;
					}

					endTime = argValue.TryParseTimeSpan();
					if (endTime == null) {
						Console.Error.WriteLineColours($"#R#~W~Can't parse end time `~Y~{argValue}~W~`!");
						argsError = true;
					}

					break;

				case '+':
					if (startTime.HasValue) {
						var length = argValue.TryParseTimeSpan();
						if (length.HasValue) {
							if (endTime.HasValue) {
								Console.Error.WriteLineColours($"#R#~W~Multiple endTimes supplied!  Dropping `~Y~{endTime}~W~`!");
								argsError = true;
							}

							endTime = startTime + length;
						} else {
							Console.Error.WriteLineColours($"#R#~W~Can't parse length `~Y~{argValue}~W~`!");
							argsError = true;
						}
					} else {
						Console.Error.WriteLineColours($"#R#~Y~Length without start time `~W~{argValue}~Y~`!");
						argsError = true;
					}

					break;

				default:
					if (fileName != null) {
						Console.Error.WriteLineColours($"#R#~W~Can only have 1 file - dropping '~Y~{fileName}~W~'!");
						argsError = true;
					}

					fileName = arg;
					break;
			}
		}


		FinalArgsValidation();

		return !argsError || PrintArgsError();


		// ReSharper disable once ImplicitlyCapturedClosure - don't care.
		string[] ProcessArgsRepeats() {
			for (var i = 0; i < args.Length - 2; i++) {
				var match = RepeatMultipleRE().Match(args[i]);
				if (!match.Success) {
					continue;
				}


				var repeat = new[] { args[i + 1], args[i + 2] };
				var argsList = args
					.Take(i - 1)
					.ToList();

				var count = int.Parse(match.Groups[1].Value);
				for (var j = 0; j < count; j++) {
					argsList.AddRange(repeat);
				}

				args = argsList
					.Concat(args.Skip(i + 3))
					.ToArray();
			}

			return args;
		}

		bool ProcessDashArg(string arg, string argValue, ref int i) {
			if (arg.Length == 1) {
				if (fileName != null) {
					Console.Error.WriteLineColours($"#R#~W~Can only have 1 file - dropping '~Y~{fileName}~W~'");
					argsError = true;
				}

				fileName = arg;
				return true;
			}


			switch (arg[1]) {
				case '-':
					switch (argValue) {
						case "-help":
							Console.Out.WriteLineColours(config.Docs.Usage);
							return false;

						case "-config":
							Console.Out.WriteLineColours(config.Docs.Definition);
							return false;

						case "-list":
							ListConfig();
							return false;

						default:
							Console.Error.WriteLineColours($"#R#~W~Unknown option '~Y~{arg}~W~'");
							argsError = true;
							return true;
					}


				case '?':
				case 'h':
					Console.Out.WriteLineColours(config.Docs.Usage);
					return false;


				case 'A':
				case 'a':
					AnsiConsoleColourExtensions.OutputMode = ConsoleColourOutputMode.Ansi;
					break;

				case 'e':
					exceptionQuery = true;
					break;

				case 'F':
					follow = true;
					followDir = (argValue == "FD");
					break;

				case 'f':
					if (i < args.Length - 1) {
						var isFlagQuery = (argValue == "fq");
						flagQuery |= isFlagQuery;

						var flagSetName = args[++i];
						if (flagSetName == "*") {
							flagSets.AddRange(GetAllFlagSets(isFlagQuery));
						} else {
							var flagSet = GetFlagSet(flagSetName, isFlagQuery);
							if (flagSet != null) {
								flagSets.Add(flagSet);
							}
						}
					} else {
						Console.Error.WriteLineColours($"#R#~W~Option -{argValue} requires an argument!");
						argsError = true;
					}

					break;

				case 'g':
					switch (argValue) {
						case "gA":
							grepRawLine = false;
							grepStrippedOutput = true;
							break;

						case "ga":
							grepRawLine = false;
							break;

						case "gv" when i < args.Length - 1: {
							var grepValue = args[++i];
							if (invGrep == null) {
								invGrep = grepValue;
							} else {
								if (invGrepGrouped) {
									invGrep += $"|(?:{grepValue})";
								} else {
									invGrep = $"(?:{invGrep})|(?:{grepValue})";
									invGrepGrouped = true;
								}
							}

							break;
						}

						case "gv":
							Console.Error.WriteLineColours("#R#~W~Option -gv requires an argument!");
							argsError = true;
							break;

						case "gV" when i < args.Length - 1: {
							var grepValue = args[++i];
							if (exclGrep == null) {
								exclGrep = grepValue;
							} else {
								if (exclGrepGrouped) {
									exclGrep += $"|(?:{grepValue})";
								} else {
									exclGrep = $"(?:{exclGrep})|(?:{grepValue})";
									exclGrepGrouped = true;
								}
							}

							break;
						}

						case "gV":
							Console.Error.WriteLineColours("#R#~W~Option -gV requires an argument!");
							argsError = true;
							break;

						default: {
							if (i < args.Length - 1) {
								var grepValue = args[++i];
								if (grep == null) {
									grep = grepValue;
								} else {
									if (grepGrouped) {
										grep += $"|(?:{grepValue})";
									} else {
										grep = $"(?:{grep})|(?:{grepValue})";
										grepGrouped = true;
									}
								}
							} else {
								Console.Error.WriteLineColours("#R#~W~Option -g requires an argument!");
								argsError = true;
							}

							break;
						}
					}

					break;

				case 'l':
					ListConfig();
					return false;

				case 'o':
					forceCompile = true;
					break;

				case 'O':
					if (i < args.Length - 1) {
						var test = args[++i].TrimToNull();
						if (test is null || !Directory.Exists(test)) {
							Console.Error.WriteLineColours($"#R#~W~Option -O ({test.EscapeColourCodeChars()}) doesn't point to an existing directory!");
							argsError = true;
						} else {
							compileTemp = test;
						}
					} else {
						Console.Error.WriteLineColours("#R#~W~Option -O requires an argument!");
						argsError = true;
					}

					break;

				case 't':
					switch (argValue) {
						case "ta":
							timingMode = TimeDeltaMode.PerAll;
							break;

						case "tt":
							timingMode = TimeDeltaMode.PerThread;
							break;

						case "tv":
							timingMode = TimeDeltaMode.PerVisible;
							break;

						default:
							// ReSharper disable once PossibleNullReferenceException
							if (argValue.StartsWith("tq", StringComparison.Ordinal)) {
								var nextArg = argValue[2..].NullIfEmpty()
									?? (
										(i < args.Length - 1)
											? args[++i]
											: null
									);

								if (nextArg == null) {
									Console.Error.WriteLineColours("#R#~W~Option -tq requires a value!");
									argsError = true;
									return true;
								}


								var time = (nextArg.Length == 1 && !char.IsDigit(nextArg[0]))
									? timeScale[nextArg.ToUpperInvariant()[0]]
									: nextArg.TryParseTimeSpan();

								if (time.HasValue) {
									timeQuery = time;
								} else {
									Console.Error.WriteLineColours($"#R#~W~Unable to parse value of -tq option: `~Y~{nextArg}~W~`!");
									argsError = true;
								}

								if (!flagSets.Any(fs => fs is TimeMarker)) {
									flagSets.Add(GetFlagSet(arg, isFlagQuery: false)!);
								}
							} else {
								flagSets.Add(GetFlagSet(arg, isFlagQuery: false)!);
							}


							break;
					}

					break;

				case 'T':
					flagSets.Add(GetFlagSet(arg, isFlagQuery: false)!);
					break;

				case 'r':
					if (i < args.Length - 1) {
						if (ruleSetName != null) {
							Console.Error.WriteLineColours($"#R#~W~Can only have 1 RuleSet - dropping '~Y~{ruleSetName}~W~'");
							argsError = true;
						}

						ruleSetName = args[++i];
					} else {
						Console.Error.WriteLineColours("#R#~W~Option -r requires an argument!");
						argsError = true;
					}

					break;

				case 'S':
					if (i < args.Length - 1) {
						summariseMode = true;
						var stepArgParts = args[++i].Split('/');
						if (stepArgParts.Length > 1) {
							stepCount = stepArgParts[1].TryParseUInt();
							if (!stepCount.HasValue) {
								Console.Error.WriteLineColours("#R#~W~Invalid scale-factor value for the -S option: ~Y~" + stepArgParts[1]);
								argsError = true;
								summariseMode = false;
							}
						}

						var stepTimeVal = stepArgParts[0].TryParseTimeSpan();
						if (stepTimeVal.HasValue) {
							stepTime = stepTimeVal.Value;
						} else {
							Console.Error.WriteLineColours("#R#~W~Invalid time-span value for the -S option: ~Y~" + stepArgParts[1]);
							argsError = true;
							summariseMode = false;
						}
					} else {
						Console.Error.WriteLineColours("#R#~W~Option -S requires an argument!");
						argsError = true;
					}

					break;

				case 's':
					if (i < args.Length - 1) {
						var stepArg = args[++i];
						var normalisedStepArg = stepArg.ToUpperInvariant();
						if (normalisedStepArg.EndsWith("L", StringComparison.Ordinal)) {
							stepMode = StepMode.LineCount;
							stepCount = normalisedStepArg.TrimEnd('L').TryParseUInt();

							if (!stepCount.HasValue) {
								Console.Error.WriteLineColours("#R#~W~Invalid line-count value for the -s option: ~Y~" + stepArg);
								argsError = true;
							}
						} else if (stepArg.Contains(":")) {
							stepMode = StepMode.TimeSpan;
							var stepTimeVal = stepArg.TryParseTimeSpan();
							if (stepTimeVal.HasValue) {
								stepTime = stepTimeVal.Value;
							} else {
								Console.Error.WriteLineColours("#R#~W~Invalid time-span value for the -s option: ~Y~" + stepArg);
								argsError = true;
							}
						} else {
							stepMode = StepMode.RecordCount;
							stepCount = stepArg.TryParseUInt();

							if (!stepCount.HasValue) {
								Console.Error.WriteLineColours("#R#~W~Invalid record-count value for the -s option: ~Y~" + stepArg);
								argsError = true;
							}
						}
					} else {
						Console.Error.WriteLineColours("#R#~W~Option -s requires an argument!");
						argsError = true;
					}

					break;

				case 'X':
					if (i < args.Length - 1) {
						var redirectStandardError = (argValue.ToUpperInvariant() == "XE");
						var redirectStandardOutput = (argValue != "XE");

						var processStartInfo = new ProcessStartInfo(
							FindInPath(args[++i]),
							$"\"{string.Join("\" \"", args[++i..])}\""	// Yuck!  But this API doesn't let me pass the args as a list..!?!
						) {
							RedirectStandardError = redirectStandardError,
							RedirectStandardOutput = redirectStandardOutput
						};

						if (redirectStandardError) {
							processStartInfo.StandardErrorEncoding = Encoding.UTF8;
						}

						if (redirectStandardOutput) {
							processStartInfo.StandardOutputEncoding = Encoding.UTF8;
						}

						wrappedProcess = new() { StartInfo = processStartInfo };

						if (!wrappedProcess.Start()) {
							Console.Error.WriteLineColours("#R#~W~Process failed to start!  ExitCode=" + wrappedProcess.ExitCode);
							argsError = true;
						} else {
							Console.Out.WriteLineColours($"#K#~M~>>>~Y~{wrappedProcess.StartInfo.FileName} ~G~{wrappedProcess.StartInfo.Arguments} ~M~<<<");
							Console.Out.WriteLineColours($"#W#~M~>>>~R~ Child Process ID: ~B~{wrappedProcess.Id} ~M~<<<");
							if (Debugger.IsAttached) {
								Console.WriteLine("Press Enter after attaching to this process if required.");
								Console.ReadLine();
							}

							static void PropagateKill() {
								// ReSharper disable once InvertIf
								if (wrappedProcess?.HasExited == false) {
									wrappedProcess.Kill(entireProcessTree: true);
									wrappedProcess = null;
								}
							}

							static void AssemblyUnloadEventHandler(AssemblyLoadContext _) {
								PropagateKill();
							}

							static void ConsoleCancelEventHandler(object? _, ConsoleCancelEventArgs __) {
								PropagateKill();
							}

							AssemblyLoadContext.Default.Unloading += AssemblyUnloadEventHandler;
							Console.CancelKeyPress += ConsoleCancelEventHandler;
							wrappedProcess.Exited += (_, _) => {
								AssemblyLoadContext.Default.Unloading -= AssemblyUnloadEventHandler;
								Console.CancelKeyPress -= ConsoleCancelEventHandler;
								breakForNextFile = true;
							};

							Process.GetCurrentProcess().Exited += (_, _)
								=> wrappedProcess?.Kill(entireProcessTree: true);	// Not sure if this'll get to run if the exit is from a StopDebugging when logPrint is the primary debugged process.
						}

						processReader = new(wrappedProcess, redirectStandardError, redirectStandardOutput);
						i = args.Length;
					} else {
						Console.Error.WriteLineColours("#R#~W~Option -X requires an argument!");
						argsError = true;
					}

					break;

				default:
					Console.Error.WriteLineColours($"#R#~W~Unknown option '~Y~{arg}~W~'");
					argsError = true;
					break;
			}

			return true;
		}

		static void ProcessAtArg(string argValue) {
			var parts = argValue.Split('~');
			if (parts.Length > 1) {
				if (startTime.HasValue) {
					Console.Error.WriteLineColours($"#R#~W~Multiple startTimes supplied!  Dropping `~Y~{startTime}~W~`!");
					argsError = true;
				}

				startTime = parts[0].TryParseTimeSpan();
				if (startTime.HasValue) {
					if (endTime.HasValue) {
						Console.Error.WriteLineColours($"#R#~W~Multiple endTimes supplied!  Dropping `~Y~{endTime}~W~`!");
						argsError = true;
					}

					endTime = parts[1].TryParseTimeSpan();
					if (endTime != null) {
						return;
					}


					Console.Error.WriteLineColours($"#R#~W~Can't parse end time `~Y~{parts[1]}~W~`!");
					argsError = true;
				} else {
					Console.Error.WriteLineColours($"#R#~W~Can't parse start time `~Y~{parts[0]}~W~`!");
					argsError = true;
				}
			} else {
				parts = argValue.Split('+');
				if (parts.Length > 1) {
					if (startTime.HasValue) {
						Console.Error.WriteLineColours($"#R#~W~Multiple startTimes supplied!  Dropping `~Y~{startTime}~W~`!");
						argsError = true;
					}

					startTime = parts[0].TryParseTimeSpan();
					if (startTime.HasValue) {
						var length = parts[1].TryParseTimeSpan();
						if (length.HasValue) {
							if (endTime.HasValue) {
								Console.Error.WriteLineColours($"#R#~W~Multiple endTimes supplied!  Dropping `~Y~{endTime}~W~`!");
								argsError = true;
							}

							endTime = startTime + length;
						} else {
							Console.Error.WriteLineColours($"#R#~W~Can't parse length `~Y~{parts[1]}~W~`!");
							argsError = true;
						}
					} else {
						Console.Error.WriteLineColours($"#R#~W~Can't parse start time `~Y~{parts[0]}~W~`!");
						argsError = true;
					}
				} else {
					if (startTime.HasValue) {
						Console.Error.WriteLineColours($"#R#~W~Multiple startTimes supplied!  Dropping `~Y~{startTime}~W~`!");
						argsError = true;
					}

					startTime = argValue.TryParseTimeSpan();
					if (startTime != null) {
						return;
					}


					Console.Error.WriteLineColours($"#R#~W~Can't parse start time `~Y~{argValue}~W~`!");
					argsError = true;
				}
			}
		}
	}

	private static string FindInPath(string exePath) {
		if (exePath.Contains('/') || exePath.Contains('\\') || exePath.Contains(':')) {
			return exePath;
		}


		var extension = Path.GetExtension(exePath);
		var extensions = (extension == "")
			? executableExtensions
			: [];


		var path = Environment.GetEnvironmentVariable("PATH")
			?.Split(pathSeparator);

		// ReSharper disable once InvertIf
		if (path?.Length > 0) {
			// ReSharper disable once LoopCanBePartlyConvertedToQuery - need to leave testPath mutable.
			foreach (var folder in path) {
				var testPath = Path.Combine(folder, exePath);
				if (File.Exists(testPath)) {
					return testPath;
				}


				foreach (var ext in extensions) {
					testPath = Path.ChangeExtension(testPath, ext);
					if (File.Exists(testPath)) {
						return testPath;
					}
				}
			}
		}


		return exePath;
	}

	private static void FinalArgsValidation() {
		if (!(grepRawLine || grepStrippedOutput)) {
			grep = ExpandColourMatches(grep);
			invGrep = ExpandColourMatches(invGrep);
			exclGrep = ExpandColourMatches(exclGrep);
		}

		if (grep != null) {
			grepRE = new(grep);
		}

		if (invGrep != null) {
			invGrepRE = new(invGrep);
		}

		if (exclGrep != null) {
			exclGrepRE = new(exclGrep);
		}

		if (follow && (fileName ?? "-") == "-") {
			Console.Error.WriteLineColours(
				$"#R#~W~Follow{
					(followDir
						? "Directory"
						: "")
				} without filename doesn't make sense!"
			);
			argsError = true;
		}

		if (fileName?.StartsWith("/cygdrive") ?? false) {
			fileName = Regex.Replace(fileName, "^/cygdrive/(.)", "$1:");
		}

		if (fileName?.StartsWith('/') ?? false) {
			fileName = Regex.Replace(fileName, "^/", "/cygwin64/");
		}

		return;


		static string? ExpandColourMatches(string? input) {
			return input
				?.Replace($"{AnsiConsoleColourExtensions.FOREGROUND}{AnsiConsoleColourExtensions.FOREGROUND}", AnsiConsoleColourExtensions.MATCH_FOREGROUND)
				.Replace($"{AnsiConsoleColourExtensions.BACKGROUND}{AnsiConsoleColourExtensions.BACKGROUND}", AnsiConsoleColourExtensions.MATCH_BACKGROUND)
				.Replace($"{AnsiConsoleColourExtensions.FOREGROUND}{AnsiConsoleColourExtensions.BACKGROUND}", AnsiConsoleColourExtensions.MATCH_ANY);
		}
	}

	private static bool PrintArgsError() {
		if (wrappedProcess?.HasExited == false) {
			wrappedProcess.Kill();
			wrappedProcess = null;
		}

		// ReSharper disable once StringLiteralTypo
		Console.Error.WriteColours("~M~Errors parsing args.  Continue anyway? [~Y~y~y~es~M~/~Y~N~g~o~M~/~Y~l~y~ist~M~/~Y~u~y~sage~M~/~Y~c~y~onfig~M~]: ");
		Console.Error.Flush();

		var ch = ((char)Console.Read()).ToString().ToUpperInvariant()[0];
		Console.Error.ClearLine();
		switch (ch) {
			case 'U':
				Console.Out.WriteLineColours(config.Docs.Usage);
				break;

			case 'C':
				Console.Out.WriteLineColours(config.Docs.Definition);
				break;

			case 'L':
				ListConfig();
				break;
		}

		return (ch == 'Y');
	}

	private static void ListConfig() {
		flagQuery = exceptionQuery = false;
		grepRE = invGrepRE = exclGrepRE = null;
		start = end = null;
		timeQuery = null;
		flagSets.Clear();
		PrintHeader(recordKind: null, isList: true);
	}


	private static IEnumerable<FlagSet> GetAllFlagSets(bool isFlagQuery) {
		return config.FlagSets
			.Select(
				flagSet => {
					flagSet.SetSubMatch("", [], isFlagQuery, OnQueryFlagStateChange);
					return flagSet;
				}
			);
	}

	private static FlagSet? GetFlagSet(string flagSetName, bool isFlagQuery) {
		if (flagSetName.ToUpperInvariant().StartsWith("-T", StringComparison.Ordinal)) {
			return new TimeMarker(flagSetName[2..].TryParseInt(), flagSetName[1] == 'T', timingMode);
		}


		var parts = flagSetName.Split('=');
		flagSetName = parts[0];
		var flagSetTrackID = (parts.Length > 1)
			? parts[1]
			: null;

		parts = flagSetName.Split(':');
		flagSetName = parts[0];
		var selectedDefines = (parts.Length > 1)
			? parts[1].Split(',').ToList()
			: [];

		parts = flagSetName.Split('/');
		flagSetName = parts[0];
		var flagName = (parts.Length > 1)
			? parts[1]
			: "";

		var selectedFlagSets = config.FlagSets
			.Where(set => set.Name.StartsWith(flagSetName, StringComparison.OrdinalIgnoreCase))
			.ToList();


#if DEBUG_MATCHING
		flagSets.Dump("flagSets", true, (_, p) => p.DeclaringType == typeof(FlagSet), (_, p) => p.PropertyType.IsGenericType);
#endif

		FlagSet flagSet;
		switch (selectedFlagSets.Count) {
			case 0:
				Console.Error.WriteLineColours($"#R#~W~Unknown FlagSet name: '~Y~{flagSetName}~W~'!");
				argsError = true;
				return null;

			case 1:
				flagSet = selectedFlagSets.First().Copy(flagSetTrackID);
				flagSet.SetSubMatch(flagName, selectedDefines, isFlagQuery, OnQueryFlagStateChange);
				return flagSet;
		}


		Console.Error.WriteLineColours($"#R#~W~Ambiguous FlagSet name '~Y~{flagSetName}~W~' matches '{string.Join("', '", selectedFlagSets.Select(fs => fs.Name))}'; using the first match!");
		argsError = true;

		flagSet = selectedFlagSets.First().Copy(flagSetTrackID);
		flagSet.SetSubMatch(flagName, selectedDefines, isFlagQuery, OnQueryFlagStateChange);
		return flagSet;
	}

	private static void OnQueryFlagStateChange(Flag? flag, FlagState newFlagState) {
		queriedFlagStateChanged = true;
	}

	private static RuleSet GetRuleSet() {
		var result = (ruleSetName.TrimToNull() == null)
			? config.RuleSets.First()
			: config.RuleSets.FirstOrDefault(set => set.Name.StartsWith(ruleSetName!, StringComparison.OrdinalIgnoreCase)) ?? config.RuleSets.First();

#if DEBUG_MATCHING
		ruleSet.Dump("ruleSet", true, (_, p) => p.DeclaringType == typeof(RuleSet), (_, p) => p.PropertyType.IsGenericType);
#endif

		if (ruleSetName.TrimToNull() == null || result.Name.StartsWith(ruleSetName!, StringComparison.OrdinalIgnoreCase)) {
			return result;
		}


		Console.Error.WriteLineColours($"#R#~W~Unknown RuleSet name: '~Y~{ruleSetName}~W~'!");
		argsError = true;

		return result;
	}


	private static void HandleNewFile(object sender, FileSystemEventArgs fileSystemEventArgs) {
		Console.Error.WriteLineColours($"#g#~Y~fileName ~W~:= ~C~{fileSystemEventArgs.FullPath}");
		fileName = fileSystemEventArgs.FullPath;
		breakForNextFile = true;
	}


	private static ILineReader GetLineSource() {
		return processReader
			?? (ILineReader)(
				fileName is null or "-"
					? new ConsoleReader()
					: new FileReader(fileName, follow)
			);
	}


	#region Funky

	private readonly struct Colour(byte r, byte g, byte b) : IEquatable<Colour> {
		public static readonly Dictionary<char, Colour> Palette = new() {
			{ 'k', new(0, 0, 0) },
			{ 'r', new(128, 0, 0) },
			{ 'g', new(0, 128, 0) },
			{ 'y', new(128, 128, 0) },
			{ 'b', new(0, 0, 128) },
			{ 'm', new(128, 0, 128) },
			{ 'c', new(0, 128, 128) },
			{ 'w', new(170, 170, 170) },
			{ 'K', new(85, 85, 85) },
			{ 'R', new(255, 0, 0) },
			{ 'G', new(0, 255, 0) },
			{ 'Y', new(255, 255, 0) },
			{ 'B', new(0, 0, 255) },
			{ 'M', new(255, 0, 255) },
			{ 'C', new(0, 255, 255) },
			{ 'W', new(255, 255, 255) }
		};


		private readonly byte _r = r;
		private readonly byte _g = g;
		private readonly byte _b = b;


		public override string ToString() {
			return $"${_r:X2}{_g:X2}{_b:X2}";
		}


		public override int GetHashCode() {
			return HashCode.Combine(_r, _g, _b);
		}

		public bool Equals(Colour other) {
			return (
				_r == other._r
				&& _g == other._g
				&& _b == other._b
			);
		}

		public override bool Equals(object? obj) {
			return (obj is Colour colour && Equals(colour));
		}

		public static bool operator==(Colour a, Colour b) {
			return a.Equals(b);
		}

		public static bool operator!=(Colour a, Colour b) {
			return !a.Equals(b);
		}


		//static char BestMatch(Colour c) {
		//	return Palette
		//		.Select(
		//			pair => new {
		//				pair,
		//				distance = Math.Sqrt(
		//					Math.Pow(c._r - pair.Value._r, 2)
		//					+ Math.Pow(c._g - pair.Value._g, 2)
		//					+ Math.Pow(c._b - pair.Value._b, 2)
		//				),
		//			}
		//		)
		//		.OrderBy(entry => entry.distance)
		//		.First()
		//		.pair.Key;
		//}

		internal static Colour Lerp(Colour start, double t, Colour end) {
			t = Math.Max(0.0, Math.Min(1.0, t));
			var tt = 1.0 - t;

			return new(
				(byte)(start._r * tt + end._r * t),
				(byte)(start._g * tt + end._g * t),
				(byte)(start._b * tt + end._b * t)
			);
		}

		//public static char LerpChar(Colour start, double t, Colour end) {
		//	return BestMatch(Lerp(start, t, end));
		//}
	}


	private static readonly SafeDictionary<int, Colour> floaters = new(SafeDictionary<int, Colour>.MissingKeyOperation.ReturnDefault);
	private static readonly double[] floaterPos = new double[3];

	private static readonly double[] floaterSpeed = new double[3];
	//static readonly double[,] _floaterColourSpeed = new double[3, 3];
	private static readonly Random random = new();

	private static int funkyTime = -1;


	private static string? RenderFunky(int length) {
		if (++funkyTime == 0) {
			var palettes = new[] {
				"kKwW",
				"bBcC",
				"rRmM",
				"gGyY",
				string.Join("", Colour.Palette.Keys)
			};

			var palette = palettes[random.Next(0, palettes.Length)];
			do {
				floaters[0] = Colour.Palette[palette[random.Next(0, palette.Length)]];
				floaters[1] = Colour.Palette[palette[random.Next(0, palette.Length)]];
				floaters[2] = Colour.Palette[palette[random.Next(0, palette.Length)]];
			} while (floaters[0] == floaters[1] || floaters[0] == floaters[2] || floaters[1] == floaters[2]);

			floaterPos[0] = 0;
			floaterPos[1] = random.NextDouble();
			floaterPos[2] = random.NextDouble();

			floaterSpeed[0] = 0;
			floaterSpeed[1] = random.NextDouble() / 2;
			floaterSpeed[2] = random.NextDouble() / 2;

			//for (int i = 0; i < 3; i++)
			//{
			//	for (int j = 0; j < 3; j++)
			//	{
			//		_floaterColourSpeed[i, j] = Random.NextDouble() / 200;
			//	}
			//}

			return null;
		}


		for (var i = 0; i < 3; i++) {
			floaterPos[i] = i * Math.Sin(funkyTime * floaterSpeed[i]) / 3.0 + i / 3.0;
			//_floaters[i].R = (byte)Math.Max(0.0, Math.Min(255.0, _floaters[i].R + Math.Sin(funkyTime * _floaterColourSpeed[i, 0]) * 128));
			//_floaters[i].G = (byte)Math.Max(0.0, Math.Min(255.0, _floaters[i].G + Math.Sin(funkyTime * _floaterColourSpeed[i, 1]) * 128));
			//_floaters[i].B = (byte)Math.Max(0.0, Math.Min(255.0, _floaters[i].B + Math.Sin(funkyTime * _floaterColourSpeed[i, 2]) * 128));
		}


		var ordered = floaterPos
			.Select((p, i) => (p, i))
			.OrderBy(x => x.p)
			.ToList();

		var size = Math.Min(length, funkyTime);
		var sb = new StringBuilder();
		for (var i = 0; i < size; i++) {
			var t = (size == 1)
				? 0.5
				: i / (double)(size - 1);

			var floaterBefore = ordered.LastOrDefault(x => x.p <= t);
			var floaterAfter = ordered.FirstOrDefault(x => x.p > t);

			Colour colourBefore;
			double p0;
			if (floaterBefore == default) {
				colourBefore = Colour.Palette['k'];
				p0 = 0.0;
			} else {
				colourBefore = floaters[floaterBefore.i];
				p0 = floaterBefore.p;
			}

			Colour colourAfter;
			double p1;
			if (floaterAfter == default) {
				colourAfter = Colour.Palette['k'];
				p1 = 1.0;
			} else {
				colourAfter = floaters[floaterAfter.i];
				p1 = floaterAfter.p;
			}

			sb.Append($"#{Colour.Lerp(colourBefore, (t - p0) / (p1 - p0), colourAfter)}# ");
		}

		return sb.ToString();
	}

	private static string FunkyIndent(int length) {
		return $"{
			RenderFunky(length)
		}#!#{
			((length - funkyTime > -1)
				? new string(' ', length - funkyTime)
				: null)
		}";
	}

	#endregion

	private static void PrintHeader(string? recordKind, bool isList = false) {
#if DEBUG
		var length = "LogPrintDebug:".Length;
		const string DEBUG = "~Y~Debug";
#else
		var length = "LogPrint:".Length;
		const string DEBUG = "";
#endif
		var templateLength = (length - 1) * 2;
		var template = Enumerable.Range(0, templateLength)
			.Select(
				n => Colour.Lerp(new(0, 192, 255), 1.0 * n / (templateLength - 1), new(0, 0, 255))
					.ToString()
			)
			.Concat(["b", "k"])
			.Select((c, i) => (c, i))
			.Aggregate(
				new List<string>(),
				(list, pair) => {
					if (pair.i % 2 == 0) {
						list.Add(pair.c);
					} else {
						list[^1] += $",{pair.c}";
					}

					return list;
				}
			);

		string Indent(int count) {
			return (count < length)
				? template[(count)..]
					.Aggregate(
						new StringBuilder(),
						(sb, c) => {
							var cc = c.Split(',');
							return sb.Append($"~{cc[0]}~#{cc[1]}#\u25E4");
						},
						sb => sb.Append("#k#").Append(new string(' ', count)).ToString()
					)
				: FunkyIndent(length);
		}

		string MapBG(string input) {
			var sb = new StringBuilder();
			var j = 0;
			for (var i = 0; i < input.Length; i++) {
				sb.Append($"#{template[j][..template[j++].IndexOf(',')]}#")
					.Append(input[i]);
				if (input[i] == '~') {
					sb.Append(input[++i])
						.Append(input[++i]);
				}
			}

			return sb.ToString();
		}


		if (isList) {
			Console.Out.WriteLineColours($"~W~{MapBG($"LogPrint{DEBUG}")}~C~#b#:#k# Available config:");
		} else {
			Console.Out.WriteLineColours(
				$"~W~{
					MapBG($"LogPrint{DEBUG}")
				}~C~#b#:#k# #y#{
					(
						follow
							? $"~M~Following{
								(
									followDir
										? "~R~ Directory"
										: ""
								)
							}~C~"
							: "Processing"
					)
				} ~Y~{
					recordKind
				}~W~#g# from ~Y~{
					(
						(fileName ?? "-") == "-"
							? (wrappedProcess == null)
								? "~M~STDIN"
								: $"~W~{wrappedProcess.StartInfo.FileName} ~Y~{wrappedProcess.StartInfo.Arguments}"
							: fileName
					)
				}#!#"
			);
		}

		var i = 0;

		if (isList) {
			ListRuleSets();
		} else {
			Console.Out.WriteLineColours($"{Indent(++i)} with ~Y~RuleSet ~G~{ruleSet!.Name}");
		}

		if (startTime.HasValue) {
			if (endTime.HasValue) {
				Console.Out.WriteLineColours(
					endTime == startTime
						? $"{Indent(++i)} showing logs ~Y~@ ~C~{startTime}"
						: $"{Indent(++i)} showing logs ~Y~@ ~C~{startTime} ~c~- ~C~{endTime}"
				);
			} else {
				Console.Out.WriteLineColours($"{Indent(++i)} showing logs ~Y~@ ~C~{startTime} ~c~onwards");
			}
		}

		if (flagQuery || timeQuery.HasValue || exceptionQuery || !(grep == null && invGrep == null)) {
			PrintQueries();
		}

		switch (stepMode) {
			case StepMode.None:
				break;

			case StepMode.RecordCount:
				Console.Out.WriteLineColours($"{Indent(++i)} ~c~Stepping by ~C~{stepCount} records ~c~at a time.");
				break;

			case StepMode.LineCount:
				Console.Out.WriteLineColours($"{Indent(++i)} ~c~Stepping by about ~C~{stepCount} lines ~c~at a time.");
				break;

			case StepMode.TimeSpan:
				Console.Out.WriteLineColours($"{Indent(++i)} ~c~Stepping by ~C~{stepTime} ~c~at a time.");
				break;

			default:
#pragma warning disable CA2208  // Instantiate argument exceptions correctly
				throw new ArgumentOutOfRangeException(nameof(stepMode), stepMode, $"Unhandled StepMode value: `{stepMode}`!");
#pragma warning restore CA2208
		}

		if (summariseMode) {
			Console.Out.WriteLineColours($"{Indent(++i)} #g#~W~Summarising log into ~C~{stepTime}~W~ buckets{", scaled down by ~M~".RCoalesce(stepCount.ToString().NullIfEmpty())}~W~.");
		}

		if (isList) {
			flagSets.AddRange(config.FlagSets);
			flagSets.Add(new TimeMarker(size: null, outputTimeSpan: false, TimeDeltaMode.PerAll));
		}

		if (!flagQuery && flagSets.Count > 0) {
			PrintFlags();
		}

		// ReSharper disable once InvertIf - //NOTE: can't ReadLine if we are reading STDIN!
		if (verifyArgs && !(recordKind == null || (fileName ?? "-") == "-" && wrappedProcess is null)) {
			Console.WriteLine("---Press-Enter---");
			Console.ReadLine();
		}

		return;


		static string? HighlightColourMatches(string? input) {
			return input
				?.Replace(AnsiConsoleColourExtensions.MATCH_FOREGROUND.EscapeColourCodeChars()!, $"{AnsiConsoleColourExtensions.PUSH_BG}#B#~~~~{AnsiConsoleColourExtensions.POP_BG}")
				.Replace(AnsiConsoleColourExtensions.MATCH_BACKGROUND.EscapeColourCodeChars()!, $"{AnsiConsoleColourExtensions.PUSH_BG}#B#####{AnsiConsoleColourExtensions.POP_BG}")
				.Replace(AnsiConsoleColourExtensions.MATCH_ANY.EscapeColourCodeChars()!, $"{AnsiConsoleColourExtensions.PUSH_BG}#B#~~##{AnsiConsoleColourExtensions.POP_BG}");
		}


		void ListRuleSets() {
			foreach (var currentRuleSet in config.RuleSets) {
				Console.Out.WriteLineColours($"{Indent(++i)} ~Y~RuleSet ~G~{currentRuleSet.Name}~y~:");
				var maxWidth = Console.IsOutputRedirected
					? int.MaxValue
					: Console.WindowWidth - 1;

				if (currentRuleSet.Vars.Any()) {
					const string FIRST = "~m~Vars~r~: ~c~";
					const string OTHERS = "   ~c~";

					string Prefix(int count) {
						return $"{Indent(count)}     ";
					}

					var output = new StringBuilder($"{Prefix(++i)}{FIRST}{currentRuleSet.Vars.First().Key}");

					foreach (var (key, _) in currentRuleSet.Vars) {
						if (output.ToString().StripColourCodes().Length + key.Length + 2 < maxWidth) {
							output
								.Append("~B~, ~c~")
								.Append(key);

							continue;
						}


						Console.Out.WriteLineColours(output.ToString());
						output.Clear();
						output.Append(Prefix(++i) + OTHERS + key);
					}

					if (output.Length > 0) {
						Console.Out.WriteLineColours(output.ToString());
					}
				}

				foreach (var rule in currentRuleSet.RulesList) {
					Console.Out.WriteLineColours($"{Indent(++i)}     ~y~Rule ~g~{rule.Name}");
				}
			}

			Console.Out.WriteLineColours(Indent(++i));
		}

		// ReSharper disable once ImplicitlyCapturedClosure - don't care.
		void PrintQueries() {
			var joiner = "     ";
			Console.Out.WriteLineColours($"{Indent(++i)} ~G~Querying ~g~for:");
			if (flagQuery) {
				Console.Out.WriteLineColours($"{Indent(++i)}     ~G~Flag ~m~changes: ");
				flagSets.ForEach(
					flagSet => {
						var suffix = (flagSet.autoTrackID || flagSet.trackIDValueRE != null)
							? "~Y~=~y~" + flagSet.trackIDValueRE
							: null;

						// ReSharper disable once AccessToModifiedClosure - evaluated immediately.
						Console.Out.WriteLineColours(
							$"{
								Indent(++i)
							}         ~{
								(flagSet.IsQuerying
									? 'G'
									: 'M')
							}~{
								flagSet.Name
							}{
								suffix
							}"
						);
						flagSet.Flags.ToList()
							.ForEach(
								flag => Console.Out.WriteLineColours(
									$"{
										Indent(++i)
									}             ~{
										(flag.IsQuerying
											? 'G'
											: 'M')
									}~{
										flag.Name
									}"
								)
							);
					}
				);

				joiner = "  ~g~or:";
			}

			if (timeQuery.HasValue) {
				Console.Out.WriteLineColours($"{Indent(++i)} {joiner} ~R~Long-Duration: ~r~time-since-last-log ~R~>= ~M~{timeQuery}");
				joiner = "  ~g~or:";
			}

			if (exceptionQuery) {
				Console.Out.WriteLineColours($"{Indent(++i)} {joiner} ~M~Exceptions.");
				joiner = "  ~g~or:";
			}

			var lineType = grepRawLine
				? "Raw"
				: grepStrippedOutput
					? "Formatted & Stripped"
					: "Formatted";

			var rex = (grep: grep.EscapeColourCodeChars(), invGrep: invGrep.EscapeColourCodeChars(), exclGrep: exclGrep.EscapeColourCodeChars());
			if (!(grepRawLine || grepStrippedOutput)) {
				rex = (HighlightColourMatches(rex.grep), HighlightColourMatches(rex.invGrep), HighlightColourMatches(rex.exclGrep));
			}

			if (grep != null) {
				Console.Out.WriteLineColours($"{Indent(++i)} {joiner} ~M~{lineType} lines matching ~c~/~C~{rex.grep}~c~/~M~.");
				joiner = "  ~g~or:";
			}

			if (invGrep != null) {
				Console.Out.WriteLineColours($"{Indent(++i)} {joiner} ~M~{lineType} lines ~Y~not~M~ matching ~c~/~C~{rex.invGrep}~c~/~M~.");
			}

			if (exclGrep != null) {
				Console.Out.WriteLineColours($"{Indent(++i)} {joiner} ~Y~Suppress~M~ {lineType} lines matching ~c~/~C~{rex.exclGrep}~c~/~M~.");
			}
		}

		// ReSharper disable once ImplicitlyCapturedClosure - don't care.
		void PrintFlags() {
			Console.Out.WriteLineColours(
				isList
					? $"{Indent(++i)} Flags~m~:"
					: $"{Indent(++i)} With ~M~Flags~m~:"
			);

			flagSets.ForEach(
				flagSet => {
					var suffix = (isList && !(flagSet is TimeMarker || flagSet.TrackID == null) || flagSet.autoTrackID || flagSet.trackIDValueRE != null)
						? "~Y~=~y~"
						+ (flagSet.trackIDValueRE?.ToString()
							?? (
								isList
									? "~Y~[~c~/id/~Y~]"
									: ""
							)
						)
						: null;

					Console.Out.WriteLineColours($"{Indent(++i)}     ~M~{flagSet.Name}{suffix}");
					foreach (var flag in flagSet.Flags) {
						Console.Out.WriteLineColours($"{Indent(++i)}         ~m~{flag.Name}");
						if (isList) {
							flag.Evals.ToList().ForEach(eval => Console.Out.WriteLineColours($"{Indent(++i)}             ~y~{eval.When}"));
						}
					}
				}
			);
		}
	}


	private static void ProcessLines(ILineReader reader, TimeSpan timeout) {
		PrintHeader("lines");

		string? line;

		var accumulated = new StringBuilder();
		while (!((line = reader.GetNextLine(timeout)) == null || breakForNextFile)) {
#if DEBUG_INPUT
			Console.Error.WriteLine("Read got: " + ("'".RCoalesce(line, "'") ?? "null").Replace("\r", @"\r").Replace("\n", @"\n"));
#endif
			if (line == "") {
				// Timed out...
#if DEBUG_INPUT
				Console.Error.WriteLine("Read timed out");
#endif
				Thread.Sleep(timeout);
				continue;
			}


			accumulated.Append(line);
#if DEBUG_INPUT
			Console.Error.WriteLine("acc => " + accumulated.ToString().Replace("\r", @"\r").Replace("\n", @"\n"));
#endif

			var output = accumulated.ToString();
			if (!output.Contains("\n")) {
				continue;
			}


#if DEBUG_INPUT
			Console.Error.WriteLine("Found a line...");
#endif
			var parts = Regex.Match(output, @"^(.+[\n\r]+)*(.+)");
#if DEBUG_INPUT
			parts.Groups[0].Captures.Select(c => c.Value).ToList().Dump("wholeLines", true);
			parts.Groups[1].Value.Dump("partialLine");
#endif

			accumulated.Clear();
#if DEBUG_INPUT
			Console.Error.WriteLine("clear acc");

#endif
			if (parts.Groups[0].Success) {
				accumulated.Append(parts.Groups[1].Value);
#if DEBUG_INPUT
				Console.Error.WriteLine("acc += partial: " + parts.Groups[1].Value.Replace("\r", @"\r").Replace("\n", @"\n"));
#endif
			}

			parts.Groups[0]
				.Captures
				.Select(capture => capture.Value)
				.ToList()
				.ForEach(ProcessAndOutputLine);
		}

#if DEBUG_INPUT
		Console.Error.WriteLine("Read got: " + ("'".RCoalesce(line, "'") ?? "null").Replace("\r", @"\r").Replace("\n", @"\n"));

#endif
		if (accumulated.Length > 0) {
			accumulated
				.ToString()
				.Split(lineBreakChars, StringSplitOptions.RemoveEmptyEntries)
				.ToList()
				.ForEach(ProcessAndOutputLine);
		}
	}

	private static void ProcessRecords(ILineReader reader, TimeSpan timeout) {
		PrintHeader($"Records~W~, delimited by /~C~{ruleSet!.RecordStart!.ToString().EscapeColourCodeChars()}~W~/");

		string? line;

		var record = new List<string>();

		while (!((line = reader.GetNextLine(timeout)) == null || breakForNextFile)) {
#if DEBUG_INPUT
			Console.Error.WriteLine("Read got: " + ("'".RCoalesce(line, "'") ?? "null").Replace("\r", @"\r").Replace("\n", @"\n"));
#endif
			if (line == "") {
				// Timed out...
#if DEBUG_INPUT
				Console.Error.WriteLine("Read timed out; record#=" + record.Count);
#endif
				if (record.Count > 0) {
					var lastLine = record[^1];
					if (lastLine[^1] != '\n') {
						continue;
					}


					ProcessAndOutputLine(string.Join("", record));
					record.Clear();
				}


				Thread.Sleep(timeout);
				continue;
			}


			var isStartOfNewRecord = ruleSet.RecordStart!.IsMatch(line);
			if (isStartOfNewRecord && record.Count > 0) {
#if DEBUG_INPUT
				Console.Error.WriteLine("New record starts; output last record#=" + record.Count);
#endif
				ProcessAndOutputLine(string.Join("", record));
				record.Clear();
			}
#if DEBUG_INPUT
			if (!isStartOfNewRecord) {
				Console.Error.WriteLine($"broken record; record#={record.Count}; line = '{line.Replace("\r", @"\r").Replace("\n", @"\n")}'");
			}
#endif

			if (record.Count == 0) {
#if DEBUG_INPUT
				Console.Error.WriteLine($"Add (first) line = '{line.Replace("\r", @"\r").Replace("\n", @"\n")}'");
#endif
				record.Add(line);
			} else {
				var lastLine = record[^1];
				if (lastLine[^1] == '\n') {
#if DEBUG_INPUT
					Console.Error.WriteLine($"Add line = '{line.Replace("\r", @"\r").Replace("\n", @"\n")}'");
#endif
					record.Add(line);
				} else {
#if DEBUG_INPUT
					Console.Error.WriteLine($"Append line = '{line.Replace("\r", @"\r").Replace("\n", @"\n")}'");
#endif
					record[^1] += line;
				}
			}
#if DEBUG_INPUT
			Console.Error.WriteLine($"record# => {record.Count}; line = '{line.Replace("\r", @"\r").Replace("\n", @"\n")}'");
#endif
		}


		if (record.Count > 0) {
			ProcessAndOutputLine(string.Join("", record));
		}
	}

	private static void ProcessAndOutputLine(string line) {
		var time = TimeMarker.GetTime(line);

		if (firstLine) {
			stepProgress = 0U;
			if (!summariseMode) {
				stepStartTime = time;
			}

			if (time.HasValue) {
				if (startTime.HasValue) {
					start = time.Value.Date + startTime;
				}

				if (endTime.HasValue) {
					end = time.Value.Date + endTime;
				}
			}

			firstLine = false;
		}

		lastTime = time;

		if (start.HasValue || end.HasValue) {
			if (time < start || time > end)		// Nullable comparisons of null are false.
			{
				if (!shownSkip) {
					Console.Out.WriteLineColours($"~K~---------------------------------~w~8~W~<~K~-~c~[~C~{time:yyyy-MM-dd HH:mm:ss.ffff}~c~]~K~----------------------------------------------");
					shownSkip = true;
					progressDrawn = null;
				}

				flagSets.ForEach(flagSet => flagSet.Process(line));		// Still need to process these...

				return;
			}
		}

		shownSkip = false;

		var action = ruleSet!.Reset?.IsMatch(line) ?? false
			? (Func<FlagSet, string, string>)(
				(flagSet, l) => flagSet.Reset(l)
			)
			: (flagSet, l) => flagSet.Process(l);

		var flagsOutput = flagSets.Aggregate(
			new StringBuilder(),
			(sb, flagSet) => sb.Append(action(flagSet, line)),
			sb => sb.ToString()
		);

		if (ProcessQueryTests()) {
			return;
		}


		var trimmedLine = line.TrimEnd(lineBreakChars).EscapeColourCodeChars();
		if (summariseMode && time.HasValue) {
			SummariseLine(time.Value, ruleSet.Summarise(trimmedLine));
			return;
		}


		string? output;
		try {
			output = ruleSet.Process(trimmedLine);
		} catch (JsonReaderException) {
			Console.Error.WriteLine(trimmedLine);
			throw;
		}


		// If we did highlight the match then it won't match anymore, so don't check:
		if (!ruleSet.DidHighlight
			&& ProcessPostGrep(
				grepStrippedOutput
					? output.StripColourCodes()
					: output ?? ""
			)
		) {
			return;
		}


		lastPrintedTime = time;
		progressDrawn = null;
		Console.Out.WriteColours(flagsOutput);
		Console.Out.WriteLineColours(output);
		queriedFlagStateChanged = false;

		switch (stepMode) {
			case StepMode.None:
				break;

			case StepMode.RecordCount:
				stepProgress++;
				if (stepProgress >= stepCount) {
					stepProgress = 0U;
					Pause();
				}

				break;

			case StepMode.LineCount:
				stepProgress += (uint)(output ?? "").Occurrences('\n');
				if (stepProgress >= stepCount) {
					stepProgress = 0U;
					Pause();
				}

				break;

			case StepMode.TimeSpan:
				if (time.HasValue && stepStartTime.HasValue && time.Value - stepStartTime.Value >= stepTime) {
					stepStartTime = time;
					Pause();
				}

				break;

			default:
#pragma warning disable CA2208  // Instantiate argument exceptions correctly
				throw new ArgumentOutOfRangeException(nameof(stepMode), stepMode, $"Unhandled StepMode value: `{stepMode}`!");
#pragma warning restore CA2208
		}


		return;


		// ReSharper disable once ImplicitlyCapturedClosure - don't care.
		bool ProcessQueryTests() {
			var delta = ((TimeMarker?)flagSets.FirstOrDefault(flagSet => flagSet is TimeMarker))?.LastDelta;
			// ReSharper disable once InvertIf
			if (
				(flagQuery || timeQuery.HasValue || exceptionQuery || !(grep == null && invGrep == null) && grepRawLine)	// There is a query of some sort...
				&& !(																						// And not:
						queriedFlagStateChanged																	// :This only ever happens if flagQuery is true.
						|| delta >= timeQuery																	// :Nullable comparison of null is false.
						|| exceptionQuery && line.Contains("Exception")											// :Explicitly checked.
						|| grepRawLine
						&& (
							(grepRE?.IsMatch(line) ?? false)													// :Implicitly checked.
							|| !(invGrepRE?.IsMatch(line) ?? true)												// :Implicitly checked.
						)
					)
				|| grepRawLine && (exclGrepRE?.IsMatch(line) ?? false)
			) {
				// ReSharper disable once InvertIf
				if (time.HasValue && !(DateTime.Now - progressDrawn < progressThrottle))	// Nullable compare.
				{
					Console.Out.WriteColours($"#K#~k~ {time.Value:yyyy-MM-dd HH:mm:ss.ffff} \r");
					progressDrawn = DateTime.Now;
				}

				return true;
			}


			return false;
		}

		static bool ProcessPostGrep(string str) {
			return !grepRawLine
				&& (
					!(
						grep == null && invGrep == null
						|| (grepRE?.IsMatch(str) ?? false)
						|| !(invGrepRE?.IsMatch(str) ?? true)
					)
					|| (exclGrepRE?.IsMatch(str) ?? false)
				);
		}
	}

	private static void SummariseLine(DateTime time, (LogLevel level, string marker) output) {
		if (time < stepStartTime + stepTime) {
			AppendSummaryItem(output);
		} else {
			// ReSharper disable once PossibleInvalidOperationException
			stepStartTime ??= time.TruncateTo(stepTime);

			AppendAndClearBucket();
			if (summarised!.Length > 0) {
				// ReSharper disable once PossibleInvalidOperationException
				Console.Out.WriteLineColours(timeOutputRuleSet!.Process(stepStartTime.Value.ToString(DATE_TIME_FORMAT)) + summarised);
				summarised.Clear();
			}

#pragma warning disable CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.
			for (stepStartTime += stepTime; stepStartTime < time - stepTime; stepStartTime += stepTime)
#pragma warning restore CS8620
			{
				Console.Out.WriteLineColours(timeOutputRuleSet!.Process(stepStartTime.Value.ToString(DATE_TIME_FORMAT)));
			}

			AppendSummaryItem(output);
		}
	}

	private static void AppendSummaryItem((LogLevel level, string marker) output) {
		if (stepCount > 1) {
			bucket.Add(output);
			if (bucket.Count == stepCount.Value) {
				AppendAndClearBucket();
			}
		} else {
			summarised!.Append(output.marker);
		}
	}

	private static void AppendAndClearBucket() {
		if (!bucket.Any()) {
			return;
		}


		summarised!.Append(
			bucket
				.MaxBy(pair => Precedence.LogLevels.IndexOf(pair.level))
				.marker
		);

		bucket.Clear();
	}

	private static void Pause() {
		while (true) {
			Console.Out.WriteColours("#w#~W~Paused> ");
			var key = Console.ReadKey(intercept: true);
			Console.Out.Write("\r       \r");
			if (key is { Modifiers: 0, Key: ConsoleKey.Q or ConsoleKey.Escape }) {
				breakForNextFile = true;
			}
#if DEBUG
			else if (key.Key == ConsoleKey.D) {
				if (Debugger.IsAttached) {
					Debugger.Break();
				} else {
					Debugger.Launch();
				}
			}
#endif
			else if (key.KeyChar == '-') {
				Console.Out.WriteLineColours("~K~" + new string('-', Console.WindowWidth - 1));
			} else if (key.Key == ConsoleKey.F1 || key.Key == ConsoleKey.H || key.Key == ConsoleKey.Help || key.KeyChar == '?') {
#if DEBUG
				Console.Out.WriteLineColours(
					"#K#~g~When ~W~Paused~g~, press ~Y~Q~g~ or ~Y~Esc~g~ to ~R~Exit~g~, ~Y~-~g~ to write an ~G~<hr/>~g~, ~Y~?~g~, ~Y~H~g~ or ~Y~F1~g~ for this ~G~help~g~, ~Y~D~g~ to ~R~Debug~g~, or anything else to ~C~step~g~."
				);
#else
				Console.Out.WriteLineColours("#K#~g~When ~W~Paused~g~, press ~Y~Q~g~ or ~Y~Esc~g~ to ~R~Exit~g~, ~Y~-~g~ to write an ~G~<hr/>~g~, ~Y~?~g~, ~Y~H~g~ or ~Y~F1~g~ for this ~G~help~g~, or anything else to ~C~step~g~.");
#endif
				continue;
			}


			break;
		}
	}


	[GeneratedRegex(@"^(\d+)x$")]
	private static partial Regex RepeatMultipleRE();
}
