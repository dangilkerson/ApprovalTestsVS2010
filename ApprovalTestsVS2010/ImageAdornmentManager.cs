using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EnvDTE;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;

namespace Microsoft.VisualStudio.ImageInsertion
{
	/// <summary>
	/// Manages the image adornments in an instance of <see cref="IWpfTextView"/>
	/// </summary>
	internal class ImageAdornmentManager
	{
		private const string ImageAdornmentLayerName = "Intra Text Adornment";

		private readonly IServiceProvider serviceProvider;

		internal ImageAdornmentManager(IServiceProvider serviceProvider, IWpfTextView view, IEditorFormatMap editorFormatMap)
		{
			View = view;
			this.serviceProvider = serviceProvider;
			AdornmentLayer = View.GetAdornmentLayer(ImageAdornmentLayerName);

			ImagesAdornmentsRepository = new ImageAdornmentRepositoryService(view.TextBuffer);

			// Create the highlight line adornment
			HighlightLineAdornment = new HighlightLineAdornment(view, editorFormatMap);
			AdornmentLayer.AddAdornment(AdornmentPositioningBehavior.OwnerControlled, null, HighlightLineAdornment,
			                            HighlightLineAdornment.VisualElement, null);

			// Create the preview image adornment
			PreviewImageAdornment = new PreviewImageAdornment();
			AdornmentLayer.AddAdornment(AdornmentPositioningBehavior.OwnerControlled, null, this,
			                            PreviewImageAdornment.VisualElement, null);

			// Attach to the view events
			View.LayoutChanged += OnLayoutChanged;
			View.TextBuffer.Changed += OnBufferChanged;
			View.Closed += OnViewClosed;

			// Load and initialize the image adornments repository
			ImagesAdornmentsRepository.Load();
			ImagesAdornmentsRepository.Images.ToList().ForEach(image => InitializeImageAdornment(image));
		}

		private ImageAdornmentRepositoryService ImagesAdornmentsRepository { get; set; }

		internal IList<ImageAdornment> Images
		{
			get { return ImagesAdornmentsRepository.Images; }
		}

		internal HighlightLineAdornment HighlightLineAdornment { get; private set; }
		internal PreviewImageAdornment PreviewImageAdornment { get; private set; }
		internal IWpfTextView View { get; private set; }
		internal IAdornmentLayer AdornmentLayer { get; private set; }

		private void OnViewClosed(object sender, EventArgs e)
		{
			// Save the image adornments
			ImagesAdornmentsRepository.Save();

			// Detach from the view events
			View.LayoutChanged -= OnLayoutChanged;
			View.TextBuffer.Changed -= OnBufferChanged;
			View.Closed -= OnViewClosed;
		}

		private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
		{
			// Remove the image adornments if the associated spans were deleted.
			var imagesToBeRemoved = new List<ImageAdornment>();
			foreach (ImageAdornment imageAdornment in ImagesAdornmentsRepository.Images)
			{
				Span span = imageAdornment.TrackingSpan.GetSpan(e.After);
				if (span.Length == 0)
				{
					imagesToBeRemoved.Add(imageAdornment);
				}
			}

			imagesToBeRemoved.ForEach(imageAdornment => RemoveImageAdornment(imageAdornment));
		}

		private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
		{
			foreach (var item in e.NewOrReformattedLines)
			{
				if (item.GetText().ToLower().Contains("approve"))
					break;
			}

			foreach (var item in e.NewOrReformattedSpans)
			{
				

				if (item.GetText().ToLower().Contains("approve"))
					break;
			}
			//List<ImageAdornment> imageAdornmentsToBeShown = new List<ImageAdornment>();

			//// Detect which images should be shown again based on the new or reformatted spans
			//foreach (Span span in e.NewOrReformattedSpans)
			//{
			//    imageAdornmentsToBeShown.AddRange(this.ImagesAdornmentsRepository.Images.Where(image => image.TrackingSpan.GetSpan(this.View.TextSnapshot).OverlapsWith(span)));
			//}

			//foreach (ImageAdornment imageAdornment in imageAdornmentsToBeShown)
			//{
			//    SnapshotSpan imageSnaphotSpan = imageAdornment.TrackingSpan.GetSpan(this.View.TextSnapshot);
			//    // Get the text view line associated with the image span
			//    ITextViewLine newOrReformattedLine = e.NewOrReformattedLines.FirstOrDefault(line => 
			//        line.ContainsBufferPosition(imageSnaphotSpan.Start) && line.ContainsBufferPosition(imageSnaphotSpan.End));
			//    if (newOrReformattedLine != null)
			//    {
			//        // Use the top of the text view line to set image top location. And finally adjust the final location using the delta Y.
			//        Canvas.SetTop(imageAdornment.VisualElement, newOrReformattedLine.Top + imageAdornment.TextViewLineDelta.Y);
			//        Show(imageAdornment, newOrReformattedLine);
			//    }
			//}
		}

		internal ITextViewLine GetTargetTextViewLine(UIElement uiElement)
		{
			if (View.TextViewLines == null)
				return null;

			return View.TextViewLines.GetTextViewLineContainingYCoordinate(Canvas.GetTop(uiElement));
		}

		internal ITextViewLine GetTargetTextViewLine(ImageAdornment imageAdornment)
		{
			if (View.TextViewLines == null)
				return null;

			return
				View.TextViewLines.GetTextViewLineContainingBufferPosition(
					imageAdornment.TrackingSpan.GetStartPoint(View.TextSnapshot));
		}

		/// <summary>
		/// Creates and adds an image adornment for the image.
		/// </summary>
		/// <param name="image"></param>
		/// <returns></returns>
		internal ImageAdornment AddImageAdornment(Image image)
		{
			ITextViewLine targetLine = GetTargetTextViewLine(image);

			if (targetLine != null && targetLine.Length > 0)
			{
				var imageAdornment = new ImageAdornment(
					new SnapshotSpan(targetLine.Start, targetLine.Length),
					image);

				// Initialize the image adornment
				InitializeImageAdornment(imageAdornment);

				// Add the image adornment to the repository
				ImagesAdornmentsRepository.Add(imageAdornment);
				ImagesAdornmentsRepository.EnsureRepositoryFileExists();

				// Add the repository file to the solution explorer
				AddFileToTheActiveDocument(ImagesAdornmentsRepository.RepositoryFilename);

				// Show the image
				Show(imageAdornment);

				DisplayTextViewLine(imageAdornment);

				return imageAdornment;
			}

			return null;
		}


		/// <summary>
		/// Adds the file as a child of the active document.
		/// </summary>
		/// <param name="filename"></param>
		internal void AddFileToTheActiveDocument(string filename)
		{
			if (!string.IsNullOrEmpty(filename) && File.Exists(filename))
			{
				if (serviceProvider != null)
				{
					var vs = serviceProvider.GetService(typeof (DTE)) as DTE;
					if (vs != null && vs.ActiveDocument != null)
					{
						ProjectItem projectItem = vs.ActiveDocument.ProjectItem.ProjectItems.AddFromFile(filename);
						if (projectItem != null)
						{
							Property buildActionProperty = projectItem.Properties.Item("BuildAction");
							if (buildActionProperty != null)
							{
								buildActionProperty.Value = 0;
							}
						}
					}
				}
			}
		}

		private void InitializeImageAdornment(ImageAdornment imageAdornment)
		{
			imageAdornment.VisualElement.MouseMove += OnAdornmentVisualElementMouseMove;
			imageAdornment.VisualElement.MouseLeftButtonDown += OnAdornmentVisualElementMouseLeftButtonDown;
			imageAdornment.VisualElement.MouseLeftButtonUp += OnAdornmentVisualElementMouseLeftButtonUp;
			imageAdornment.VisualElement.MouseLeave += OnAdornmentVisualElementMouseLeave;
			imageAdornment.VisualElement.Deleted += OnAdornmentVisualElementDeleted;
			imageAdornment.VisualElement.Resizing += OnImageAdornmentResizing;
		}

		private void OnAdornmentVisualElementDeleted(object sender, EventArgs e)
		{
			var visualElement = sender as EditorImage;
			var imageAdornment = visualElement.Tag as ImageAdornment;

			RemoveImageAdornment(imageAdornment);
		}

		private void RemoveImageAdornment(ImageAdornment imageAdornment)
		{
			ImagesAdornmentsRepository.Remove(imageAdornment);
			AdornmentLayer.RemoveAdornment(imageAdornment.VisualElement);
		}

		private void OnAdornmentVisualElementMouseMove(object sender, MouseEventArgs e)
		{
			var visualElement = sender as FrameworkElement;
			var imageAdornment = visualElement.Tag as ImageAdornment;

			if (e.LeftButton == MouseButtonState.Pressed)
			{
				// Move the image adornment
				if (!imageAdornment.VisualElement.IsResizing &&
				    !ImagesAdornmentsRepository.Images.ToList().Exists(
				    	image => image != imageAdornment && image.VisualElement.IsMoving))
				{
					imageAdornment.VisualElement.IsMoving = true;

					Point adjustedPosition = e.GetPosition(View.VisualElement);
					adjustedPosition.X += View.ViewportLeft - (imageAdornment.VisualElement.Width/2);
					adjustedPosition.Y += View.ViewportTop - (imageAdornment.VisualElement.Height/2);

					AdornmentLayer.RemoveAdornmentsByTag(imageAdornment);

					imageAdornment.VisualElement.Opacity = PreviewImageAdornment.PreviewOpacity;
					imageAdornment.VisualElement.MoveTo(adjustedPosition);

					Show(imageAdornment);

					HighlightLineAdornment.Highlight(GetTargetTextViewLine(imageAdornment));
				}
			}

			e.Handled = true;
		}

		private void OnAdornmentVisualElementMouseLeave(object sender, MouseEventArgs e)
		{
			var visualElement = sender as FrameworkElement;
			var imageAdornment = visualElement.Tag as ImageAdornment;
			imageAdornment.VisualElement.Opacity = 1;
			imageAdornment.VisualElement.IsMoving = false;
			HighlightLineAdornment.Clear();
		}

		private void OnAdornmentVisualElementMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			var visualElement = sender as FrameworkElement;
			var imageAdornment = visualElement.Tag as ImageAdornment;
			HighlightLineAdornment.Highlight(GetTargetTextViewLine(imageAdornment));
			e.Handled = true;
		}

		private void OnAdornmentVisualElementMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			var visualElement = sender as FrameworkElement;
			var imageAdornment = visualElement.Tag as ImageAdornment;
			imageAdornment.VisualElement.Opacity = 1;
			HighlightLineAdornment.Clear();
			DisplayTextViewLine(imageAdornment);
		}

		private void Show(ImageAdornment imageAdornment)
		{
			Show(imageAdornment, GetTargetTextViewLine(imageAdornment));
		}

		private void Show(ImageAdornment imageAdornment, ITextViewLine targetTextViewLine)
		{
			if (targetTextViewLine != null)
			{
				// Update the line delta
				imageAdornment.TextViewLineDelta = new Point(imageAdornment.VisualElement.Left - targetTextViewLine.Left,
				                                             imageAdornment.VisualElement.Top - targetTextViewLine.Top);
			}

			AdornmentLayer.RemoveAdornmentsByTag(imageAdornment);
			AdornmentLayer.AddAdornment(AdornmentPositioningBehavior.TextRelative,
			                            imageAdornment.TrackingSpan.GetSpan(View.TextSnapshot), imageAdornment,
			                            imageAdornment.VisualElement, null);

			UpdateTargetLocation(imageAdornment);
		}

		private void OnImageAdornmentResizing(object sender, EventArgs e)
		{
			if (View.TextViewLines != null)
			{
				var editorImage = sender as EditorImage;
				var imageAdornment = editorImage.Tag as ImageAdornment;
				DisplayTextViewLine(imageAdornment);
			}
		}

		private void DisplayTextViewLine(ImageAdornment imageAdornment)
		{
			ITextViewLine textViewLine =
				View.TextViewLines.FirstOrDefault(line => imageAdornment.ApplyRenderTrackingPoint(View.TextSnapshot, line));

			if (textViewLine != null)
			{
				View.DisplayTextLineContainingBufferPosition(textViewLine.Start, textViewLine.Top, ViewRelativePosition.Top);
			}
			else
			{
				View.DisplayTextLineContainingBufferPosition(new SnapshotPoint(View.TextSnapshot, 0), 0.0, ViewRelativePosition.Top);
			}
		}

		private void UpdateTargetLocation(ImageAdornment imageAdornment)
		{
			imageAdornment.RenderTrackingPoint = null;

			foreach (ITextViewLine line in View.TextViewLines)
			{
				var lineArea = new Rect(line.Left, line.Top, line.Width, line.Height);
				Rect imageAdornmentArea = imageAdornment.VisualElement.Area;
				// Use the height half to be able to move the image up and down
				imageAdornmentArea.Height = imageAdornmentArea.Height/2;

				if (line.Length > 0 && lineArea.IntersectsWith(imageAdornmentArea))
				{
					imageAdornment.RenderTrackingPoint = View.TextSnapshot.CreateTrackingPoint(line.Start.Position,
					                                                                           PointTrackingMode.Negative);
					imageAdornment.UpdateTrackingSpan(line);

					return;
				}
			}
		}
	}
}