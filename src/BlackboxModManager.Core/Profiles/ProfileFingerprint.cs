using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;
using BlackboxModManager.Core.Mods;

namespace BlackboxModManager.Core.Profiles
{
	/// <summary>
	/// One short string that stands for everything in a profile that changes the deployed
	/// result.
	///
	/// A deploy stores this string in the workspace. The window reads it back and compares it
	/// against the profile of the moment. A difference means that the game directory no
	/// longer holds what the profile says, and the window then asks for a deploy.
	///
	/// <b>Only the enabled set reaches the fingerprint.</b> A disabled mod supplies no file,
	/// so its options and its place in the order change nothing. A user who switches a mod
	/// off and on again gets the same string back, and the window stops asking.
	///
	/// XxHash128 is not a cryptographic hash. This compares our own state against our own
	/// state. We do not defend against a crafted collision.
	/// </summary>
	public static class ProfileFingerprint
	{
		/// <summary>
		/// The fingerprint of a profile that enables nothing. A vanilla game directory holds
		/// that result, so the two match and the window asks for no deploy.
		/// </summary>
		public static string Vanilla { get; } = Of(new Profile());

		/// <summary>
		/// Returns the fingerprint of one profile as lowercase hexadecimal.
		/// </summary>
		public static string Of(Profile profile)
		{
			if (profile is null) throw new ArgumentNullException(nameof(profile));

			var text = new StringBuilder();

			// The load order is part of the result, so the entries go in their own order and
			// never in a sorted order. Every map below sorts, because the order of a
			// dictionary means nothing and must not change the answer.
			foreach (ProfileEntry entry in profile.Entries)
			{
				if (!entry.Enabled) continue;

				string id = Text(entry.ModId);

				text.Append("mod\u001f").Append(id).Append('\n');

				AppendRoute(text, profile, entry);
				AppendVariants(text, entry.Selections);
				AppendIni(text, entry);
			}

			AppendLoaders(text, profile);

			var hash = new XxHash128();
			hash.Append(Encoding.UTF8.GetBytes(text.ToString()));

			return Convert.ToHexStringLower(hash.GetCurrentHash());
		}

		/// <summary>
		/// Adds the route of one mod, and only when that route is not the default.
		///
		/// <b>A default value must add nothing.</b> Every profile that predates the route field
		/// runs the native route, and a line for it would change every stored fingerprint. The
		/// window would then ask every user for a deploy that changes no byte.
		/// </summary>
		private static void AppendRoute(StringBuilder text, Profile profile, ProfileEntry entry)
		{
			BinaryRoute route = profile.RouteOf(entry);

			if (route == BinaryRoute.Native) return;

			text.Append("  route").Append(route.ToString()).Append('\n');
		}

		private static void AppendVariants(StringBuilder text, ModSelections selections)
		{
			if (selections?.Variants is null) return;

			foreach (string variant in Sorted(selections.Variants.Keys))
			{
				VariantSelection selection = selections.Variants[variant];

				// A variant that the profile knows and the user switched off applies nothing.
				if (selection is null || !selection.Enabled) continue;

				text.Append("  variant\u001f").Append(Text(variant)).Append('\n');

				if (selection.Answers is null) continue;

				var ordinals = new List<int>(selection.Answers.Keys);
				ordinals.Sort();

				foreach (int ordinal in ordinals)
				{
					text.Append("    answer\u001f").Append(ordinal).Append('\u001f')
						.Append(selection.Answers[ordinal] ?? String.Empty).Append('\n');
				}
			}
		}

		private static void AppendIni(StringBuilder text, ProfileEntry entry)
		{
			if (entry.IniSettings is null) return;

			foreach (string file in Sorted(entry.IniSettings.Keys))
			{
				Dictionary<string, string> answers = entry.IniSettings[file];

				if (answers is null || answers.Count == 0) continue;

				text.Append("  ini\u001f").Append(Text(file)).Append('\n');

				foreach (string key in Sorted(answers.Keys))
				{
					text.Append("    key\u001f").Append(key).Append('\u001f')
						.Append(answers[key] ?? String.Empty).Append('\n');
				}
			}
		}

		/// <summary>
		/// Adds the loader answers of the enabled mods.
		///
		/// A choice that names a mod which the profile does not enable places no file, so it
		/// stays out. Without that test a stale answer of a switched-off mod would ask the
		/// user for a deploy that changes nothing.
		/// </summary>
		private static void AppendLoaders(StringBuilder text, Profile profile)
		{
			if (profile.LoaderChoices is null) return;

			var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string id in profile.EnabledInOrder()) enabled.Add(id);

			foreach (string proxy in Sorted(profile.LoaderChoices.Keys))
			{
				string owner = profile.LoaderChoices[proxy];

				if (String.IsNullOrWhiteSpace(owner) || !enabled.Contains(owner)) continue;

				text.Append("loader\u001f").Append(Text(proxy)).Append('\u001f')
					.Append(Text(owner)).Append('\n');
			}
		}

		private static IReadOnlyList<string> Sorted(IEnumerable<string> keys)
		{
			var list = new List<string>(keys);
			list.Sort(StringComparer.Ordinal);

			return list;
		}

		/// <summary>
		/// One spelling for one name. A store identifier and a file path both compare without
		/// letter case everywhere else in this application, so they do here as well.
		/// </summary>
		private static string Text(string value) => (value ?? String.Empty).ToLowerInvariant();
	}
}
