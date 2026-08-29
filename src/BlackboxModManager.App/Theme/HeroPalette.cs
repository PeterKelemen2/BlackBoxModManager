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

		/// <summary>
		/// The spread of the gaussian fade below. A smaller value fades out faster over the
		/// same diagonal.
		/// </summary>
		private const double GradientSigma = 0.45;

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
		/// Mixes every pixel of <paramref name="source"/> toward <paramref name="backdrop"/>,
		/// and returns a new, fully opaque bitmap. This is the "blur," not a
		/// <c>BlurEffect</c> — the source is 64x20, and the UI scales the small, blended
		/// result up with a high-quality resampler.
		///
		/// The mix ratio is <paramref name="heroWeight"/> at the top-left pixel, and it
		/// fades out on a gaussian curve, not a straight line, to exactly 0 at every other
		/// corner — see <see cref="GaussianFade"/>. The fade is baked into the pixels
		/// themselves, once, so nothing here is a live <c>Opacity</c>.
		/// </summary>
		private static WriteableBitmap BlendOverBackdrop(BitmapSource source, Color backdrop, double heroWeight)
		{
			int width = source.PixelWidth;
			int height = source.PixelHeight;
			int stride = width * 4;
			byte[] pixels = new byte[stride * height];
			source.CopyPixels(pixels, stride, 0);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					double pixelWeight = heroWeight * GaussianFade(x, y, width, height);
					double backWeight = 1 - pixelWeight;

					int i = (y * stride) + (x * 4);

					// Bgra32: blue, green, red, alpha.
					pixels[i + 0] = Mix(pixels[i + 0], backdrop.B, pixelWeight, backWeight);
					pixels[i + 1] = Mix(pixels[i + 1], backdrop.G, pixelWeight, backWeight);
					pixels[i + 2] = Mix(pixels[i + 2], backdrop.R, pixelWeight, backWeight);
					pixels[i + 3] = 255;
				}
			}

			WriteableBitmap result = new(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
			result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
			result.Freeze();
			return result;
		}

		/// <summary>
		/// 1 at the top-left pixel, fading to exactly 0 at every other corner — top-right,
		/// bottom-right, and bottom-left alike. <paramref name="x"/> and <paramref name="y"/>
		/// are pixel coordinates with y = 0 at the top, matching <see cref="BitmapSource"/>.
		///
		/// This is the product of two independent fades, one per axis: <c>nx</c> runs 1 at
		/// the left edge to 0 at the right edge, and <c>ny</c> runs 1 at the top edge to 0 at
		/// the bottom edge. Each one is <see cref="NormalizedGaussian"/> — a curve, not a
		/// straight ramp — and it is exactly, not approximately, 0 at its far edge. The
		/// product is then 1 only where both factors are 1, which is the top-left corner
		/// alone, and 0 wherever either factor is 0, which is every other edge and corner.
		/// </summary>
		private static double GaussianFade(int x, int y, int width, int height)
		{
			double nx = width > 1 ? (double)x / (width - 1) : 0;
			double ny = height > 1 ? (double)y / (height - 1) : 0;

			double leftToRight = NormalizedGaussian(nx);
			double topToBottom = NormalizedGaussian(ny);

			return leftToRight * topToBottom;
		}

		/// <summary>
		/// A gaussian curve rescaled so it reads exactly 1 at <paramref name="t"/> = 0 and
		/// exactly 0 at <paramref name="t"/> = 1, instead of only approaching 0 there. A raw
		/// gaussian never reaches zero, and "fully transparent" needs an exact one.
		/// </summary>
		private static double NormalizedGaussian(double t)
		{
			double raw = Math.Exp(-(t * t) / (2 * GradientSigma * GradientSigma));
			double rawAtOne = Math.Exp(-1 / (2 * GradientSigma * GradientSigma));

			return (raw - rawAtOne) / (1 - rawAtOne);
		}

		private static byte Mix(byte a, byte b, double weightA, double weightB)
		{
			double mixed = (a * weightA) + (b * weightB);
			return (byte)Math.Clamp(mixed, 0, 255);
		}
	}
}
