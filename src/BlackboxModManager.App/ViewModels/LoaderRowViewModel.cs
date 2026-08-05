using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Asi;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BlackboxModManager.App.ViewModels
{
	/// <summary>
	/// One ASI loader file, with the mod that supplies it.
	///
	/// The game directory holds one file at each loader path, and several mods ship one each.
	/// Before step 9 the last mod of the load order won and no log line mentioned it.
	///
	/// <b>This row never picks a supplier.</b> A proxy DLL forwards to the real system library,
	/// and a version that forwards wrongly breaks sound or input rather than the plugin. Only
	/// the user knows which one their setup needs.
	/// </summary>
	public sealed class LoaderRowViewModel : ObservableObject
	{
		private readonly ProxyContest _contest;

		/// <summary>The loader file name, such as <c>dinput8.dll</c>.</summary>
		public string ProxyName => this._contest.ProxyName;

		public int CandidateCount => this._contest.Candidates.Count;

		/// <summary>True when the deploy needs an answer before it can run.</summary>
		public bool NeedsAnswer => this._contest.NeedsAnswer;

		public IReadOnlyList<ProxyCandidate> Candidates => this._contest.Candidates;

		/// <summary>The store identifier of the current supplier, or an empty string.</summary>
		public string SupplierId => this._contest.Supplier?.ModId ?? String.Empty;

		public LoaderRowViewModel(ProxyContest contest)
		{
			this._contest = contest ?? throw new ArgumentNullException(nameof(contest));
		}

		/// <summary>One line that names the supplier, or the reason that there is none.</summary>
		public string Supplier
		{
			get
			{
				ProxyCandidate winner = this._contest.Supplier;

				if (winner != null) return $"\"{winner.ModName}\", {winner.Identity.Describe()}";

				return this._contest.Reason.Length > 0
					? this._contest.Reason
					: $"{this.CandidateCount} mods supply this file and the profile names none of them. " +
						"Choose one, then deploy.";
			}
		}

		/// <summary>One line that says how many mods supply the file and what that means.</summary>
		public string Detail
		{
			get
			{
				if (this.CandidateCount == 1) return "One mod supplies this file. There is nothing to choose.";

				if (this._contest.AllSameFile)
				{
					return $"{this.CandidateCount} mods supply this file and every copy is the same file. " +
						"The choice changes nothing.";
				}

				return $"{this.CandidateCount} mods supply this file and the copies differ. " +
					"A change needs a new deploy.";
			}
		}

		/// <summary>The rows of the choice dialog, one per candidate.</summary>
		public IReadOnlyList<Views.UserChoice> Choices()
		{
			var choices = new List<Views.UserChoice>(this.CandidateCount + 1);

			foreach (ProxyCandidate candidate in this._contest.Candidates)
			{
				choices.Add(new Views.UserChoice(candidate.ModId,
					$"{candidate.ModName} — load order {candidate.Order}",
					$"{candidate.RelativePath}, {candidate.Bytes} bytes, {candidate.Identity.Describe()}"));
			}

			// The way back. An empty key clears the stored answer, and the next deploy asks.
			choices.Add(new Views.UserChoice(String.Empty, "Ask me again",
				"Clear the stored answer. The next deploy stops and asks for one."));

			return choices;
		}

		public override string ToString() => $"{this.ProxyName}: {this.Supplier}";
	}
}
