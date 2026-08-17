using System;
using System.Collections.Generic;
using System.Diagnostics;
using Nikki.Utils;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Records how long each part of a deploy took, and writes one table at the end.
	///
	/// <b>Measure before you change anything here.</b> The roadmap holds two entries that first
	/// blamed the wrong code. Defect 17 blamed forced garbage collections, and a measurement
	/// showed that removing every one of them changed a 49,891 ms run into a 49,480 ms run.
	/// Defect 20 then found the real cost with a table like this one.
	///
	/// The counters of the native compressor cost a timestamp and four interlocked adds per
	/// call, and that path runs tens of thousands of times. They stay off unless the environment
	/// holds <c>BBMM_TIME_COMPRESSION=1</c>.
	/// </summary>
	public sealed class DeployTiming
	{
		/// <summary>Name of the environment variable that turns the compressor counters on.</summary>
		public const string CompressionSwitch = "BBMM_TIME_COMPRESSION";

		private readonly List<(string Name, int Milliseconds)> _spans = new List<(string, int)>();
		private readonly Dictionary<string, int> _containerMilliseconds =
			new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, int> _containerCalls =
			new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private readonly object _lock = new object();
		private readonly long _started = Stopwatch.GetTimestamp();

		/// <summary>
		/// True when the native compressor counts its calls for this run.
		/// </summary>
		public bool CountsCompression { get; }

		public DeployTiming()
		{
			this.CountsCompression = Environment.GetEnvironmentVariable(CompressionSwitch) == "1";

			if (this.CountsCompression)
			{
				Interop.ResetCompressionStats();
				Interop.CountCompression = true;
			}
		}

		/// <summary>
		/// Opens a span. Dispose the return value to close it.
		/// </summary>
		public IDisposable Measure(string name) => new Span(this, name);

		/// <summary>
		/// Records one container load or save. The caller is the progress hook of the profile,
		/// which runs on the task that did the work, so this method takes the lock.
		/// </summary>
		public void Container(string action, string name, int milliseconds)
		{
			string key = action + " " + name;

			lock (this._lock)
			{
				this._containerMilliseconds.TryGetValue(key, out int total);
				this._containerMilliseconds[key] = total + milliseconds;

				this._containerCalls.TryGetValue(key, out int calls);
				this._containerCalls[key] = calls + 1;
			}
		}

		private void Add(string name, int milliseconds)
		{
			lock (this._lock)
			{
				this._spans.Add((name, milliseconds));
			}
		}

		/// <summary>
		/// Writes the table and turns the compressor counters off again.
		/// </summary>
		public void Write(Action<string> log)
		{
			if (log is null) return;

			int total = (int)Stopwatch.GetElapsedTime(this._started).TotalMilliseconds;

			log($"Deploy timing. The whole deploy took {total} ms.");

			lock (this._lock)
			{
				foreach ((string Name, int Milliseconds) span in this._spans)
				{
					log($"  {span.Milliseconds,9} ms  {span.Name}");
				}

				if (this._containerMilliseconds.Count > 0)
				{
					log("  Containers, slowest first.");

					var rows = new List<KeyValuePair<string, int>>(this._containerMilliseconds);
					rows.Sort((left, right) => right.Value.CompareTo(left.Value));

					foreach (KeyValuePair<string, int> row in rows)
					{
						int calls = this._containerCalls[row.Key];
						string times = calls == 1 ? String.Empty : $" ({calls} times)";
						log($"  {row.Value,9} ms  {row.Key}{times}");
					}
				}
			}

			if (!this.CountsCompression) return;

			try
			{
				Interop.CompressionStat[] stats = Interop.CompressionStats();

				if (stats.Length == 0)
				{
					log("  The native compressor ran no call.");
					return;
				}

				log("  Native compressor, one row per codec.");

				foreach (Interop.CompressionStat stat in stats)
				{
					log($"  {(int)stat.Elapsed.TotalMilliseconds,9} ms  {stat.Type} " +
						$"{stat.Calls} calls, {Megabytes(stat.InputBytes)} MB in, " +
						$"{Megabytes(stat.OutputBytes)} MB out.");
				}
			}
			finally
			{
				Interop.CountCompression = false;
			}
		}

		private static string Megabytes(long bytes)
		{
			return (bytes / 1024d / 1024d).ToString("0.0");
		}

		private sealed class Span : IDisposable
		{
			private readonly DeployTiming _owner;
			private readonly string _name;
			private readonly long _stamp;
			private bool _closed;

			public Span(DeployTiming owner, string name)
			{
				this._owner = owner;
				this._name = name;
				this._stamp = Stopwatch.GetTimestamp();
			}

			public void Dispose()
			{
				if (this._closed) return;

				this._closed = true;
				this._owner.Add(this._name, (int)Stopwatch.GetElapsedTime(this._stamp).TotalMilliseconds);
			}
		}
	}
}
