using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nikki.Core;

namespace BlackboxModManager.App.Theme
{
	/// <summary>
	/// Turns a game's tiny dominant-color image into the corner accent that MainWindow
	/// shows above the window background. The mod-list card and the tab strip's card sit
	/// above the accent and see through it on their own, through
	/// <c>SurfaceRaisedTranslucent</c> in Colors.xaml — this class only builds the one
	/// image underneath.
	///
	/// The blend runs once, when the game switches, and never per frame. That is the same
	/// reasoning that the dark theme already applies to <c>SuccessSubtle</c> and
	/// <c>DangerSubtle</c> in Colors.xaml: a live <c>Opacity</c> on the image itself would
	/// recomposite on every repaint under the Wine software rasterizer, and a precomputed
	/// bitmap costs nothing.
	/// </summary>
	public static class HeroPalette
	{
		/// <summary>How much the corner accent favors the source image over the window background.</summary>
		private const double CornerHeroWeight = 0.6;

		private static readonly Dictionary<GameINT, BitmapSource> _cache = new();

		/// <summary>
		/// Returns the corner accent for one game, computing it the first time this game is
		/// asked for and reusing the result after that.
		/// </summary>
		public static BitmapSource CornerFor(GameINT game, string heroImageFileName, Color surfaceBase)
		{
			if (_cache.TryGetValue(game, out BitmapSource cached)) return cached;

			BitmapSource source = LoadSource(heroImageFileName);
			BitmapSource corner = BlendOverBackdrop(source, surfaceBase, CornerHeroWeight);

			_cache[game] = corner;
			return corner;
		}

		private static BitmapSource LoadSource(string heroImageFileName)
		{
			var uri = new Uri($"pack://application:,,,/Assets/GameImages/{heroImageFileName}");
			BitmapImage image = new(uri);
			image.Freeze();
			return new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
		}

		/// <summary>
		/// Mixes every pixel of <paramref name="source"/> toward <paramref name="backdrop"/>
		/// by <paramref name="heroWeight"/>, and returns a new, fully opaque bitmap. This is
		/// the "blur," not a <c>BlurEffect</c> — the source is 64x20, and the UI scales the
		/// small, blended result up with a high-quality resampler.
		/// </summary>
		private static WriteableBitmap BlendOverBackdrop(BitmapSource source, Color backdrop, double heroWeight)
		{
			int width = source.PixelWidth;
			int height = source.PixelHeight;
			int stride = width * 4;
			byte[] pixels = new byte[stride * height];
			source.CopyPixels(pixels, stride, 0);

			double backWeight = 1 - heroWeight;

			for (int i = 0; i < pixels.Length; i += 4)
			{
				// Bgra32: blue, green, red, alpha.
				pixels[i + 0] = Mix(pixels[i + 0], backdrop.B, heroWeight, backWeight);
				pixels[i + 1] = Mix(pixels[i + 1], backdrop.G, heroWeight, backWeight);
				pixels[i + 2] = Mix(pixels[i + 2], backdrop.R, heroWeight, backWeight);
				pixels[i + 3] = 255;
			}

			WriteableBitmap result = new(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
			result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
			result.Freeze();
			return result;
		}

		private static byte Mix(byte a, byte b, double weightA, double weightB)
		{
			double mixed = (a * weightA) + (b * weightB);
			return (byte)Math.Clamp(mixed, 0, 255);
		}
	}
}
