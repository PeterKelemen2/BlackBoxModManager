# Step 18 — the hero background

Give the window three looks for the per-game color image, and put the choice in the settings window. This step changes what the window shows. **It changes no deploy rule and no byte that a deploy writes.**

The window already drew one look: a small accent in the top left corner, built from a small dominant-color image of the selected game. That look was the only one on offer, and a user who wants more color or no color at all had no way to say so.

## The three looks

| Name     | What the window draws                                            |
| -------- | ---------------------------------------------------------------- |
| `Off`    | Nothing. The plain `SurfaceBase` of the theme alone.              |
| `Corner` | One accent in the top left corner, which fades out to nothing.    |
| `Full`   | An even wash of the image colors across the whole window.         |

`Corner` is the default, and it is the look that every build before this step drew.

## What this step must not change

These rules come from steps 10 to 17. All of them still hold.

1. **The window runs under Wine with the software rasterizer.** Use no resting `Opacity`, no `BlurEffect`, and no `DropShadowEffect`. `docs/roadmap/10-dark-theme.md`, Part A, is the source.
2. **Write no color literal outside `Theme/Colors.xaml`.**
3. **`ConfigWindow` holds no logic.** It takes the `MainViewModel` as its `DataContext`.
4. **A profile fully determines the deployed result.** The look belongs to the machine, so it lives in the settings file and never in a profile.
5. **Name each `Resource` of the csproj.** Do not glob the game images.

## Part A — the fade is pixels, and never an opacity

`Theme/HeroPalette.cs` reads the color image of the game, mixes every pixel toward the `SurfaceBase` color, and writes the result into an opaque `WriteableBitmap`. The alpha of every pixel stays at 255. **The fade is a color mix and not a transparency.**

This matters because of rule 1 above. A live `Opacity` on the `Image` element would recomposite the element on every repaint, and the software rasterizer of Wine pays for that on every frame. A bitmap that already holds the blend costs nothing after the one pass that builds it.

The blend runs once for each game and look, and the result stays in a cache. **The look belongs in the cache key.** The two looks of one game hold different pixels, and a key of the game alone hands the second look the bitmap of the first.

`RenderOptions.BitmapScalingMode="HighQuality"` is what blurs the result. The source is small, and the Fant resampler turns that into a soft gradient when the element scales it up. This is the blur. A `BlurEffect` is not, and rule 1 bans it.

## Part B — `Corner` fades and `Full` does not

`Corner` scales its weight by a gaussian curve on each axis. The curve reads 1 at the top left pixel and exactly 0 at the far edge of each axis, so the accent reaches the window background at the other three corners. `NormalizedGaussian` rescales a raw gaussian to get that exact zero. A raw curve only approaches zero, and "fully faded" needs an exact value.

`Full` skips the curve. Every pixel takes the same weight, and the wash carries the game colors to every edge. There is no corner to fade away from.

**Two weights, and the full wash is the weaker one.** `CornerHeroWeight` is 0.6 and `FullHeroWeight` is 0.45. The accent covers one corner and fades out, so it can afford to be strong. The wash covers every pixel of the window, and the status bar text, the hint text, and the group headers all sit on top of it. **`FullHeroWeight` is the number to tune when the wash is too strong.**

## Part C — the wash averages its source down first

A source image holds separate columns of color, and it is not one flat color. The shipped images are 64 columns wide. The first build of the wash stretched all 64 across 1040 pixels. Each column became a 16-pixel stripe, and the window read as a set of vertical bars.

`Downsample` fixes that. It averages the source down to `FullWashColumns` columns before the blend, and the element then scales 8 columns instead of 64. The same colors come out as a soft gradient.

The accent needs none of this. It shows the top left of the image behind one card, and the fade there hides the columns.

Two ways to change how much detail the wash keeps.

1. Raise `FullWashColumns` for more color detail, and lower it for a smoother wash.
2. Supply a game image at the resolution that you want, and set `FullWashColumns` to zero. Zero turns the pass off, and the wash then draws the image as it is.

A source that already holds `FullWashColumns` columns or fewer passes through untouched either way.

**Do not replace this pass with a `Stretch` on the element.** A stretch of the raw image draws every source column, and drawing the columns is the problem.

## Part D — no code holds the resolution of a game image

`Downsample` reads the width and the height of what it gets, and it derives the row count from the column count and that shape. A 64x20 image and a 320x100 image give the same wash. `BlendOverBackdrop` reads the same two numbers and writes a bitmap of the same size.

`MainWindow.xaml` sets `Width="480"` on the corner accent and no `Height`. `Stretch="Uniform"` then makes the element as tall as the shape of the image asks for. A hardcoded `Height` would fix one aspect ratio. **`UniformToFill` with no `Height` is wrong here**, because it grows the element to fill the whole window instead.

The wash element needs nothing of the sort. `Stretch="Fill"` covers the grid whatever the source holds.

So a game image can be replaced with one of any resolution, and only the amount of detail changes. **Keep the shape wide and short.** The accent takes its height from the shape, and a square image would draw a 480 by 480 accent.

## Part E — null is how a look hides

`MainWindow.xaml` holds two `Image` elements, and `MainViewModel` feeds each one from a property of its own. The property that does not match the current look takes null.

**An `Image` with no `Source` draws nothing and answers no hit test.** So neither element needs a `Visibility`, a `DataTrigger`, or a converter, and the window needs no new converter in `Theme/Metrics.xaml`.

Both elements sit at the top of the root grid, the wash first and the accent second. WPF paints panel children in declaration order, so the first child sits above the window background and under every row that follows. Neither element needs `Panel.ZIndex`.

The wash carries `Margin="-12"` on all four sides, which cancels the `Margin="12"` of the root grid. The accent cancels the top and the left alone, because it keeps the top left corner of the window. The wash uses `Stretch="Fill"` and not `UniformToFill`. The source is wide and short and the window is not, and an even wash carries no shape that a crop could protect.

## Part F — what the wash reaches, and what covers it

Two cards already paint `SurfaceRaisedTranslucent`, which is `SurfaceRaised` with its alpha at `0xBF`. The mod list card and the tab strip both let a quarter of the wash through. `Theme/Colors.xaml` records why that one brush is a deliberate exception to rule 1: the two cards redraw rarely, and the alpha sits on the fill alone, so a border and its text stay crisp.

The status bar needed one change. The style in `Theme/Controls.xaml` paints that bar `SurfaceBase`, and an opaque bar cuts a solid strip across the bottom of the wash. `MainWindow.xaml` sets `Background="Transparent"` on the instance. The window itself paints `SurfaceBase`, so the bar looks the same as before in the other two looks.

## Part G — a string in the settings file, and an enum in the App

`Settings.HeroBackground` is a `string`. `BlackboxModManager.Core` holds no view concern, and `Settings.LastGame` sets that precedent: `Core` stores the name and the App parses it back.

**The file needs no migration, and `Settings.Version` stays at 2.** A file that an older build wrote holds no such key, a missing key reads as null, and `MainViewModel.StoredHeroBackground` answers `Corner` for every name that it cannot read.

The `HeroBackground` enum lives in `src/BlackboxModManager.App/Theme/HeroBackground.cs`. The order of its names is the order that the settings window shows. That order is free to change, because the fallback lives in the parse helper and never in the number of a value.

## Part H — three radio buttons

The settings window holds an `Appearance` group with three radio buttons. The two groups above it hold two values each, and a `TogglePill` carries those. This answer holds three values, so a toggle cannot.

`Theme/Controls.xaml` gained a `RadioButton` style. The theme retemplates every control, and no such style existed. A bare radio button draws the default WPF chrome and black text on the dark surface. The new style is the `CheckBox` style with a round box and a drawn dot. It needs no key, because no template of that file names a `RadioButton`.

`MainViewModel` exposes the look as three booleans, one per button, in the shape that `BinaryRouteIsCli` already uses for a toggle. **Each setter acts on true alone.** WPF pushes false into the two buttons that lose the group, and a setter that answered false would fight the one that won. Three `NotifyPropertyChangedFor` attributes on the enum property keep all three buttons in step.

## Results

**Done, and checked on Windows.** No part of this step ran under Wine yet.

Six facts carry forward.

1. **The fade is baked into the pixels, and the alpha is always 255.** Every future look has to do the same. A live `Opacity` on the image breaks rule 1, and the reason is the Wine software rasterizer and not a preference.
2. **The cache key holds the game and the look, and not the backdrop color.** This application has one dark theme and one `SurfaceBase`. A second theme has to put the backdrop in the key, or every game keeps the bitmap that the first theme built.
3. **A source image is a color strip and not one flat color.** The wash averages it down to `FullWashColumns` columns first, or every source column reads as a vertical stripe across the window. Any future look that shows the whole image has to do the same.
4. **No code holds the resolution of a game image.** Every pass reads the size of what it gets, and the corner accent takes its height from the shape of the source. A replacement image may hold any resolution. Keep the shape wide and short.
5. **`FullHeroWeight` is the readability control.** The wash sits behind every label of the window. Read the status bar, the hint text, and the group headers over all six games before you raise it.
6. **The window rescales the wash bitmap on every layout pass in `Full`.** That is a resize cost and not a per-frame cost, because the window animates nothing. If a drag of the window edge feels slow under Wine, `LowQuality` on that one element is the answer. An even wash needs no high-quality resample.
