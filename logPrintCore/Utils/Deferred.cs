using System;

namespace logPrintCore.Utils;

/// <inheritdoc />
/// <summary>Runs the provided <see cref="T:System.Action" /> when it is <see cref="M:logPrintCore.Utils.Deferred.Dispose" />d.</summary>
// ReSharper disable once UnusedType.Global
public sealed class Deferred : IDisposable {
	private readonly Action _action;


	private Deferred(Action action) {
		_action = action;
	}


	public void Dispose() {
		_action();
	}


	/// <summary>Runs the provided <see cref="Action"/> when the return value is <see cref="Dispose"/>d.</summary>
	/// <param name="action">The <see cref="Action"/> to run.</param>
	/// <returns>The (reusable) <see cref="IDisposable"/> which will run the <paramref name="action"/> when it is disposed.</returns>
	/// <example><code>
	/// 	{
	/// 		...
	/// 		// Allocate a thing.
	/// 		using var _ = Deferred.Defer(
	/// 			() => // Free the thing.  Write this right next to the allocation, so we don't forget.
	/// 		);
	/// 		// Use the thing...
	/// 		...
	/// 		// End of using scope; the thing will be freed next.
	/// 	}
	/// </code></example>
	/// <remarks>Note that this is reusable - you can hold a reference and <code>using</code> it as many times as you want.</remarks>
	/// <example><code>
	/// 	_postReturnAction = Deferred.Defer(() => _buffer.Clear());
	///
	/// 	...
	///
	/// 	using (_postReturnAction) {
	/// 		return _buffer.ToString();	// Return the current value, THEN clear it for next time.
	/// 	}
	/// </code></example>
	// ReSharper disable once UnusedMember.Global
	public static Deferred Defer(Action action) {
		return new(action);
	}
}
