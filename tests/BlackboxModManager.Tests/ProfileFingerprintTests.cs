using BlackboxModManager.Core.Profiles;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the fingerprint that tells the window whether the game directory still holds
	/// what the profile says.
	///
	/// Two rules carry the whole feature. A change that reaches the deployed files changes
	/// the string. A change that reaches nothing leaves it alone. Every test below states
	/// one of those two.
	/// </summary>
	public class ProfileFingerprintTests
	{
		private static Profile WithTwoMods()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("alpha").Enabled = true;
			profile.Ensure("beta").Enabled = true;

			return profile;
		}

		[Fact]
		public void AProfileThatEnablesNothingMatchesTheVanillaFingerprint()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("alpha").Enabled = false;
			profile.Ensure("beta").Enabled = false;

			Assert.Equal(ProfileFingerprint.Vanilla, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void SwitchingAModOnChangesTheFingerprint()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("alpha").Enabled = false;

			string before = ProfileFingerprint.Of(profile);
			profile.Find("alpha").Enabled = true;

			Assert.NotEqual(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void SwitchingAModOffAndOnAgainGivesTheSameFingerprint()
		{
			Profile profile = WithTwoMods();
			string before = ProfileFingerprint.Of(profile);

			profile.Find("alpha").Enabled = false;
			profile.Find("alpha").Enabled = true;

			Assert.Equal(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void AddingAModChangesTheFingerprint()
		{
			Profile profile = WithTwoMods();
			string before = ProfileFingerprint.Of(profile);

			profile.Ensure("gamma").Enabled = true;

			Assert.NotEqual(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void AddingADisabledModLeavesTheFingerprintAlone()
		{
			Profile profile = WithTwoMods();
			string before = ProfileFingerprint.Of(profile);

			profile.Ensure("gamma").Enabled = false;

			Assert.Equal(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void TheLoadOrderOfTheEnabledModsChangesTheFingerprint()
		{
			Profile profile = WithTwoMods();
			string before = ProfileFingerprint.Of(profile);

			Assert.True(profile.Move("beta", -1));
			Assert.NotEqual(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void TheLoadOrderOfADisabledModLeavesTheFingerprintAlone()
		{
			Profile profile = WithTwoMods();
			profile.Ensure("off").Enabled = false;

			string before = ProfileFingerprint.Of(profile);

			Assert.True(profile.Move("off", -1));
			Assert.Equal(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void AnAnswerOfAnEnabledVariantChangesTheFingerprint()
		{
			Profile profile = WithTwoMods();
			ProfileEntry entry = profile.Find("alpha");
			entry.Selections.Ensure("Install").Enabled = true;

			string before = ProfileFingerprint.Of(profile);
			entry.Selections.Ensure("Install").Choose(0, "Wide");

			Assert.NotEqual(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void AnAnswerOfAVariantThatIsOffLeavesTheFingerprintAlone()
		{
			Profile profile = WithTwoMods();
			ProfileEntry entry = profile.Find("alpha");
			entry.Selections.Ensure("Install").Enabled = false;

			string before = ProfileFingerprint.Of(profile);
			entry.Selections.Ensure("Install").Choose(0, "Wide");

			Assert.Equal(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void ASettingsAnswerChangesTheFingerprint()
		{
			Profile profile = WithTwoMods();
			string before = ProfileFingerprint.Of(profile);

			profile.Find("alpha").SetIni("scripts/mod.ini", "MAIN/FpsLimit", "60", "0");

			Assert.NotEqual(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void ASettingsAnswerThatGoesBackToTheValueOfTheModLeavesTheFingerprintAlone()
		{
			Profile profile = WithTwoMods();
			string before = ProfileFingerprint.Of(profile);

			ProfileEntry entry = profile.Find("alpha");
			entry.SetIni("scripts/mod.ini", "MAIN/FpsLimit", "60", "0");
			entry.SetIni("scripts/mod.ini", "MAIN/FpsLimit", "0", "0");

			Assert.Equal(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void ALoaderChoiceOfAnEnabledModChangesTheFingerprint()
		{
			Profile profile = WithTwoMods();
			string before = ProfileFingerprint.Of(profile);

			profile.ChooseLoader("dinput8.dll", "alpha");

			Assert.NotEqual(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void ALoaderChoiceOfAModThatIsOffLeavesTheFingerprintAlone()
		{
			Profile profile = WithTwoMods();
			profile.Ensure("off").Enabled = false;

			string before = ProfileFingerprint.Of(profile);
			profile.ChooseLoader("dinput8.dll", "off");

			Assert.Equal(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void TheNameOfTheProfileDoesNotReachTheFingerprint()
		{
			Profile one = WithTwoMods();
			Profile two = WithTwoMods();
			two.Name = "Another name";

			Assert.Equal(ProfileFingerprint.Of(one), ProfileFingerprint.Of(two));
		}
	}
}
