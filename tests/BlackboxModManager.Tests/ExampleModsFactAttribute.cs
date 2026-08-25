using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// A Fact that needs the example mods. The test reports as skipped when the example_mods
	/// directory is absent.
	///
	/// xUnit 2 has no Assert.Skip. A test decides to skip at run time, and the runner reads
	/// the decision from the Skip property of the attribute. Setting that property in the
	/// constructor is the way that xUnit 2 supports.
	///
	/// See <see cref="ExampleMods"/> for why a clean checkout holds no example mod.
	/// </summary>
	public sealed class ExampleModsFactAttribute : FactAttribute
	{
		public ExampleModsFactAttribute()
		{
			if (!ExampleMods.Exists) this.Skip = ExampleMods.Absent;
		}
	}

	/// <summary>
	/// A Theory that needs the example mods. This is the Theory counterpart of
	/// <see cref="ExampleModsFactAttribute"/>.
	/// </summary>
	public sealed class ExampleModsTheoryAttribute : TheoryAttribute
	{
		public ExampleModsTheoryAttribute()
		{
			if (!ExampleMods.Exists) this.Skip = ExampleMods.Absent;
		}
	}
}
