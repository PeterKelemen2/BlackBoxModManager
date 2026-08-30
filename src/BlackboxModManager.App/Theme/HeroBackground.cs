namespace BlackboxModManager.App.Theme
{
	/// <summary>
	/// The three looks that the window can draw with the color image of the selected game.
	/// See Theme/HeroPalette.cs for the image itself, and docs/roadmap/18-hero-background.md
	/// for the reasoning.
	///
	/// The order of these names is the order that the settings window shows.
	///
	/// <b>The default is Corner, and the number of a value never says so.</b>
	/// Settings.HeroBackground holds a string, and a file of an older build holds no such
	/// key. MainViewModel.StoredHeroBackground answers Corner for every name that it cannot
	/// read, and Corner is the look that every build before this one drew. Reorder these
	/// names freely. The settings window follows the order, and the fallback does not.
	/// </summary>
	public enum HeroBackground
	{
		/// <summary>The window draws no image. The plain SurfaceBase alone.</summary>
		Off,

		/// <summary>One small accent in the top-left corner, which fades out to nothing.</summary>
		Corner,

		/// <summary>An even wash of the image colors across the whole window.</summary>
		Full,
	}
}
