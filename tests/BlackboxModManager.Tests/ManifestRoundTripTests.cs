using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Endscript.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Verification 1 of docs/roadmap/01-console-harness.md.
	///
	/// The VERSN1 manifest format is not standard JSON. It starts with a [VERSN1] tag, and
	/// it writes one backslash where JSON needs two. Launch.Deserialize doubles every
	/// backslash and Launch.Serialize halves them again. See defect 4.
	///
	/// A retarget or a library update can break that pair without any other symptom. This
	/// test reads each example manifest, writes it back, and compares the result.
	/// </summary>
	public class ManifestRoundTripTests
	{
		public static TheoryData<string> Manifests()
		{
			var data = new TheoryData<string>();

			foreach (string path in FindManifests()) data.Add(path);

			return data;
		}

		[ExampleModsFact]
		public void TheExampleModsHoldManifests()
		{
			// A broken path would make every theory below pass with zero cases.
			Assert.NotEmpty(FindManifests());
		}

		[ExampleModsTheory]
		[MemberData(nameof(Manifests))]
		public void AManifestSurvivesADeserializeAndSerializePair(string path)
		{
			Launch.Deserialize(path, out Launch launch);

			string temporary = Path.Combine(Path.GetTempPath(), $"versn1-{Guid.NewGuid():N}.end");

			try
			{
				Launch.Serialize(temporary, launch);

				byte[] original = File.ReadAllBytes(path);
				byte[] written = File.ReadAllBytes(temporary);

				// System.Text.Json ends a line with Environment.NewLine. The example files
				// hold CRLF, so a Linux run writes LF. That difference belongs to the
				// platform, not to the dialect. Compare with one line ending on both sides.
				Assert.Equal(Normalize(original), Normalize(written));
			}
			finally
			{
				if (File.Exists(temporary)) File.Delete(temporary);
			}
		}

		[ExampleModsTheory]
		[MemberData(nameof(Manifests))]
		public void AManifestKeepsOneBackslashPerSeparator(string path)
		{
			// This is the property that defect 4 puts at risk. A double backslash in the
			// output means Serialize did not undo what Deserialize did.
			Launch.Deserialize(path, out Launch launch);

			string temporary = Path.Combine(Path.GetTempPath(), $"versn1-{Guid.NewGuid():N}.end");

			try
			{
				Launch.Serialize(temporary, launch);

				string text = File.ReadAllText(temporary);

				Assert.DoesNotContain(@"\\", text);
				Assert.StartsWith("[VERSN1]", text, StringComparison.Ordinal);
			}
			finally
			{
				if (File.Exists(temporary)) File.Delete(temporary);
			}
		}

		private static byte[] Normalize(byte[] bytes)
		{
			string text = Encoding.UTF8.GetString(bytes);
			text = text.Replace("\r\n", "\n");
			return Encoding.UTF8.GetBytes(text);
		}

		private static List<string> FindManifests()
		{
			string root = ExampleMods.Root;
			var manifests = new List<string>();

			foreach (string path in Directory.EnumerateFiles(root, "*.end", SearchOption.AllDirectories))
			{
				if (IsVersion1(path)) manifests.Add(path);
			}

			manifests.Sort(StringComparer.Ordinal);
			return manifests;
		}

		private static bool IsVersion1(string path)
		{
			using var reader = new StreamReader(path);
			string first = reader.ReadLine();
			return first != null && first.StartsWith("[VERSN1]", StringComparison.Ordinal);
		}
	}
}
