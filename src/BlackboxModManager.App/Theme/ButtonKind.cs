using System.Windows;

namespace BlackboxModManager.App.Theme
{
	/// <summary>The visual mode of a button. See docs/roadmap/10-dark-theme.md, Part C.</summary>
	public enum ButtonKind
	{
		Default,
		Primary,
		Success,
		Danger,
		Quiet
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
