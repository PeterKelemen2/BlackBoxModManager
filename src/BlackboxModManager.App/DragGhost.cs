using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlackboxModManager.App
{
	/// <summary>
	/// The copy of a row that floats under the pointer during a drag.
	///
	/// It draws one bitmap and one outline, and nothing else. The bitmap comes from one
	/// capture at the start of the drag, so a move costs one blit of a small rectangle. A
	/// <see cref="VisualBrush"/> of the live row would redraw the whole row on every frame
	/// instead, and the software rasterizer of Wine pays that cost on the CPU.
	///
	/// <b>The capture must happen before the row becomes the ghost slot.</b> The row recesses
	/// and dims as soon as IsDragSource turns true, and the floating copy has to hold the
	/// resting look of the row.
	///
	/// This is the one adorner of the window. Step 10 kept the insertion line out of the
	/// adorner layer, because a marker on every row would need a render pass for each one.
	/// A single floating visual is what an adorner is for. It also has to draw outside the
	/// list, which nothing inside the list can do.
	/// </summary>
	internal sealed class DragGhost : Adorner
	{
		/// <summary>
		/// How solid the floating copy is. It lifts off the window, so the user has to read the
		/// list under it.
		///
		/// Step 10 banned opacity as a color tool, and this does not break that rule. That ban
		/// covers a resting element, where the rasterizer composites the value on every repaint
		/// for as long as the window is open. This is one rectangle, for the length of one drag,
		/// and the alpha is the point of it.
		/// </summary>
		private const double Solidity = 0.8;

		private readonly ImageSource _image;
		private readonly Size _size;
		private readonly Pen _edge;

		private Point _origin;

		private DragGhost(UIElement adorned, ImageSource image, Size size, Brush edge)
			: base(adorned)
		{
			this._image = image;
			this._size = size;
			this._edge = new Pen(edge, 1);
			this._edge.Freeze();

			this.IsHitTestVisible = false;
		}

		/// <summary>
		/// Captures <paramref name="row"/> and adds the result to the adorner layer of
		/// <paramref name="adorned"/>. It returns null when the window holds no adorner layer,
		/// or when the capture fails. A drag still works without the floating copy.
		/// </summary>
		public static DragGhost Attach(UIElement adorned, FrameworkElement row, Brush edge)
		{
			if (adorned is null || row is null) return null;

			AdornerLayer layer = AdornerLayer.GetAdornerLayer(adorned);

			if (layer is null) return null;

			try
			{
				Rect bounds = VisualTreeHelper.GetDescendantBounds(row);

				if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) return null;

				double scale = VisualTreeHelper.GetDpi(row).DpiScaleX;

				// A VisualBrush inside a DrawingVisual, and not RenderTargetBitmap.Render(row).
				// Render reads the visual with the offset that its parent arranged it at, so a row
				// with a margin comes out shifted. The brush fills a rectangle of its own.
				var drawing = new DrawingVisual();

				using (DrawingContext context = drawing.RenderOpen())
				{
					context.DrawRectangle(new VisualBrush(row), null, new Rect(bounds.Size));
				}

				var bitmap = new RenderTargetBitmap(
					(int)Math.Ceiling(bounds.Width * scale),
					(int)Math.Ceiling(bounds.Height * scale),
					96 * scale, 96 * scale, PixelFormats.Pbgra32);

				bitmap.Render(drawing);
				bitmap.Freeze();

				var ghost = new DragGhost(adorned, bitmap, bounds.Size, edge);
				layer.Add(ghost);

				return ghost;
			}
			catch (Exception)
			{
				// The floating copy is cosmetic. It never fails a drag.
				return null;
			}
		}

		/// <summary>Puts the top left corner of the copy at a point of the adorned element.</summary>
		public void MoveTo(Point origin)
		{
			if (this._origin == origin) return;

			this._origin = origin;
			this.InvalidateVisual();
		}

		public void Detach()
		{
			AdornerLayer.GetAdornerLayer(this.AdornedElement)?.Remove(this);
		}

		protected override void OnRender(DrawingContext drawingContext)
		{
			var area = new Rect(this._origin, this._size);

			drawingContext.PushOpacity(Solidity);

			drawingContext.DrawImage(this._image, area);

			// One line, and no DropShadowEffect. A bitmap effect runs per frame on the CPU under
			// the software rasterizer. See the pitfalls of docs/roadmap/10-dark-theme.md.
			drawingContext.DrawRectangle(null, this._edge, area);

			drawingContext.Pop();
		}
	}
}
