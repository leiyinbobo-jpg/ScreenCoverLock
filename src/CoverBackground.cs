using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using ScreenCoverUpgrade;

namespace ScreenCoverSupport {
 static class CoverBackground {
  static Image normal = Load("","cover-background.png"), dark = Load("","cover-dark.png");
  static string inputPosition = "Bottom";
  public static bool Dark;

  public static void Configure(Settings settings) {
   var next = Load(settings.NormalImage, "cover-background.png");
   Image dim;
   if (!string.IsNullOrWhiteSpace(settings.DarkImage)) {
    dim = Load(settings.DarkImage, "cover-dark.png");
   } else if (!string.IsNullOrWhiteSpace(settings.NormalImage) && next != null) {
    // Automatically dim the user's custom wallpaper so idle dimming never falls back to a mismatched image.
    dim = DimCopy(next, 0.28f);
   } else {
    dim = Load("", "cover-dark.png");
   }
   if (normal != null) normal.Dispose();
   if (dark != null) dark.Dispose();
   normal = next;
   dark = dim;
   inputPosition = string.IsNullOrEmpty(settings.InputPosition) ? "Bottom" : settings.InputPosition;
   Dark = false;
  }

  public static Image LoadFromFileOrResource(string selected, string fallback) {
   return Load(selected, fallback);
  }

  public static Image DimCopy(Image source, float factor) {
   if (source == null) return null;
   var bmp = new Bitmap(source.Width, source.Height);
   using (var g = Graphics.FromImage(bmp))
   using (var attrs = new ImageAttributes()) {
    var matrix = new ColorMatrix(new float[][] {
     new float[] { factor, 0, 0, 0, 0 },
     new float[] { 0, factor, 0, 0, 0 },
     new float[] { 0, 0, factor, 0, 0 },
     new float[] { 0, 0, 0, 1f, 0 },
     new float[] { 0, 0, 0, 0, 1f }
    });
    attrs.SetColorMatrix(matrix);
    g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
   }
   return bmp;
  }

  static Image Load(string selected, string fallback) {
   foreach (string path in new[] { selected, string.IsNullOrEmpty(fallback) ? "" : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fallback) }) {
    if (string.IsNullOrWhiteSpace(path)) continue;
    try {
     if (File.Exists(path)) {
      byte[] data = File.ReadAllBytes(path);
      using (var ms = new MemoryStream(data))
      using (var image = Image.FromStream(ms)) return new Bitmap(image);
     }
    } catch { }
   }
   if (!string.IsNullOrEmpty(fallback)) {
    try {
     using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fallback))
      if (stream != null) using (var image = Image.FromStream(stream)) return new Bitmap(image);
    } catch { }
   }
   return null;
  }

  public static Rectangle DialogueBounds(Rectangle bounds) {
   return DialogueBounds(bounds, inputPosition);
  }

  public static Rectangle DialogueBounds(Rectangle bounds, string position) {
   float yRatio = 0.755f;
   if (string.Equals(position, "Center", StringComparison.OrdinalIgnoreCase)) yRatio = 0.43f;
   else if (string.Equals(position, "Top", StringComparison.OrdinalIgnoreCase)) yRatio = 0.10f;
   var picture = Dark ? (dark ?? normal) : normal;
   if (picture == null)
    return new Rectangle(bounds.X + bounds.Width / 5, bounds.Y + (int)(bounds.Height * yRatio), bounds.Width * 3 / 5, bounds.Height / 7);
   float scale = Math.Max((float)bounds.Width / picture.Width, (float)bounds.Height / picture.Height);
   float width = picture.Width * scale, height = picture.Height * scale;
   float left = bounds.X + (bounds.Width - width) / 2, top = bounds.Y + (bounds.Height - height) / 2;
   return Rectangle.Intersect(Rectangle.Round(new RectangleF(left + width * 0.205f, top + height * yRatio, width * 0.59f, height * 0.135f)), bounds);
  }

  public static void Paint(Graphics graphics, Rectangle bounds) {
   graphics.Clear(Color.Black);
   var picture = Dark ? (dark ?? normal) : normal;
   if (picture == null || bounds.Width <= 0 || bounds.Height <= 0) return;
   float scale = Math.Max((float)bounds.Width / picture.Width, (float)bounds.Height / picture.Height);
   float width = picture.Width * scale, height = picture.Height * scale;
   graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
   graphics.DrawImage(picture, bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
  }
 }

 sealed class CoverAssets : IDisposable {
  public readonly Image[] Digits = new Image[6];
  public Image CompleteIcon;
  public Image ErrorIcon;

  public void Configure(Settings settings) {
   DisposeImages();
   for (int i = 0; i < 5; i++) {
    string custom = ResolveDigitPath(settings, i + 1);
    Digits[i] = CoverBackground.LoadFromFileOrResource(custom, "digit-" + (i + 1) + ".png");
   }
   string completePath = ResolveCompletePath(settings);
   CompleteIcon = CoverBackground.LoadFromFileOrResource(completePath, "digit-complete.png");
   Digits[5] = CompleteIcon;
   ErrorIcon = CoverBackground.LoadFromFileOrResource(settings != null ? settings.ErrorImage : "", "error-badge.png");
  }

  public static string ResolveDigitPath(Settings settings, int index) {
   if (settings == null) return "";
   if (!string.IsNullOrWhiteSpace(settings.DigitIconFolder) && Directory.Exists(settings.DigitIconFolder)) {
    string[] candidates = new[] {
     "digit-" + index + ".png",
     index + ".png",
     index + ".jpg",
     "chained-invade-title-" + index + ".png"
    };
    foreach (string name in candidates) {
     string p = Path.Combine(settings.DigitIconFolder, name);
     if (File.Exists(p)) return p;
    }
   }
   return string.IsNullOrWhiteSpace(settings.DigitImage) ? "" : settings.DigitImage;
  }

  public static string ResolveCompletePath(Settings settings) {
   if (settings == null) return "";
   if (!string.IsNullOrWhiteSpace(settings.CompleteImage)) return settings.CompleteImage;
   if (!string.IsNullOrWhiteSpace(settings.DigitIconFolder) && Directory.Exists(settings.DigitIconFolder)) {
    foreach (string name in new[] { "digit-complete.png", "complete.png", "6.png", "digit-6.png" }) {
     string p = Path.Combine(settings.DigitIconFolder, name);
     if (File.Exists(p)) return p;
    }
   }
   return "";
  }

  public void PaintInputAndWarning(Graphics g, Size clientSize, Rectangle clientRect, int inputLength, bool lockedOut, LockState state, Settings settings) {
   bool showWarning = state != null && state.Warning && settings != null && settings.WarnOnError;
   Rectangle dialogue = CoverBackground.DialogueBounds(clientRect);
   if (showWarning)
    dialogue.Height = Math.Max(1, dialogue.Height - Math.Max(28, Math.Min(50, clientSize.Height / 20)));

   int digitCount = lockedOut ? 0 : Math.Min(inputLength, 6);
   int maxHeight = Math.Max(1, Math.Min(dialogue.Height, clientSize.Height / 10));
   if (digitCount == 6 && CompleteIcon != null) {
    float scale = Math.Min((float)dialogue.Width / CompleteIcon.Width, (float)maxHeight / CompleteIcon.Height);
    int width = Math.Max(1, (int)(CompleteIcon.Width * scale));
    int height = Math.Max(1, (int)(CompleteIcon.Height * scale));
    g.DrawImage(CompleteIcon, dialogue.Left, dialogue.Top + (dialogue.Height - height) / 2, width, height);
   } else if (digitCount > 0) {
    int gap = Math.Max(4, maxHeight * 15 / 80);
    int dw = Math.Max(1, Math.Min(maxHeight * 50 / 80, (dialogue.Width - gap * 4) / 5));
    int dh = Math.Max(1, Math.Min(maxHeight, dw * 80 / 50));
    int dy = dialogue.Top + (dialogue.Height - dh) / 2;
    for (int i = 0; i < digitCount; i++)
     if (Digits[i] != null)
      g.DrawImage(Digits[i], dialogue.Left + i * (dw + gap), dy, dw, dh);
   }

   if (!showWarning) return;
   var area = CoverBackground.DialogueBounds(clientRect);
   int heightBar = Math.Max(28, Math.Min(50, clientSize.Height / 20));
   var textArea = new Rectangle(Math.Max(12, area.Left), Math.Min(clientSize.Height - heightBar - 8, area.Bottom - heightBar), Math.Max(1, area.Width), heightBar);
   using (var brush = new SolidBrush(Color.FromArgb(195, 12, 15, 21)))
    g.FillRectangle(brush, textArea);
   if (settings.ShowErrorImage && ErrorIcon != null) {
    int badgeSize = Math.Max(48, Math.Min(120, clientSize.Height / 9));
    int bx = Math.Max(12, area.Right - badgeSize);
    int by = Math.Max(12, textArea.Top - badgeSize - 10);
    float s = Math.Min((float)badgeSize / ErrorIcon.Width, (float)badgeSize / ErrorIcon.Height);
    int bw = Math.Max(1, (int)(ErrorIcon.Width * s));
    int bh = Math.Max(1, (int)(ErrorIcon.Height * s));
    g.DrawImage(ErrorIcon, bx + (badgeSize - bw) / 2, by + (badgeSize - bh) / 2, bw, bh);
   }
   using (var font = new Font("Microsoft YaHei UI", Math.Max(11, Math.Min(20, clientSize.Height / 55f)), FontStyle.Regular))
    TextRenderer.DrawText(g, "如果你不是机主，请停止操作。", font, textArea, Color.White,
     TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
  }

  void DisposeImages() {
   for (int i = 0; i < Digits.Length; i++) {
    if (Digits[i] != null && !ReferenceEquals(Digits[i], CompleteIcon)) Digits[i].Dispose();
    Digits[i] = null;
   }
   if (CompleteIcon != null) { CompleteIcon.Dispose(); CompleteIcon = null; }
   if (ErrorIcon != null) { ErrorIcon.Dispose(); ErrorIcon = null; }
  }

  public void Dispose() { DisposeImages(); }
 }

 sealed class BackgroundCoverForm : Form {
  public BackgroundCoverForm() { DoubleBuffered = true; ResizeRedraw = true; }
  protected override void OnPaintBackground(PaintEventArgs e) { CoverBackground.Paint(e.Graphics, ClientRectangle); }
 }
}
