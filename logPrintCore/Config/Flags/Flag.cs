using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;

using JetBrains.Annotations;

using logPrintCore.Ansi;
using logPrintCore.Config.Flags.Evaluator;
using logPrintCore.Utils;

namespace logPrintCore.Config.Flags;

internal sealed class Flag : IValidatableObject {
	private const string RESET = "#!#~!~";


	private string[] _outputLookup = null!;
	private FlagState _state;
	private Dictionary<string, Type>? _typesLookup;
	private string? _lastEvalOutput = " ";


	public event StateChangeCallback? OnStateChange;


	internal FlagState State {
		get => _state;
		private set {
			if (!Enum.IsDefined(typeof(FlagState), value)) {
				throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FlagState));
			}


			if (_state != value || _state == InitialState && Types.Any()) {
				OnStateChange?.Invoke(this, value);
			}

			_state = value;

			if (_state == InitialState && Types.Any()) {
				_lastEvalOutput = Evals.FirstOrDefault(eval => eval.When == "Else")?.Output ?? "~k~ ";
			}
		}
	}

	[Required]
	public string Name { get; private init; } = null!;

	// ReSharper disable MemberCanBePrivate.Global
	public string? Off { get; init; }
	public string? On { get; init; }

	public string? TransitionToOff { get; init; }
	public string? TransitionToOn { get; init; }

	public Regex? OffMatch { get; init; }
	public Regex? OnMatch { get; init; }

	public Regex? TransitionToOffMatch { get; init; }
	public Regex? TransitionToOnMatch { get; init; }

	public Regex? ToggleMatch { get; [UsedImplicitly] init; }

	public FlagState InitialState { get; init; }

	public Dictionary<string, string> Types { get; init; } = new();
	// ReSharper restore MemberCanBePrivate.Global

	public Define[] Defines { get; private init; } = [];

	public Field[] Consts { get; private init; } = [];
	public Field[] Fields { get; private init; } = [];

	public Property[] Properties { get; private init; } = [];

	public Method[] Methods { get; private init; } = [];

	public Eval[] Evals { get; private init; } = [];


	public bool IsQuerying => (OnStateChange != null);


	public List<string> SelectedDefines { get; set; } = null!;


	/// <inheritdoc />
	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
		var result = new List<ValidationResult>();

		if (Types.Any()) {
			if (OnMatch == null) {
				result.Add(new("OnMatch must be specified for an eval-flag!"));
			}

			if (!(Evals.Length > 0)) {
				result.Add(new("Evals must be specified for an eval-flag!"));
			}
		} else {
			if (string.IsNullOrEmpty(Off)) {
				result.Add(new("Off must be populated!"));
			}

			if (string.IsNullOrEmpty(On)) {
				result.Add(new("On must be populated!"));
			}

			_outputLookup = [Off + RESET, TransitionToOff + RESET, TransitionToOn + RESET, On + RESET];
		}

		State = InitialState;

		return result;
	}


	public string Process(string line) {
		if (Types.Any()) {
			var output = ProcessEval(line);
			foreach (Eval eval in Evals) {
				eval.SetLastOutput(output);
			}

			return output + RESET;
		}


		if (OffMatch?.IsMatch(line) ?? false) {
			State = FlagState.Off;
		}

		if (OnMatch?.IsMatch(line) ?? false) {
			State = FlagState.On;
		}

		if (TransitionToOffMatch?.IsMatch(line) ?? false) {
			State = FlagState.TransitioningOff;
		}

		if (TransitionToOnMatch?.IsMatch(line) ?? false) {
			State = FlagState.TransitioningOn;
		}

		if (ToggleMatch?.IsMatch(line) ?? false) {
			// ReSharper disable once BitwiseOperatorOnEnumWithoutFlags	- this does what I want.
			State ^= FlagState.On;
		}

		return _outputLookup[(int)State];
	}

	private string? ProcessEval(string line) {
		if (!TransitionToOnMatch?.IsMatch(line) ?? false) {
			return _lastEvalOutput;
		}


		var newOutput = Evals
			.Select(eval => eval.Process(line, this))
			.ToList()		// Ensure all evals get all the data.
			.FirstOrDefault(output => output != null);

		if (newOutput == null || newOutput == _lastEvalOutput) {
			return _lastEvalOutput;
		}


		_lastEvalOutput = newOutput;
		OnStateChange?.Invoke(this, newFlagState: default);

		return _lastEvalOutput;
	}


	public string Reset() {
		State = InitialState;

		if (Types.Any()) {
			ResetEval();
		}

		return new('-', (_lastEvalOutput ?? "-").StripColourCodes().Length);
	}

	private void ResetEval() {
		Evals.ToList().ForEach(eval => eval.Reset());
	}


	public Flag Copy() {
		return new() {
			Name = Name,
			Off = Off,
			OffMatch = OffMatch,
			TransitionToOff = TransitionToOff,
			TransitionToOffMatch = TransitionToOffMatch,
			TransitionToOn = TransitionToOn,
			TransitionToOnMatch = TransitionToOnMatch,
			On = On,
			OnMatch = OnMatch,
			InitialState = InitialState,
			Types = Types,
			Defines = Defines,
			Consts = Consts,
			Fields = Fields,
			Properties = Properties,
			Methods = Methods,
			Evals = Evals,
			_outputLookup = _outputLookup,
			_state = State,
			_typesLookup = _typesLookup
		};
	}


	public override string ToString() {
		return $"{{{
			GetType().Name
		}: {
			Name
		}, <{
			"/".RCoalesce(OffMatch?.ToString().NullIfEmpty(), "/") ?? "null"
		}, <<{
			"/".RCoalesce(TransitionToOffMatch?.ToString().NullIfEmpty(), "/") ?? "null"
		}, >>{
			"/".RCoalesce(TransitionToOnMatch?.ToString().NullIfEmpty(), "/") ?? "null"
		}, >{
			"/".RCoalesce(OnMatch?.ToString().NullIfEmpty(), "/") ?? "null"
		}, ~{
			"/".RCoalesce(ToggleMatch?.ToString().NullIfEmpty(), "/") ?? "null"
		}}}";
	}
}
