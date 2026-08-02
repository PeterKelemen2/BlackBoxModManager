using System;
using System.IO;
using BlackboxModManager.Core.Games;
using Nikki.Core;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// A throwaway directory tree that passes the Underground 2 validator.
	///
	/// The files hold text and not container data. Every test that uses this class reads
	/// paths and hashes, never container content, so it runs on native Linux with no Wine
	/// and no game.
	/// </summary>
	internal sealed class FakeGame : IDisposable
	{
		/// <summary>The parent of the install. The workspace goes here too.</summary>
		public string Parent { get; }

		public string Root { get; }

		public FakeGame(string directoryName = "Need for Speed Underground 2")
		{
			this.Parent = Path.Combine(Path.GetTempPath(), $"game-test-{Guid.NewGuid():N}");
			this.Root = Path.Combine(this.Parent, directoryName);

			Directory.CreateDirectory(this.Root);

			this.Write("SPEED2.EXE", "the game");
			this.Write("server.dll", "a library");
			this.Write("GLOBAL/GLOBALA.BUN", "container a");

			// The name on the disk differs from the name in the manifests. This is real.
			this.Write("GLOBAL/GlobalB.lzc", "container b");

			// Binary leaves these behind. A snapshot must ignore them.
			this.Write("GLOBAL/GLOBALA.BUN.bacc", "the backup of another tool");

			this.Write("CARS/car.bin", "a car");
			this.Write("TRACKS/track.bin", "a track");
			this.Write("FRONTEND/front.bin", "a menu");

			// A read-only file. A real install holds one, and a copy of it blocks a delete.
			string readOnly = Path.Combine(this.Root, "server.dll");
			new FileInfo(readOnly).IsReadOnly = true;
		}

		public GameInstall Install()
		{
			GameInstallStatus status = GameInstallValidator.Validate(GameINT.Underground2, this.Root);

			if (!status.IsUsable) throw new InvalidOperationException(status.Message);

			return status.Install;
		}

		public void Write(string relative, string content)
		{
			string full = Path.Combine(this.Root, relative.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(full));
			File.WriteAllText(full, content);
		}

		public string Read(string relative)
		{
			return File.ReadAllText(Path.Combine(this.Root, relative.Replace('/', Path.DirectorySeparatorChar)));
		}

		public bool Has(string relative)
		{
			return File.Exists(Path.Combine(this.Root, relative.Replace('/', Path.DirectorySeparatorChar)));
		}

		public void Dispose()
		{
			try
			{
				Core.Files.FileTree.Delete(this.Parent);
			}
			catch (Exception)
			{
				// A leftover temporary directory does not fail a test run.
			}
		}
	}
}
