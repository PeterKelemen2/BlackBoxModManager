using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BlackboxModManager.Core.Mods;

namespace BlackboxModManager.Core.Profiles
{
	/// <summary>
	/// One mod inside a profile.
	///
	/// The entry names the mod by its store identifier. It never holds a path, because the
	/// store owns the layout and a profile must survive a move of the store.
	/// </summary>
	public sealed class ProfileEntry
	{
		/// <summary>The store identifier of the mod.</summary>
		public string ModId { get; set; }

		public bool Enabled { get; set; }

		/// <summary>
		/// What the user chose for each variant of the mod. This stays empty for an ASI mod
		/// and for a loose-file mod, because neither asks a question.
		///
		/// Step 6 reads this. It lives here because a profile must fully determine the
		/// result of a deploy, with no prompt.
		/// </summary>
		public ModSelections Selections { get; set; } = new ModSelections();

		public ProfileEntry() { }

		public ProfileEntry(string modId, bool enabled)
		{
			this.ModId = modId;
			this.Enabled = enabled;
		}
	}

	/// <summary>
	/// One named set of mods for one game.
	///
	/// <b>A profile fully determines the deployed result.</b> It holds the enabled set, the
	/// load order, and every option answer. A deploy reads a profile and asks the user
	/// nothing. Any question that a deploy would have to ask belongs in this file instead.
	///
	/// The order of Entries is the load order. A later entry overrides an earlier one. Do
	/// not add a separate priority number. One list, one order, one meaning.
	/// </summary>
	public sealed class Profile
	{
		/// <summary>The shape of the file. Raise this when a change needs a migration.</summary>
		public int Version { get; set; } = 1;

		/// <summary>The name that the UI shows. The file name comes from this value.</summary>
		public string Name { get; set; }

		/// <summary>The GameINT name of the game that this profile belongs to.</summary>
		public string Game { get; set; }

		/// <summary>
		/// The mods, in load order. The first entry applies first. The last entry wins a
		/// collision.
		/// </summary>
		public List<ProfileEntry> Entries { get; set; } = new List<ProfileEntry>();

		[JsonIgnore]
		public int EnabledCount
		{
			get
			{
				int count = 0;

				foreach (ProfileEntry entry in this.Entries)
				{
					if (entry.Enabled) ++count;
				}

				return count;
			}
		}

		public Profile() { }

		public Profile(string name, string game)
		{
			this.Name = name;
			this.Game = game;
		}

		public ProfileEntry Find(string modId)
		{
			if (String.IsNullOrWhiteSpace(modId)) return null;

			foreach (ProfileEntry entry in this.Entries)
			{
				if (String.Equals(entry.ModId, modId, StringComparison.OrdinalIgnoreCase)) return entry;
			}

			return null;
		}

		/// <summary>
		/// Returns the entry of one mod and adds one at the end of the order when it is
		/// absent. A new entry starts disabled.
		/// </summary>
		public ProfileEntry Ensure(string modId)
		{
			if (String.IsNullOrWhiteSpace(modId)) throw new ArgumentException("The mod identifier is empty.", nameof(modId));

			ProfileEntry entry = this.Find(modId);

			if (entry is null)
			{
				entry = new ProfileEntry(modId, false);
				this.Entries.Add(entry);
			}

			return entry;
		}

		public void Remove(string modId)
		{
			ProfileEntry entry = this.Find(modId);

			if (entry != null) this.Entries.Remove(entry);
		}

		/// <summary>
		/// Moves one mod by one position in the load order. Pass a negative offset to move
		/// it earlier. A move past either end does nothing.
		/// </summary>
		public bool Move(string modId, int offset)
		{
			ProfileEntry entry = this.Find(modId);

			if (entry is null) return false;

			int from = this.Entries.IndexOf(entry);
			int to = from + offset;

			if (to < 0 || to >= this.Entries.Count || to == from) return false;

			this.Entries.RemoveAt(from);
			this.Entries.Insert(to, entry);

			return true;
		}

		/// <summary>
		/// Drops every entry whose mod left the store, and adds an entry for every mod that
		/// the store holds and the profile does not. It returns true when it changed
		/// something.
		///
		/// Call this after the store changes. A profile that names a mod that no longer
		/// exists must not fail a deploy.
		/// </summary>
		public bool Reconcile(IEnumerable<string> storeModIds)
		{
			if (storeModIds is null) throw new ArgumentNullException(nameof(storeModIds));

			// Read the sequence once. The adds below keep the order that the caller gave.
			var ordered = new List<string>(storeModIds);
			var known = new HashSet<string>(ordered, StringComparer.OrdinalIgnoreCase);
			bool changed = false;

			for (int i = this.Entries.Count - 1; i >= 0; --i)
			{
				if (known.Contains(this.Entries[i].ModId)) continue;

				this.Entries.RemoveAt(i);
				changed = true;
			}

			foreach (string id in ordered)
			{
				if (this.Find(id) != null) continue;

				this.Entries.Add(new ProfileEntry(id, false));
				changed = true;
			}

			return changed;
		}

		/// <summary>
		/// Returns the enabled mod identifiers in load order.
		/// </summary>
		public IReadOnlyList<string> EnabledInOrder()
		{
			var found = new List<string>();

			foreach (ProfileEntry entry in this.Entries)
			{
				if (entry.Enabled) found.Add(entry.ModId);
			}

			return found;
		}

		public override string ToString() => $"{this.Name} ({this.EnabledCount} of {this.Entries.Count} enabled)";
	}
}
