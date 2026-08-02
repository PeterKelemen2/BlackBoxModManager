using System;
using System.Runtime.InteropServices;

// RenderMode lives in System.Windows.Interop. RenderOptions lives in System.Windows.Media.
using System.Windows.Interop;
using System.Windows.Media;

namespace BlackboxModManager.App
{
	/// <summary>
	/// Chooses the render mode of the process.
	///
	/// <b>A dropdown renders solid black under Wine with hardware rendering.</b> The popup of
	/// a ComboBox carries AllowsTransparency, so WPF makes it a layered window and composites
	/// the pixels itself. The Direct3D path of Wine gives back no content for that window, and
	/// the popup then paints black. The list is there and the user cannot read it.
	///
	/// The software rasterizer draws the same popup correctly. It costs frame rate that this
	/// window does not need, because the window holds a list, a grid, and text.
	///
	/// <b>Only Wine gets the software rasterizer.</b> A real Windows machine keeps hardware
	/// rendering. Set BLACKBOX_RENDER_MODE to compare the two on one machine.
	/// </summary>
	internal static class Rendering
	{
		/// <summary>
		/// The environment variable that overrides the choice. It takes "software",
		/// "hardware", or "auto". Any other value reads as "auto".
		/// </summary>
		public const string ModeVariable = "BLACKBOX_RENDER_MODE";

		/// <summary>
		/// One line that names the platform and the mode. MainViewModel writes it to the log,
		/// so that a user who reports a render problem can read the mode.
		/// </summary>
		public static string Report { get; private set; } = String.Empty;

		/// <summary>
		/// True when this process runs under Wine.
		///
		/// The test asks ntdll for the export wine_get_version. Wine adds that function and
		/// Windows does not. This is the method that Wine itself documents.
		/// </summary>
		public static bool IsWine { get; } = DetectWine();

		/// <summary>
		/// Sets the render mode of the process.
		///
		/// <b>Call this before the first window opens.</b> ProcessRenderMode takes effect for
		/// the composition target of a window, and a window that already renders keeps the
		/// mode that it started with.
		/// </summary>
		public static void Apply()
		{
			string wanted = Environment.GetEnvironmentVariable(ModeVariable);
			bool software;
			string why;

			if (String.Equals(wanted, "software", StringComparison.OrdinalIgnoreCase))
			{
				software = true;
				why = $"{ModeVariable} asks for it";
			}
			else if (String.Equals(wanted, "hardware", StringComparison.OrdinalIgnoreCase))
			{
				software = false;
				why = $"{ModeVariable} asks for it";
			}
			else
			{
				software = IsWine;
				why = IsWine
					? "this process runs under Wine, where a hardware popup paints black"
					: "this process runs on Windows";
			}

			RenderOptions.ProcessRenderMode = software ? RenderMode.SoftwareOnly : RenderMode.Default;

			string mode = software ? "software" : "hardware";
			Report = $"The window renders in {mode} mode, because {why}.";
		}

		private static bool DetectWine()
		{
			try
			{
				if (!NativeLibrary.TryLoad("ntdll.dll", out IntPtr library)) return false;

				return NativeLibrary.TryGetExport(library, "wine_get_version", out IntPtr _);
			}
			catch (Exception)
			{
				// A platform that cannot answer is not Wine. Keep hardware rendering.
				return false;
			}
		}
	}
}
