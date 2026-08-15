using System.Windows;

namespace BlackboxModManager.App.Theme
{
	/// <summary>
	/// The visual mode of a button. See docs/roadmap/10-dark-theme.md, Part C, and
	/// docs/roadmap/12-minimal-ui.md, Part C.
	/// </summary>
	public enum ButtonKind
	{
		Default,
		Primary,
		Success,
		Danger,
		Quiet,

		/// <summary>
		/// A colored edge and colored text over no fill. The window uses these for the two
		/// actions of the bottom bar and for the remove button of the mod list.
		///
		/// <b>Success and Danger keep their solid fill.</b> ConfirmWindow paints the confirm
		/// button of a destructive question with Danger, and step 11 made that choice on
		/// purpose. A dialog that asks a question needs a louder button than a toolbar does.
		/// </summary>
		SuccessOutline,

		/// <inheritdoc cref="SuccessOutline"/>
		DangerOutline
	}

	/// <summary>
	/// The attached property that a button sets to choose its mode. The user changes the mode
	/// and nothing else:
	/// <c>&lt;Button ui:Kind.Value="Success" ... /&gt;</c>
	/// </summary>
	public static class Kind
	{
		public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
			"Value", typeof(ButtonKind), typeof(Kind), new FrameworkPropertyMetadata(ButtonKind.Default));

		public static void SetValue(DependencyObject element, ButtonKind value) => element.SetValue(ValueProperty, value);

		public static ButtonKind GetValue(DependencyObject element) => (ButtonKind)element.GetValue(ValueProperty);
	}
}
