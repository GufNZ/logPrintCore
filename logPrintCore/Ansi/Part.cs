#if DEBUG
//#define DEBUG_ASSEMBLY
#endif

using System.IO;

namespace logPrintCore.Ansi;

internal abstract class Part {
#if DEBUG_ASSEMBLY
	private static uint nextID;
	private readonly uint _id = ++nextID;
#endif

	public abstract bool MergeWith(Part previous, out Part merged);

	public abstract string ToAnsi();

	public abstract void ToConsole(TextWriter writer);


	public override string ToString() {
#if DEBUG_ASSEMBLY
		return $"{{{GetType().Name}#{_id:000}: {DebugOutput()}}}";
#else
		return $"{{{GetType().Name}: {DebugOutput()}}}";
#endif
	}

	protected abstract string DebugOutput();
}
