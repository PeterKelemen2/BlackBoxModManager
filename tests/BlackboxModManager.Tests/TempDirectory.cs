using System;
using System.IO;
using System.Text;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// A throwaway directory that holds a hand-built mod. Use this for the cases that the
	/// example mods do not cover, such as a broken manifest or an append loop.
	/// </summary>
	internal sealed class TempDirectory : IDisposable
	{
		public string Path { get; }

		public TempDirectory()
		{
			this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mod-test-{Guid.NewGuid():N}");
			Directory.CreateDirectory(this.Path);
		}

		public void Dispose()
		{
			if (Directory.Exists(this.Path)) Directory.Delete(this.Path, true);
		}

		public string File(string name) => System.IO.Path.Combine(this.Path, name);

		/// <summary>
		/// Writes a VERSN1 manifest in the dialect that Binary emits. It holds one
		/// backslash where standard JSON needs two.
		///
		/// Name the containers to put in the Files list. An empty list gives one container,
		/// which is what most tests need.
		/// </summary>
		public void WriteManifest(string name, string game, string script, params string[] files)
		{
			string[] chosen = files is null || files.Length == 0
				? new[] { @"GLOBAL\GLOBALB.LZC" }
				: files;

			var text = new StringBuilder();
			text.AppendLine("[VERSN1]");
			text.AppendLine();
			text.AppendLine("{");
			text.AppendLine("  \"Usage\": \"User\",");
			text.AppendLine($"  \"Game\": \"{game}\",");
			text.AppendLine("  \"Directory\": \"\",");
			text.AppendLine($"  \"Endscript\": \"{script}\",");
			text.AppendLine("  \"Files\": [");

			for (int i = 0; i < chosen.Length; ++i)
			{
				string comma = i + 1 < chosen.Length ? "," : String.Empty;

				text.AppendLine($"    \"{chosen[i]}\"{comma}");
			}

			text.AppendLine("  ],");
			text.AppendLine("  \"Links\": []");
			text.AppendLine("}");

			WriteAll(name, text.ToString());
		}

		public void WriteScript(string name, params string[] lines)
		{
			var text = new StringBuilder();
			text.AppendLine("[VERSN2]");

			foreach (string line in lines) text.AppendLine(line);

			WriteAll(name, text.ToString());
		}

		private void WriteAll(string name, string text)
		{
			string full = this.File(name);
			string parent = System.IO.Path.GetDirectoryName(full);

			if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

			System.IO.File.WriteAllText(full, text);
		}
	}
}
