using System;
using System.Threading;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// One lock for all library access.
	///
	/// The hash list properties are static, and LoadHashList calls Map.ReloadBinKeys, which
	/// resets global state in Nikki. Two profiles that load at the same time overwrite each
	/// other. Hold this gate for a whole deploy, from the static assignment through Load,
	/// the script run, and Save. See defect 8.
	///
	/// Run the deploy on one background thread. Never on several.
	/// </summary>
	public static class LibraryGate
	{
		private static readonly object Sync = new object();

		[ThreadStatic]
		private static int _depth;

		/// <summary>
		/// True when the calling thread holds the gate. Use this in an assertion, not in
		/// control flow.
		/// </summary>
		public static bool IsHeldByCurrentThread => _depth > 0;

		/// <summary>
		/// Takes the gate and releases it when the returned value is disposed.
		/// </summary>
		public static IDisposable Enter()
		{
			Monitor.Enter(Sync);
			++_depth;
			return new Scope();
		}

		/// <summary>
		/// Throws when the calling thread does not hold the gate. Call this at the top of
		/// any method that touches the library statics.
		/// </summary>
		public static void DemandHeld(string operation)
		{
			if (_depth > 0) return;

			throw new InvalidOperationException(
				$"The operation {operation} touches global library state. Take LibraryGate.Enter first.");
		}

		private sealed class Scope : IDisposable
		{
			private bool _done;

			public void Dispose()
			{
				if (this._done) return;

				this._done = true;
				--_depth;
				Monitor.Exit(Sync);
			}
		}
	}
}
