using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Files;

namespace BlackboxModManager.Core.Store
{
	/// <summary>
	/// Thrown when a mod store cannot move. The store stays where it was.
	/// </summary>
	public sealed class ModStoreMoveException : Exception
	{
		public ModStoreMoveException(string message, Exception inner = null) : base(message, inner) { }
	}

	/// <summary>
	/// What one move of the mod store did.
	/// </summary>
	public sealed class ModStoreMoveReport
	{
		public string From { get; }

		public string To { get; }

		/// <summary>The identifier of every mod that reached the new store.</summary>
		public IReadOnlyList<string> Moved { get; }

		/// <summary>
		/// The mods that stayed behind, with the reason. A mod whose identifier the target
		/// already holds lands here, and so does one that this application could not read.
		/// </summary>
		public IReadOnlyList<string> Kept { get; }

		/// <summary>True when the move copied every byte instead of renaming a directory.</summary>
		public bool CrossedVolume { get; }

		public ModStoreMoveReport(string from, string to, IReadOnlyList<string> moved,
			IReadOnlyList<string> kept, bool crossedVolume)
		{
			this.From = from;
			this.To = to;
			this.Moved = moved ?? Array.Empty<string>();
			this.Kept = kept ?? Array.Empty<string>();
			this.CrossedVolume = crossedVolume;
		}

		public string Summary()
		{
			string how = this.CrossedVolume
				? "The two directories sit on different volumes, so the move copied every byte."
				: "The move renamed each directory, so it cost no disk space.";

			string tail = this.Kept.Count == 0
				? String.Empty
				: $" {this.Kept.Count} mods stayed at {this.From}.";

			return $"The mod store moved {this.Moved.Count} mods to {this.To}. {how}{tail}";
		}
	}

	/// <summary>
	/// Moves the mod store to another directory.
	///
	/// <b>The volume of the store decides the cost of every deploy.</b> A hard link cannot
	/// cross a volume. A store on the volume of the game gets hard links, and a deploy then
	/// costs almost no disk space. A store on another volume falls through to Copy, and every
	/// deploy writes every byte of every mod. Under Wine there is no cheaper method in
	/// between, because Wine writes a symbolic link as a zero-byte file. See step 9.
	///
	/// <b>One mod at a time.</b> Each mod of the store is a self-contained directory, so a
	/// failure halfway through leaves both stores readable. Nothing is deleted until its copy
	/// exists at the target.
	/// </summary>
	public static class ModStoreRelocator
	{
		/// <summary>
		/// Tests whether a directory can hold the mod store. It returns an empty string when it
		/// can, and the reason when it cannot.
		///
		/// Call this before you ask the user to confirm anything.
		/// </summary>
		public static string Problem(string target, string gameRoot = null)
		{
			if (String.IsNullOrWhiteSpace(target)) return "The directory is empty.";

			string full;

			try
			{
				full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
			}
			catch (Exception ex)
			{
				return $"This application cannot read that path. {ex.Message}";
			}

			// A game reinstall deletes its own directory, and that would take the library of
			// the user with it.
			if (!String.IsNullOrEmpty(gameRoot) && FileTree.IsSameOrInside(full, gameRoot))
			{
				return $"The directory {full} sits inside the game install. A game reinstall would " +
					"delete the mod library. Choose a directory outside the game.";
			}

			// The workspace of a game is rebuilt on every deploy, and the staging build deletes
			// the whole directory first.
			if (full.Contains(Staging.GameWorkspace.WorkspaceSuffix, StringComparison.OrdinalIgnoreCase))
			{
				return $"The directory {full} sits inside a workspace of this application. A deploy " +
					"deletes that directory and rebuilds it. Choose another directory.";
			}

			try
			{
				Directory.CreateDirectory(full);

				// A read-only volume answers here and nowhere else.
				string probe = Path.Combine(full, $".blackbox-store-probe-{Guid.NewGuid():N}");

				File.WriteAllText(probe, "probe");
				File.Delete(probe);
			}
			catch (Exception ex)
			{
				return $"This application cannot write to {full}. {ex.Message}";
			}

			return String.Empty;
		}

		/// <summary>
		/// Moves every mod of one store into another directory.
		///
		/// It returns a report and it never leaves a mod in two places. A mod whose identifier
		/// the target already holds stays where it is, and the report names it. Pass the game
		/// root so that the check can refuse a directory inside the install.
		/// </summary>
		public static ModStoreMoveReport Move(ModStore from, string target, string gameRoot = null,
			Action<string> log = null)
		{
			if (from is null) throw new ArgumentNullException(nameof(from));

			Action<string> write = log ?? (line => { });

			string problem = Problem(target, gameRoot);

			if (problem.Length > 0) throw new ModStoreMoveException(problem);

			string to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));

			if (FileTree.IsSameOrInside(to, from.Root) && FileTree.IsSameOrInside(from.Root, to))
			{
				throw new ModStoreMoveException(
					$"The mod store already sits at {to}. There is nothing to move.");
			}

			// A target inside the store, or a store inside the target, would move a directory
			// into itself.
			if (FileTree.IsSameOrInside(to, from.Root) || FileTree.IsSameOrInside(from.Root, to))
			{
				throw new ModStoreMoveException(
					$"The directory {to} and the mod store {from.Root} overlap. A move needs two " +
					"separate directories.");
			}

			IReadOnlyList<InstalledMod> mods = from.List();
			var moved = new List<string>();
			var kept = new List<string>();
			bool crossed = false;

			write($"Move {mods.Count} mods from {from.Root} to {to}.");

			foreach (InstalledMod mod in mods)
			{
				string source = mod.Root;
				string destination = Path.Combine(to, Path.GetFileName(source));

				if (Directory.Exists(destination))
				{
					kept.Add($"\"{mod.Name}\": the directory {destination} already exists.");
					write($"  {mod.Name} stays behind. {destination} already exists.");
					continue;
				}

				try
				{
					if (MoveOne(source, destination)) crossed = true;

					moved.Add(mod.Id);
					write($"  {mod.Name}");
				}
				catch (Exception ex)
				{
					// The mod is still readable at the old place, because MoveOne deletes the
					// source only after the copy finishes.
					kept.Add($"\"{mod.Name}\": {ex.Message}");
					write($"  {mod.Name} stays behind. {ex.Message}");
				}
			}

			var report = new ModStoreMoveReport(from.Root, to, moved, kept, crossed);

			write(report.Summary());

			return report;
		}

		/// <summary>
		/// Moves one mod directory. It returns true when it had to copy every byte.
		///
		/// A rename is instant and it works on one volume only. Across volumes it copies and
		/// then deletes the source, so a failure during the copy loses nothing.
		/// </summary>
		private static bool MoveOne(string source, string destination)
		{
			try
			{
				Directory.Move(source, destination);

				return false;
			}
			catch (IOException)
			{
				// Directory.Move throws for a move across volumes. Copy instead.
			}

			CopyTree(source, destination);

			// The copy is complete, so the source is now the spare.
			FileTree.Delete(source);

			return true;
		}

		private static void CopyTree(string source, string destination)
		{
			Directory.CreateDirectory(destination);

			foreach (string relative in FileTree.Files(source))
			{
				string target = FileTree.Combine(destination, relative);

				FileTree.CreateParent(target);
				File.Copy(FileTree.Combine(source, relative), target, true);
			}
		}
	}
}
