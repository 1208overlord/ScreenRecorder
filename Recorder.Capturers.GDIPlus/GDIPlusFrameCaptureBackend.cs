// ScreenGun
// - GDIPlusFrameCaptureBackend.cs
// --------------------------------------------------------------------
// Authors: 
// - Jeff Hansen <jeff@jeffijoe.com>
// - Bjarke Søgaard <ekrajb123@gmail.com>
// Copyright (C) ScreenGun Authors 2015. All rights reserved.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Recorder.Capturers.GDIPlus
{
    /// <summary>
    ///     GDI+ Back-end.
    /// </summary>
    public class GDIPlusFrameCaptureBackend : IFrameCaptureBackend
    {
        #region Public Methods and Operators
        private Bitmap Resize(Bitmap target_image, Size new_size)
        {
            int target_width = new_size.Width;
            int target_height = new_size.Height;
            Rectangle rectangle = new Rectangle(0, 0, target_width, target_height);
            Bitmap destImage = new Bitmap(target_width, target_height);
            destImage.SetResolution(target_image.HorizontalResolution, target_image.VerticalResolution);
            using (var g = Graphics.FromImage(destImage))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapmode = new ImageAttributes())
                {
                    wrapmode.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(target_image, rectangle, 0, 0, target_image.Width, target_image.Height, GraphicsUnit.Pixel);
                }
            }
            return destImage;
        }        /// <summary>
                 /// Captures a frame.
                 /// </summary>
                 /// <param name="region">
                 /// The region.
                 /// </param>
                 /// <returns>
                 /// A bitmap containing the captured frame.
                 /// </returns>
        public Bitmap CaptureFrame(ScreenRecorderOptions recordOption)
        {
            Rectangle region = recordOption.RecordingRegion;
            // Create a new bitmap.
            var frameBitmap = new Bitmap(
                region.Width, 
                region.Height, 
                PixelFormat.Format32bppArgb);

            // Create a graphics object from the bitmap.
            using (var gfxScreenshot = Graphics.FromImage(frameBitmap))
            {
                // Take the screenshot from the upper left corner to the right bottom corner.
                gfxScreenshot.CopyFromScreen(
                    region.X, 
                    region.Y, 
                    0, 
                    0, 
                    region.Size, 
                    CopyPixelOperation.SourceCopy);

                Point position = Cursor.Position;
                var x = position.X;
                var y = position.Y;
                var cursorBmp = CursorHelper.CaptureCursor(ref x, ref y);

                // We need to offset the cursor position by the region, to position it correctly
                // in the image.
                position = new Point(
                    x - region.X, 
                    y - region.Y);
                if (cursorBmp != null)
                {
                    gfxScreenshot.DrawImage(cursorBmp, position);
                }

                cursorBmp.Dispose();
            }

            return frameBitmap;
        }

        #endregion
    }
}