using System;

namespace logPrintCore;

internal interface ILineReader : IDisposable {
	string? GetNextLine(TimeSpan timeout, int sleep = 100);
}
