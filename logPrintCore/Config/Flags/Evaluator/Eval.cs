#if true
#define DEBUG_COMPILE
#endif
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

using JetBrains.Annotations;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

using logPrintCore.Ansi;
using logPrintCore.Utils;

namespace logPrintCore.Config.Flags.Evaluator;

[UsedImplicitly]
internal sealed class Eval : IValidatableObject {
	private static readonly string twoNewLines = Environment.NewLine + Environment.NewLine;

	private static Type fieldTypeBeingLookedUp = null!;

	// ReSharper disable once CollectionNeverUpdated.Local
	private static readonly SafeDictionary<string, MethodInfo?> tryParseMethods = new(
		(key, dict) => {
			var value = fieldTypeBeingLookedUp.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.FirstOrDefault(
					method => {
						if (method.Name != "TryParse") {
							return false;
						}


						var parameters = method.GetParameters();
						return (
							parameters.Length == 2
							&& parameters[0].ParameterType == typeof(string)
							&& parameters[1].ParameterType.FullName == $"{fieldTypeBeingLookedUp.FullName}&"
							&& parameters[1].IsOut
						);
					}
				);

			dict[key] = value;
			return value;
		}
	);

	// ReSharper disable once CollectionNeverUpdated.Local
	private static readonly SafeDictionary<string, MethodInfo?> parseMethods = new(
		(key, dict) => {
			var value = fieldTypeBeingLookedUp.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.FirstOrDefault(
					method => {
						if (method.Name != "Parse") {
							return false;
						}


						var parameters = method.GetParameters();
						return (parameters.Length == 1 && parameters[0].ParameterType == typeof(string));
					}
				);

			dict[key] = value;
			return value;
		}
	);

	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
	[Required]
	public string When { get; init; } = null!;

	public string? Output { get; init; }

	public string? OutputCode { get; [UsedImplicitly] init; }


	private IEvaluator? _evaluator;

	internal void SetLastOutput(string? output) {
		_evaluator?.LastOutput = output;
	}


	internal string? Process(string line, Flag parent) {
		return PopulateValues(line, parent) && _evaluator!.Eval()
			? _evaluator.GetOutput()?.Invoke() ?? Output
			: null;
	}

	internal void Reset() {
		_evaluator?.Reset();
	}


	private bool PopulateValues(string line, Flag parent) {
		if (_evaluator == null) {
			var str = $"#y# ~Y~Compiling: ~W~#!#{this}\r";
			Console.Out.WriteColours(str);
			//NOTE: we want a stable-across-runs hashcode so can no longer rely on string.GetHashCode nor HashCode.Combine!:
			var entries = parent.Types.Select(pair => (pair.Key, pair.Value))
				.Union(parent.Defines.SelectMany(d => d.Values.Select(p => ($"{d.Name}[{d.Type}].{p.Key}", p.Value))))
				.Union(parent.Consts.Select(c => ($"{c.Name}[{c.Type}", c.Value)))
				.Union(parent.Fields.Select(c => ($"{c.Name}[{c.Type}", c.Value)))
				.Union(parent.Properties.Select(p => ($"{p.Name}[{p.Type}", p.Code)))
				.Union(parent.Methods.Select(m => (m.Name, m.Code)))
				.Select<(string key, string value), string>((key, value) => $"{key}:{value}")
				.ToArray();
			GenerateExpression(When, parent, GenerateStableHash(parent.Name, $"{When}{(Output ?? OutputCode)}", entries));
			Console.Out.ClearLine();
			var length = (str.StripColourCodes().Length - 1) / Console.WindowWidth;
			while (length > 0) {
				var (left, top) = Console.GetCursorPosition();
				Console.SetCursorPosition(left, top - 1);
				Console.Out.ClearLine();
				length--;
			}
		}

		SetValues(_evaluator!, parent.OnMatch!.Match(line));
		if (parent.Methods.Any()) {
			_evaluator!.CallMethods();
		}

		return true;
	}

	private static uint GenerateStableHash(string first, string second, params ReadOnlySpan<string> rest) {
		unchecked {
			var hash = (uint)first.GetStableHashCode() * 357 + (uint)second.GetStableHashCode();
			foreach (var other in rest) {
				hash *= 357;
				hash += (uint)other.GetStableHashCode();
			}

			return hash;
		}
	}

	private void GenerateExpression(string code, Flag parent, uint hashCode) {
		var existingType = AppDomain.CurrentDomain
			.GetAssemblies()
			.Select(assembly => assembly.GetType("logPrint.Flags.Evaluator.Evaluator" + hashCode))
			.FirstOrDefault(type => type != null);

		if (existingType != null) {
			_evaluator = (IEvaluator?)existingType
				.GetConstructor(Type.EmptyTypes)
				?.Invoke([]);

			return;
		}

	retry:
		(var className, var path, (string src, EmitResult compilerResults)? results) = CompileEvaluator(code, OutputCode!.Trim(Environment.NewLine.ToCharArray()), parent, hashCode);
#if DEBUG_COMPILE
		if (results?.compilerResults.Diagnostics.Length > 0) {
			(string src, EmitResult compilerResults) = results.Value;
			Console.WriteLine();
			this.Dump("Eval", escapeColours: false);
			compilerResults.Diagnostics.Dump(multiLine: true);
			(
				Environment.NewLine
				+ string.Join(
					Environment.NewLine,
					src.Replace("\t", "   ")
						.Split(Environment.NewLine)
						.Select(
							(line, num) => $"#c#~C~{
								num + 1
								:D3}~Y~:#!# ~W~{
								line.EscapeColourCodeChars()
							}{
								Environment.NewLine.RCoalesce(
									string
										.Join(
											Environment.NewLine,
											compilerResults.Diagnostics
												.Where(d => d.Location.GetLineSpan().StartLinePosition.Line == num)
												.Select(
													d => $"~R~{
														d.Id
													}:{
														d.Severity
													}{
														(d.WarningLevel > 0
															? $":{d.WarningLevel}"
															: "")
													}: ~M~{
														d.GetMessage().EscapeColourCodeChars()
													}"
												)
										)
										.TrimToNull()
								)
							}"
						)
				)
			).Dump("Generated Code with Errors", escapeColours: false);
			Console.WriteLine();
		}
#endif

		if (!(results?.compilerResults.Success ?? true)) {
			Console.WriteLine();
			this.Dump("Eval", escapeColours: false);
			results.Value.compilerResults.Diagnostics.Dump(multiLine: true);
			Environment.Exit(-100);
		}


		try {
			_evaluator = (IEvaluator?)AssemblyLoadContext.Default
				.LoadFromAssemblyPath(path)
				.GetTypes()
				.First(t => t.Name == className)
				.GetConstructor(Type.EmptyTypes)
				?.Invoke([]);
		} catch (ReflectionTypeLoadException) {
			if (Program.forceCompile || !File.Exists(path)) {
				throw;
			}


			Program.forceCompile = true;
			goto retry;
		}
	}

	private static string GenerateFileContent(string className, string code, string? outputCode, Flag parent) {
		var defines = Environment.NewLine.RCoalesce(
			string.Join(Environment.NewLine, parent.Defines.Select(d => $"	const {d.Type} {d.Name} = {d.Value(parent.SelectedDefines)};"))
				.NullIfEmpty(),
			twoNewLines
		);

		var consts = Environment.NewLine.RCoalesce(
			string.Join(Environment.NewLine, parent.Consts.Select(c => $"	const {c.Type} {c.Name} = {c.Value};"))
				.NullIfEmpty(),
			twoNewLines
		);

		var fields = Environment.NewLine.RCoalesce(
			"#pragma warning disable CS0414",
			Environment.NewLine,
			string.Join(Environment.NewLine, parent.Types.Select(f => $"	public {f.Value} {f.Key} = default!;"))
				.NullIfEmpty(),
			Environment.NewLine,
			"#pragma warning restore CS0414",
			twoNewLines
		);

		var otherFields = Environment.NewLine.RCoalesce(
			string.Join(Environment.NewLine, parent.Fields.Select(f => $"	{f.Type} {f.Name} = {f.Value};"))
				.NullIfEmpty(),
			twoNewLines
		);

		var props = Environment.NewLine.RCoalesce(
			string
				.Join(
					Environment.NewLine,
					parent.Properties.Select(
						p => $"\t{
							p.Type
						} {
							p.Name
						} {{ get {{ {
							(p.Code.Contains("return")
								? p.Code
								: "return " + p.Code)
						}; }} }}"
					)
				)
				.NullIfEmpty(),
			twoNewLines
		);

		var parentMethods = parent.Methods.ToList();
		if (parentMethods.All(m => m.Name != "Reset")) {
			parentMethods.Add(
				new() {
					Name = "Reset",
					Code = ""
				}
			);
		}

		var methods = Environment.NewLine.RCoalesce(
			string
				.Join(
					Environment.NewLine,
					parentMethods.Select(
						m =>
							$$"""
								{{
									(m.Name == "Reset"
										? "public "
										: "")
								}}void {{
									m.Name
								}}() {{{
									Environment.NewLine.RCoalesce(
										"\t\t",
										CleanupCode(m.Code),
										Environment.NewLine,
										"\t"
									)
								}}}

							"""
					)
				)
				.NullIfEmpty(),
			Environment.NewLine
		);

		var src =
			$$"""
			#nullable enable

			using System;
			using System.Linq;
			using System.Reflection;

			namespace logPrintCore.Config.Flags.Evaluator;

			public class {{
				className
			}} : IEvaluator {{{
				defines
			}}{{
				consts
			}}{{
				fields
			}}{{
				otherFields
			}}{{
				props
			}}
				public string? LastOutput { get; set; }
				public bool Else { get; private set; }
				
				public bool Eval() {
					return {{
						((code == "Else")
							? code
							: $"(!Else && ({code}))")
					}};
				}
				{{
					(
						outputCode is null
							? ""
							: $$"""

									public Func<string>? GetOutput() {
										return () => $"{{
											outputCode
										}}";
									}

								"""
					)
				}}{{
					methods
				}}

				public void CallMethods() {{{
					Environment.NewLine.RCoalesce(
						"\t\t",
						string.Join(
							$"{Environment.NewLine}\t\t",
							parent.Methods
								.Where(method => method.Name != "Reset")
								.Select(m => $"{m.Name}();")
						),
						Environment.NewLine,
						"\t"
					)
				}}}


				public override string ToString()
				{
					return string.Format(
						"[{0}: Eval(#<##y#%#>#)=~<~{1}; {2}~>~]",
						GetType().Name,
						Eval()
							? "~<~~C~True~>~"
							: "~<~~M~False~>~",
						string.Join(
							"~W~, ",
							GetType()
								.GetFields(BindingFlags.Public | BindingFlags.Instance)
								.Select(x => $"~>~{x.Name}=~<~~G~{x.GetValue(this)}")
						)
					);
				}
			}
			""";

#if DEBUG_COMPILE
		File.WriteAllText(Path.Combine(Program.compileTemp, $"{className}.cs"), src);

#endif
		return src;


		static string? CleanupCode(string? codeStr) {
			return codeStr
				?.Replace("\r", "")
				.Replace("    ", "\t")
				.Replace("\n", $"{Environment.NewLine}\t\t")
				.TrimToNull();
		}
	}

	private static (string className, string path, (string src, EmitResult)?) CompileEvaluator(string code, string? outputCode, Flag parent, uint hashCode) {
		var className = $"Evaluator{hashCode}";
		var fileName = Path.Combine(Program.compileTemp, $"{className}.dll");

		if (File.Exists(fileName) && !Program.forceCompile) {
			return (className, fileName, null);
		}

		var src = GenerateFileContent(className, code, outputCode, parent);
		var compilation = CSharpCompilation.Create(className)
			.WithOptions(
				new(
					OutputKind.DynamicallyLinkedLibrary,
					sourceReferenceResolver: new SourceFileResolver(
						[".", Program.compileTemp],
						null,
						[
							new("logPrintCore.Config.Flags.Evaluator", Program.compileTemp),
							new($"logPrintCore.Config.Flags.Evaluator/{className}.cs", Path.Combine(Program.compileTemp, $"{className}.cs")),
							new($"logPrintCore.Config.Flags.Evaluator\\{className}.cs", Path.Combine(Program.compileTemp, $"{className}.cs"))
						]
					)
				)
			)
			.AddReferences(AppDomain.CurrentDomain.GetAssemblies().Select(a => MetadataReference.CreateFromFile(a.Location)))
			.AddSyntaxTrees(SyntaxFactory.ParseSyntaxTree(src));

		var emitResult =
#if DEBUG_COMPILE
			compilation.Emit(fileName, fileName.Replace("dll", "pdb"));
#else
			compilation.Emit(fileName);
#endif
		return (className, fileName, (src, emitResult));
	}


	private static void SetValues(IEvaluator evaluator, Match match) {
		if (!match.Success) {
			return;
		}


		var type = evaluator.GetType();

		foreach (Group matchGroup in match.Groups) {
			if (char.IsDigit(matchGroup.Name[0])) {
				continue;
			}


			var field = type.GetField(matchGroup.Name, BindingFlags.Public | BindingFlags.Instance)
				?? throw new MemberAccessException($"Field {matchGroup.Name} not found on {type.FullName}!");


			var fieldType = field.FieldType.ToString().StartsWith("System.Nullable`1[", StringComparison.Ordinal)
				? field.FieldType.GenericTypeArguments[0]
				: field.FieldType;

			if (fieldType == typeof(string)) {
				field.SetValue(
					evaluator,
					matchGroup.Success
						? matchGroup.Value
						: null
				);
				continue;
			}


			if (matchGroup.Success) {
				SetFieldValue(evaluator, field, fieldType, matchGroup.Value);
			} else {
				field.SetValue(evaluator, null);
			}
		}
	}

	private static void SetFieldValue(IEvaluator evaluator, FieldInfo field, Type fieldType, string value) {
		fieldTypeBeingLookedUp = fieldType;

		var key = evaluator.GetType().Name + field.Name;
		var tryParseMethod = tryParseMethods[key];
		if (tryParseMethod is not null) {
			var args = new object?[] { value, null };
			var success = (bool)(tryParseMethod.Invoke(null, args) ?? throw new UnreachableException("TryParse returned null!"));
			if (success) {
				field.SetValue(evaluator, args[1]);
			} else {
				Console.Error.WriteLineColours($"#y#~R~{field.FieldType.Name}.TryParse(\"{value.EscapeColourCodeChars()}\", out var v) failed!");
			}

			return;
		}


		var parseMethod = parseMethods[key]
			?? throw new InvalidOperationException("Cannot find TryParse or Parse on " + field.FieldType);


		field.SetValue(evaluator, parseMethod.Invoke(null, [value]));
	}


	public override string ToString() {
		return $"{{{
			GetType().Name
		}: {
			nameof(When)
		}={
			When
		}, {
			nameof(Output)
				.RCoalesce("=#<#~<~", Output, "~>~#>#, ")
				.EscapeColourCodeChars()
		}{
			nameof(OutputCode)
				.RCoalesce("=#<#~<~", OutputCode?.Trim(Environment.NewLine.ToCharArray()), "~>~#>#, ")
				.EscapeColourCodeChars()
				?.Replace("{", "#<#~<~#b#~M~{~C~")
				.Replace("}", "~M~}~>~#>#")
		}{
			nameof(_evaluator)
		}:{
			_evaluator
		}}}";
	}


	/// <inheritdoc />
	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
		if (Output is null == OutputCode is null) {
			yield return new("One and only one of `Output` and `OutputCode` must be declared!");
		}
	}
}
