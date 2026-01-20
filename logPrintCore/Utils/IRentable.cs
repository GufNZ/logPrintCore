using System.Collections.Generic;

namespace logPrintCore.Utils;

public interface IRentable<T> {
	LinkedListNode<T>? Node { get; set; }
}
