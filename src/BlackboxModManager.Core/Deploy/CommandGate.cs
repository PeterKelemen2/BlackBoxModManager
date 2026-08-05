using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Mods;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Stops a deploy that would run a command that this application refuses, or that would
	/// write outside the staging copy.
	///
	/// <b>The gate runs inside the deploy engine and not only in the preflight.</b> The
	/// preflight tells the user what is wrong. The gate is the guarantee. A caller that skips
	/// the preflight must not be able to skip the rule.
	///
	/// The gate reads the scripts a second time. That costs one text parse for each variant,
	/// and it keeps the rule beside the code that writes.
	/// </summary>
	public static class CommandGate
	{
		/// <summary>
		/// Tests every enabled variant. It throws on the first variant that fails, and the
		/// message names the mod, the file, the line, and the command.
		/// </summary>
		public static void Check(IReadOnlyList<EnabledVariant> variants, string stagingDirectory,
			Action<string> log = null)
		{
			if (variants is null) throw new ArgumentNullException(nameof(variants));

			if (String.IsNullOrEmpty(stagingDirectory))
			{
				throw new ArgumentException("The staging directory is empty.", nameof(stagingDirectory));
			}

			Action<string> write = log ?? (line => { });

			var refused = new List<string>();
			var outside = new List<string>();
			int warned = 0;

			foreach (EnabledVariant variant in variants)
			{
				var roots = new SandboxRoots(stagingDirectory, variant.Variant.Manifest.ThisDir);

				// A script that this call cannot read stops the deploy on its own, further
				// down. Let the exception travel, because the engine names the variant.
				ResolvedScript resolved = ScriptFlattener.Resolve(variant.Variant, variant.Selection, roots);

				foreach (ResolvedEdit edit in resolved.Rejected)
				{
					refused.Add($"The mod \"{variant.Label}\" runs the command \"{edit.Verb}\" at " +
						$"{edit.Where}. This application does not run that command. {edit.Facts.Note}");
				}

				foreach ((ResolvedEdit Edit, PathEffect Path) escape in resolved.Escapes())
				{
					outside.Add($"The mod \"{variant.Label}\" runs the command \"{escape.Edit.Verb}\" at " +
						$"{escape.Edit.Where}, and that command leaves the staging copy. " +
						escape.Path.Violation);
				}

				warned += resolved.Warnings.Count;

				foreach (ScriptWarning warning in resolved.Warnings)
				{
					write($"  warning: {variant.Label}: {warning}");
				}
			}

			if (outside.Count > 0)
			{
				// Report this one first. A path outside staging reaches the real system, and
				// the revert never undoes it.
				throw new DeployServiceException(
					$"{outside.Count} commands write outside the staging copy, so the deploy stopped " +
					$"before it changed anything. {String.Join(" ", outside)}");
			}

			if (refused.Count > 0)
			{
				throw new DeployServiceException(
					$"{refused.Count} commands need support that this application does not have, so the " +
					$"deploy stopped before it changed anything. {String.Join(" ", refused)}");
			}

			write($"The command gate read {variants.Count} variants. It refused nothing and it found " +
				$"{warned} commands that the conflict check cannot compare.");
		}
	}
}
