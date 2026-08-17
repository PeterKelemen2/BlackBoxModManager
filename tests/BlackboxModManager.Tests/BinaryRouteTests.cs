using System.IO;
using BlackboxModManager.Core.Profiles;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers which code applies a Binary mod.
	///
	/// Two rules carry the feature. The entry of a mod decides, and the profile decides when
	/// the entry says Inherit. A default value must change no stored fingerprint, because
	/// every profile that predates the field runs the native route.
	/// </summary>
	public class BinaryRouteTests
	{
		private static Profile WithOneMod()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("alpha").Enabled = true;

			return profile;
		}

		[Fact]
		public void ANewProfileTakesTheNativeRoute()
		{
			Assert.Equal(BinaryRoute.Native, new Profile().BinaryRoute);
			Assert.Equal(BinaryRouteChoice.Inherit, new ProfileEntry().Route);
		}

		[Fact]
		public void AModWithNoChoiceFollowsTheProfile()
		{
			Profile profile = WithOneMod();
			profile.BinaryRoute = BinaryRoute.BinaryCli;

			Assert.Equal(BinaryRoute.BinaryCli, profile.RouteOf("alpha"));
		}

		[Fact]
		public void AModCanOverrideTheProfileInEitherDirection()
		{
			Profile profile = WithOneMod();

			profile.BinaryRoute = BinaryRoute.BinaryCli;
			profile.Find("alpha").Route = BinaryRouteChoice.Native;

			Assert.Equal(BinaryRoute.Native, profile.RouteOf("alpha"));

			profile.BinaryRoute = BinaryRoute.Native;
			profile.Find("alpha").Route = BinaryRouteChoice.BinaryCli;

			Assert.Equal(BinaryRoute.BinaryCli, profile.RouteOf("alpha"));
		}

		[Fact]
		public void AModThatTheProfileDoesNotHoldTakesTheRouteOfTheProfile()
		{
			Profile profile = WithOneMod();
			profile.BinaryRoute = BinaryRoute.BinaryCli;

			Assert.Equal(BinaryRoute.BinaryCli, profile.RouteOf("absent"));
		}

		/// <summary>
		/// The route changes the bytes that a deploy writes, so the window has to ask for a
		/// deploy after a change.
		/// </summary>
		[Fact]
		public void ChangingTheRouteOfTheProfileChangesTheFingerprint()
		{
			Profile profile = WithOneMod();
			string before = ProfileFingerprint.Of(profile);

			profile.BinaryRoute = BinaryRoute.BinaryCli;

			Assert.NotEqual(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void ChangingTheRouteOfOneModChangesTheFingerprint()
		{
			Profile profile = WithOneMod();
			string before = ProfileFingerprint.Of(profile);

			profile.Find("alpha").Route = BinaryRouteChoice.BinaryCli;

			Assert.NotEqual(before, ProfileFingerprint.Of(profile));
		}

		/// <summary>
		/// A choice that resolves to the native route is the state that every older profile
		/// file already holds. A line for it would ask every user for a deploy that changes no
		/// byte.
		/// </summary>
		[Fact]
		public void NamingTheNativeRouteLeavesTheFingerprintAlone()
		{
			Profile profile = WithOneMod();
			string before = ProfileFingerprint.Of(profile);

			profile.Find("alpha").Route = BinaryRouteChoice.Native;

			Assert.Equal(before, ProfileFingerprint.Of(profile));
		}

		[Fact]
		public void AnOverrideThatMatchesTheProfileGivesTheSameFingerprint()
		{
			Profile profile = WithOneMod();
			profile.BinaryRoute = BinaryRoute.BinaryCli;

			string before = ProfileFingerprint.Of(profile);

			profile.Find("alpha").Route = BinaryRouteChoice.BinaryCli;

			Assert.Equal(before, ProfileFingerprint.Of(profile));
		}

		// ---------------------------------------------------------------- persistence

		/// <summary>
		/// The route lives in the profile file, so it has to survive a restart.
		/// </summary>
		[Fact]
		public void TheRouteSurvivesASaveAndALoad()
		{
			using var temp = new TempDirectory();
			var store = new ProfileStore(Path.Combine(temp.Path, "profiles"));

			Profile saved = store.Ensure(GameINT.Underground2, "Test");
			saved.BinaryRoute = BinaryRoute.BinaryCli;
			saved.Ensure("alpha").Enabled = true;
			saved.Find("alpha").Route = BinaryRouteChoice.Native;

			store.Save(GameINT.Underground2, saved);

			Profile read = store.Find(GameINT.Underground2, "Test");

			Assert.Equal(BinaryRoute.BinaryCli, read.BinaryRoute);
			Assert.Equal(BinaryRouteChoice.Native, read.Find("alpha").Route);
			Assert.Equal(BinaryRoute.Native, read.RouteOf("alpha"));
		}

		/// <summary>
		/// A profile file is a file that a user reads. The route must appear as its name and not
		/// as a number.
		/// </summary>
		[Fact]
		public void TheProfileFileHoldsTheNameOfTheRoute()
		{
			using var temp = new TempDirectory();
			var store = new ProfileStore(Path.Combine(temp.Path, "profiles"));

			Profile profile = store.Ensure(GameINT.Underground2, "Test");
			profile.BinaryRoute = BinaryRoute.BinaryCli;

			store.Save(GameINT.Underground2, profile);

			string text = File.ReadAllText(
				Path.Combine(temp.Path, "profiles", nameof(GameINT.Underground2), "Test.json"));

			Assert.Contains("\"BinaryRoute\": \"BinaryCli\"", text);
		}

		/// <summary>
		/// A profile file that predates the route field must read as the native route, so its
		/// behavior does not change.
		/// </summary>
		[Fact]
		public void AProfileFileWithNoRouteFieldReadsAsTheNativeRoute()
		{
			using var temp = new TempDirectory();
			string directory = Path.Combine(temp.Path, "profiles", nameof(GameINT.Underground2));

			Directory.CreateDirectory(directory);
			File.WriteAllText(Path.Combine(directory, "Old.json"),
				"{\"Version\":1,\"Name\":\"Old\",\"Game\":\"Underground2\"," +
				"\"Entries\":[{\"ModId\":\"alpha\",\"Enabled\":true}]}");

			var store = new ProfileStore(Path.Combine(temp.Path, "profiles"));
			Profile read = store.Find(GameINT.Underground2, "Old");

			Assert.NotNull(read);
			Assert.Equal(BinaryRoute.Native, read.BinaryRoute);
			Assert.Equal(BinaryRouteChoice.Inherit, read.Find("alpha").Route);
		}

		/// <summary>
		/// A disabled mod supplies nothing, so its route reaches no file.
		/// </summary>
		[Fact]
		public void TheRouteOfADisabledModLeavesTheFingerprintAlone()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("alpha").Enabled = false;

			string before = ProfileFingerprint.Of(profile);

			profile.Find("alpha").Route = BinaryRouteChoice.BinaryCli;

			Assert.Equal(before, ProfileFingerprint.Of(profile));
		}
	}
}
