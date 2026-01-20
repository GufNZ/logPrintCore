using System;
using System.ComponentModel.DataAnnotations;

using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace logPrintCore.Utils;

internal sealed class ValidatingNodeDeserializer : INodeDeserializer {
	private readonly INodeDeserializer _nodeDeserializer;
	private readonly bool _debug;


	internal ValidatingNodeDeserializer(INodeDeserializer nodeDeserializer, bool debug = false) {
		_nodeDeserializer = nodeDeserializer;
		_debug = debug;
	}


	public bool Deserialize(IParser parser, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value) {
		if (_debug) {
			expectedType.Dump(nameof(expectedType));
		}

		bool deserialized = _nodeDeserializer.Deserialize(parser, expectedType, nestedObjectDeserializer, out value);
		if (_debug) {
			deserialized.Dump(nameof(deserialized), multiLine: true, recurseFilter: (_, _) => true);
			value.Dump(nameof(value), multiLine: true, recurseFilter: (_, _) => true);
		}

		if (deserialized && value != null) {
			var context = new ValidationContext(value, null, null);
			Validator.ValidateObject(value, context, true);

			if (_debug) {
				true.Dump("Valid");
			}

			return true;
		}


		if (_debug) {
			false.Dump("Invalid; deserialized = " + deserialized);
		}

		return false;
	}
}
