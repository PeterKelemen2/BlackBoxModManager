using System;
using System.Diagnostics;

namespace BlackboxModManager.Core.Store
{
	/// <summary>
	/// The step that an import runs at this moment.
	/// </summary>
	public enum ImportStage
	{
		/// <summary>The import reads the archive, or it copies the directory.</summary>
		Unpack,

		/// <summary>The import reads the files and decides what kind of mod they make.</summary>
		Inspect,

		/// <summary>The import moves the files into the mod store.</summary>
		Store,
	}

	/// <summary>
	/// One progress report of an import. The window shows this while the import runs.
	///
	/// <b>Total is 0 when the step cannot count its work.</b> A caller that draws a bar must
	/// test Total before it divides. Detail names the file that the step reads now, and it is
	/// empty when no single file fits the step.
	/// </summary>
	public sealed class ImportProgress
	{
		public ImportStage Stage { get; }

		/// <summary>The number of files that the step finished.</summary>
		public int Done { get; }

		/// <summary>The number of files that the step must do, or 0 when it does not know.</summary>
		public int Total { get; }

		/// <summary>The file that the step reads now, or an empty string.</summary>
		public string Detail { get; }

		public ImportProgress(ImportStage stage, int done = 0, int total = 0, string detail = null)
		{
			this.Stage = stage;
			this.Done = done;
			this.Total = total;
			this.Detail = detail ?? String.Empty;
		}
	}

	/// <summary>
	/// Sends the file reports of one stage, and it drops the reports that come too fast.
	///
	/// <b>A report costs a message to the window thread.</b> A zip of ten thousand small
	/// files writes faster than a window draws, and one report for each file then fills the
	/// message queue and freezes the window. This class lets the first file and the last file
	/// through, and it holds every other one to one report each <see cref="Gap"/>.
	///
	/// A null progress makes every call a no-op, so a caller needs no test of its own.
	/// </summary>
	internal sealed class StageReporter
	{
		/// <summary>The shortest time between two reports. This still reads as movement.</summary>
		private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(50);

		private readonly IProgress<ImportProgress> _progress;
		private readonly ImportStage _stage;
		private readonly Stopwatch _clock = Stopwatch.StartNew();

		private TimeSpan _last = TimeSpan.Zero;

		public StageReporter(IProgress<ImportProgress> progress, ImportStage stage)
		{
			this._progress = progress;
			this._stage = stage;
		}

		public void File(int done, int total, string name)
		{
			if (this._progress is null) return;

			TimeSpan now = this._clock.Elapsed;

			if (done > 1 && done != total && now - this._last < Gap) return;

			this._last = now;
			this._progress.Report(new ImportProgress(this._stage, done, total, name));
		}
	}
}
