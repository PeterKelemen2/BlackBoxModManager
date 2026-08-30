using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nikki.Core;

namespace BlackboxModManager.App.Theme
{
	/// <summary>
	/// Turns a game's dominant-color image into the one bitmap that MainWindow shows above
	/// the window background. <see cref="HeroBackground"/> names the two looks that draw
	/// something: a corner accent, and an even wash across the whole window. The mod-list
	/// card and the tab strip's card sit above that bitmap and see through it on their own,
	/// through <c>SurfaceRaisedTranslucent</c> in Colors.xaml — this class only builds the
	/// one image underneath.
	///
	/// <b>Nothing here assumes the resolution of a game image.</b> Every pass reads the size
	/// of what it gets and answers with a bitmap of a size that follows from it. So a game
	/// image may be replaced with one of any resolution, and only the amount of detail
	/// changes. Keep the shape wide and short, because MainWindow takes the height of the
	/// corner accent from the shape of the image.
	///
	/// The blend runs once, when the game or the look changes, and never per frame. That is
	/// the same reasoning that the dark theme already applies to <c>SuccessSubtle</c> and
	/// <c>DangerSubtle</c> in Colors.xaml: a live <c>Opacity</c> on the image itself would
	/// recomposite on every repaint under the Wine software rasterizer, and a precomputed
	/// bitmap costs nothing.
	/// </summary>
	public static class HeroPalette
	{
		/// <summary>How much the corner accent favors the source image over the window background.</summary>
		private const double CornerHeroWeight = 0.6;

		/// <summary>
		/// The same weight for the full wash. <b>Tune this one number when the wash is too
		/// strong.</b>
		///
		/// It sits below <see cref="CornerHeroWeight"/> on purpose. The corner accent covers
		/// one corner and fades out to nothing, so it can afford to be strong. The full wash
		/// covers every pixel of the window, and the status bar text, the hint text, and the
		/// group headers all sit on top of it.
		/// </summary>
		private const double FullHeroWeight = 0.45;

		/// <summary>
		/// The spread of the gaussian fade below. A smaller value fades out faster over the
		/// same diagonal.
		/// </summary>
		private const double GradientSigma = 0.45;

		/// <summary>
		/// The largest number of columns that the full wash draws. The wash averages a wider
		/// source down to this, and it keeps the shape of that source while it does.
		/// <b>Zero turns the pass off</b>, and the wash then draws the source image as it is.
		///
		/// <b>The wash needs this and the corner accent does not.</b> A source image holds
		/// separate columns of color. The accent shows the top left of one behind a card,
		/// where the fade hides the columns. The wash spreads every column across the width
		/// of the window, and each one then reads as a vertical stripe. Averaging first turns
		/// the same colors into a soft gradient.
		///
		/// Two ways to change how much detail the wash keeps. Raise or lower this number, or
		/// supply a game image at the resolution that you want and set this to zero. A source
		/// that already holds this many columns or fewer passes through untouched either way.
		/// See <see cref="Downsample"/>.
		/// </summary>
		private const int FullWashColumns = 8;

		/// <summary>
		/// One entry per game and look. <b>The look belongs in the key.</b> The two looks of
		/// one game hold different pixels, and a key of the game alone gave the second look
		/// the bitmap of the first.
		///
		/// The backdrop color stays out of the key, because this application has one dark
		/// theme and one <c>SurfaceBase</c>. A second theme has to add it.
		/// </summary>
		private static readonly Dictionary<(GameINT Game, HeroBackground Look), BitmapSource> _cache = new();

		/// <summary>
		/// Returns the image for one game and one look, computing it the first time that pair
		/// is asked for and reusing the result after that. <see cref="HeroBackground.Off"/>
		/// draws nothing, so it answers null.
		/// </summary>
		public static BitmapSource ImageFor(
			GameINT game,
			HeroBackground look,
			string heroImageFileName,
			Color surfaceBase)
		{
			if (look == HeroBackground.Off) return null;

			if (_cache.TryGetValue((game, look), out BitmapSource cached)) return cached;

			bool corner = look == HeroBackground.Corner;

			BitmapSource source = LoadSource(heroImageFileName);

			if (!corner) source = Downsample(source, FullWashColumns);

			BitmapSource image = BlendOverBackdrop(
				source,
				surfaceBase,
				corner ? CornerHeroWeight : FullHeroWeight,
				fade: corner);

			_cache[(game, look)] = image;
			return image;
		}

		private static BitmapSource LoadSource(string heroImageFileName)
		{
			var uri = new Uri($"pack://application:,,,/Assets/GameImages/{heroImageFileName}");
			BitmapImage image = new(uri);
			image.Freeze();
			return new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
		}

		/// <summary>
		/// Averages <paramref name="source"/> down to <paramref name="columns"/> pixels wide
		/// and returns the smaller bitmap.
		///
		/// <b>This reads the size of the source and never assumes one.</b> The row count
		/// follows the columns and the shape of the source, so an image of any resolution
		/// gives the same wash. A source that is already this narrow, and a
		/// <paramref name="columns"/> below one, both return the source unchanged.
		///
		/// Every target pixel is the plain mean of the source pixels of its own box. This is
		/// a box filter and not a resampler, because a downscale to a flat gradient needs
		/// nothing better. It also runs once and never per frame, in the same way as
		/// <see cref="BlendOverBackdrop"/> below.
		///
		/// <b>Do not replace this with a Stretch on the element.</b> A stretch of the raw
		/// image draws every source column, and drawing the columns is what this removes.
		/// </summary>
		private static BitmapSource Downsample(BitmapSource source, int columns)
		{
			int sourceWidth = source.PixelWidth;
			int sourceHeight = source.PixelHeight;

			if (columns < 1 || columns >= sourceWidth) return source;

			// The rows follow the shape of the source. A wash of a 64x20 image and a wash of
			// a 320x100 image then hold the same pixels.
			//
			// AwayFromZero, because the default of Math.Round rounds a half to the nearest
			// even number. The shipped images land on exactly one half, and the default there
			// drops a row.
			int height = (int)Math.Round(
				(double)columns * sourceHeight / sourceWidth,
				MidpointRounding.AwayFromZero);
			height = Math.Clamp(height, 1, sourceHeight);

			int width = columns;
			int sourceStride = sourceWidth * 4;
			byte[] sourcePixels = new byte[sourceStride * sourceHeight];
			source.CopyPixels(sourcePixels, sourceStride, 0);

			int stride = width * 4;
			byte[] pixels = new byte[stride * height];

			for (int y = 0; y < height; y++)
			{
				int top = y * sourceHeight / height;
				int bottom = Math.Max(top + 1, (y + 1) * sourceHeight / height);

				for (int x = 0; x < width; x++)
				{
					int left = x * sourceWidth / width;
					int right = Math.Max(left + 1, (x + 1) * sourceWidth / width);

					long blue = 0;
					long green = 0;
					long red = 0;
					long count = 0;

					for (int sy = top; sy < bottom; sy++)
					{
						for (int sx = left; sx < right; sx++)
						{
							int s = (sy * sourceStride) + (sx * 4);

							blue += sourcePixels[s + 0];
							green += sourcePixels[s + 1];
							red += sourcePixels[s + 2];
							count++;
						}
					}

					int i = (y * stride) + (x * 4);

					pixels[i + 0] = (byte)(blue / count);
					pixels[i + 1] = (byte)(green / count);
					pixels[i + 2] = (byte)(red / count);
					pixels[i + 3] = 255;
				}
			}

			WriteableBitmap result = new(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
			result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
			result.Freeze();
			return result;
		}

		/// <summary>
		/// Mixes every pixel of <paramref name="source"/> toward <paramref name="backdrop"/>,
		/// and returns a new, fully opaque bitmap. This is the "blur," not a
		/// <c>BlurEffect</c> — the source is a small image, and the UI scales the blended
		/// result up with a high-quality resampler.
		///
		/// With <paramref name="fade"/>, the mix ratio is <paramref name="heroWeight"/> at
		/// the top-left pixel, and it fades out on a gaussian curve, not a straight line, to
		/// exactly 0 at every other corner — see <see cref="GaussianFade"/>. The fade is
		/// baked into the pixels themselves, once, so nothing here is a live
		/// <c>Opacity</c>.
		///
		/// Without it, every pixel takes <paramref name="heroWeight"/> and the whole bitmap
		/// carries the game colors evenly. That is the full wash, which has no corner to
		/// fade away from.
		/// </summary>
		private static WriteableBitmap BlendOverBackdrop(
			BitmapSource source,
			Color backdrop,
			double heroWeight,
			bool fade)
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
					double pixelWeight = fade
						? heroWeight * GaussianFade(x, y, width, height)
						: heroWeight;
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
