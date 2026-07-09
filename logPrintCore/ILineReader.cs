using System;
using System.IO;
using System.Text;

using logPrintCore.Utils;

namespace logPrintCore;

internal interface ILineReader : IDisposable {
	string? GetNextLine(TimeSpan timeout, int sleep = 100);

	string? ReadNextLine(TextReader reader) {
		// Read up to & including the next \r\n, \r, or \n, returning null if at the end of the stream, or if the last line doesn't end in a linebreak then return the partial line.
		var builder = new StringBuilder();
		int ch;

		while ((ch = reader.Read()) != -1) {
			builder.Append((char)ch);

			switch (ch) {
				case '\r': {
					int next = reader.Peek();
					if (next == '\n') {
						builder.Append((char)reader.Read());
					}

					return builder.ToStringAndClear();
				}

				case '\n':
					return builder.ToStringAndClear();
			}
		}


		// EOF:
		return builder.Length > 0
			? builder.ToStringAndClear()
			: null;
	}
}
