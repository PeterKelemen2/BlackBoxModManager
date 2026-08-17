using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Narrows the enabled variants of a deploy to the mods that one engine call names.
	///
	/// <b>A Binary engine must read this and never read DeployContext.Variants directly.</b>
	/// That list holds every enabled variant of the profile. Two engines now share the Binary
	/// kind, and each call gives an engine a part of the mods. An engine that reads the whole
	/// list would apply the mods of the other route as well, and every edit would run twice.
	/// </summary>
	public static class BinaryVariantScope
	{
		/// <summary>
		/// The enabled variants of the given mods, in load order.
		///
		/// It reads DeployContext.Variants when the caller built it, because a fresh read costs
		/// one text parse for each appended file and one real mod appends 158 files.
		/// </summary>
		public static IReadOnlyList<EnabledVariant> Of(DeployContext context,
			IReadOnlyList<InstalledMod> mods)
		{
			if (context is null) throw new ArgumentNullException(nameof(context));
			if (mods is null) throw new ArgumentNullException(nameof(mods));

			IReadOnlyList<EnabledVariant> all = context.Variants ?? VariantReader.Read(
				context.Profile, context.Store, context.Game.Game, context.Log);

			var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (InstalledMod mod in mods)
			{
				if (mod?.Id != null) wanted.Add(mod.Id);
			}

			var found = new List<EnabledVariant>(all.Count);

			foreach (EnabledVariant variant in all)
			{
				if (wanted.Contains(variant.Mod.Id)) found.Add(variant);
			}

			return found;
		}
	}
}
