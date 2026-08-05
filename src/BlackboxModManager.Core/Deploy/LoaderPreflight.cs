using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Asi;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Settles which mod supplies each ASI loader file, before the deploy writes anything.
	///
	/// <b>A deploy never asks a question.</b> A profile fully determines the result of a deploy,
	/// and that rule holds here too. So this class reads the answer of the profile, and it stops
	/// the deploy when the profile holds no answer for a contested loader. The window asks the
	/// user and stores the answer, then the deploy runs.
	///
	/// <b>Never pick a loader automatically.</b> A proxy DLL forwards to the real system
	/// library, and a version that forwards wrongly breaks sound or input rather than the
	/// plugin. Only the user knows which one their setup needs.
	/// </summary>
	public static class LoaderPreflight
	{
		/// <summary>
		/// Builds the plan for the enabled mods of a profile. It writes nothing, so the window
		/// can call it whenever the selection changes.
		/// </summary>
		public static ProxyPlan Plan(Profile profile, IReadOnlyList<InstalledMod> mods,
			IReadOnlySet<string> proxyNames = null)
		{
			if (profile is null) throw new ArgumentNullException(nameof(profile));
			if (mods is null) throw new ArgumentNullException(nameof(mods));

			return ProxyScanner.Scan(mods, profile.LoaderChoices, proxyNames);
		}

		/// <summary>
		/// Reports the plan and stops the deploy when a contest has no answer.
		///
		/// It returns the log lines that name the winner of each loader and every mod whose copy
		/// the deploy skips. Before this step the last mod of the load order won and no line
		/// mentioned it.
		/// </summary>
		public static IReadOnlyList<LoaderChoice> Settle(ProxyPlan plan, Action<string> log = null)
		{
			if (plan is null) throw new ArgumentNullException(nameof(plan));

			Action<string> write = log ?? (line => { });

			foreach (string note in plan.Unmanaged) write(note);

			var open = new List<string>();

			foreach (ProxyContest contest in plan.Open)
			{
				var names = new List<string>(contest.Candidates.Count);

				foreach (ProxyCandidate candidate in contest.Candidates) names.Add(candidate.Describe());

				string reason = contest.Reason.Length > 0 ? $" {contest.Reason}" : String.Empty;

				open.Add($"{contest.Candidates.Count} mods supply {contest.ProxyName} and the profile " +
					$"names none of them.{reason} The candidates are {String.Join("; ", names)}.");
			}

			if (open.Count > 0)
			{
				throw new DeployServiceException(
					"The deploy needs to know which mod supplies each ASI loader, so it stopped before " +
					"it changed anything. Open the loader row of the window and choose. " +
					String.Join(" ", open));
			}

			var choices = new List<LoaderChoice>();

			foreach (ProxyContest contest in plan.Contests)
			{
				ProxyCandidate winner = contest.Supplier;

				if (winner is null) continue;

				var skipped = new List<string>();

				foreach (ProxyCandidate loser in contest.Skipped) skipped.Add($"\"{loser.ModName}\"");

				var choice = new LoaderChoice(contest.ProxyName, winner.ModId, winner.ModName,
					winner.Identity.Describe(), skipped);

				choices.Add(choice);
				write(choice.ToString());

				if (contest.AllSameFile)
				{
					write($"  Every mod that supplies {contest.ProxyName} holds the same file, " +
						$"hash {winner.Identity.ShortHash}. The choice changes nothing.");
				}
			}

			return choices;
		}
	}
}
