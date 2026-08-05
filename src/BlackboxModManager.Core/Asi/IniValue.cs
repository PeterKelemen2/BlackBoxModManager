using System;
using System.Globalization;

namespace BlackboxModManager.Core.Asi
{
	/// <summary>
	/// The editor that one value asks for.
	///
	/// <b>The kind comes from the value and never from the comment.</b> A comment such as
	/// <c>(1 = Cropped | 2 = Stretched)</c> reads like a list of choices, and a mod author
	/// writes that text any way they want. A parser that reads choices out of it produces a
	/// wrong list, and a drop-down built from a wrong list locks the user out of a legal value.
	/// </summary>
	public enum IniValueKind
	{
		/// <summary>The value is <c>0</c> or <c>1</c>. The row shows a check box.</summary>
		Flag = 0,

		/// <summary>The value parses as a whole number. The row shows a number box.</summary>
		Integer,

		/// <summary>The value parses as a decimal number. The row shows a number box.</summary>
		Decimal,

		/// <summary>Everything else. The row shows a text box.</summary>
		Text,
	}

	/// <summary>
	/// Reads the shape of one <c>.ini</c> value.
	///
	/// <b>Every answer here is a guess.</b> <c>FPSLimit = -1</c> looks like a number and means
	/// "the refresh rate of the monitor". <c>ImproveGamepadSupport = 0</c> looks like a check
	/// box and holds five states. So the window gives every row a way back to free text entry,
	/// and the model stores the answer as text.
	/// </summary>
	public static class IniValue
	{
		public static IniValueKind Classify(string value)
		{
			string text = value?.Trim() ?? String.Empty;

			if (text.Length == 0) return IniValueKind.Text;

			if (text == "0" || text == "1") return IniValueKind.Flag;

			if (Int64.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
			{
				return IniValueKind.Integer;
			}

			// Read a decimal with the invariant culture. A comma-decimal locale reads 10.0 as
			// one hundred, and the deployed file would then hold the wrong number.
			if (Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
			{
				return IniValueKind.Decimal;
			}

			return IniValueKind.Text;
		}

		/// <summary>
		/// True when the value reads as switched on. Call this for a
		/// <see cref="IniValueKind.Flag"/> value only.
		/// </summary>
		public static bool IsOn(string value) => value?.Trim() == "1";

		/// <summary>The text that a check box writes.</summary>
		public static string FromFlag(bool on) => on ? "1" : "0";
	}
}
