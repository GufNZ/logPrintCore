using System;
using System.Text.RegularExpressions;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace logPrintCore.Utils;

internal sealed class RegexFromStringConverter : IYamlTypeConverter {
	private readonly bool _debug;


	internal RegexFromStringConverter(bool debug = false) {
		_debug = debug;
	}


	/// <inheritdoc />
	public bool Accepts(Type type) {
		return (type == typeof(Regex));
	}


	/// <inheritdoc />
	public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
		var scalar = parser.Consume<Scalar>();
		if (_debug) {
			scalar.Dump("scalar :: " + type);
		}

		return (type == typeof(Regex))
			? new Regex(scalar.Value.Replace("\\e", "\e"))
			: scalar;
	}

	/// <inheritdoc />
	public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer) {
		throw new NotSupportedException();
	}
}
