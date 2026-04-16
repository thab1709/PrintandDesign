using Printapi2.Models;
using System.Drawing;
using System.Drawing.Printing;
using System.Text.Json;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

public class PrintService
{
    PrintTemplate template;
     float RECEIPT_WIDTH;

    // Designer dùng 96dpi, máy in dùng DPI thực
    // Font dùng GraphicsUnit.Point nên GDI+ tự scale
    // Width/Height từ designer (96dpi) cần convert sang print DPI// Width/Height từ designer (96dpi) cần convert sang print DPI
    float printDpiX = 96f;
    float printDpiY = 96f;
    float printDpiZ = 96f;

    float ToPx(double designerPx, float dpi) =>
        (float)(designerPx / 96.0 * dpi);

    public void Print(string json, string printerName = null)
    {
        template = JsonSerializer.Deserialize<PrintTemplate>(json);

        // 🔥 set width theo paper
        RECEIPT_WIDTH = template.PaperSize == 60 ? 197f : 263f;

        PrintDocument pd = new PrintDocument();

        pd.DefaultPageSettings.PaperSize = new PaperSize(
            "Receipt",
            (int)RECEIPT_WIDTH,
            3000);

        pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        if (!string.IsNullOrWhiteSpace(printerName))
            pd.PrinterSettings.PrinterName = printerName;

        pd.PrintPage += PrintPage;
        pd.Print();
    }

    void PrintPage(object sender, PrintPageEventArgs e)
    {
        Graphics g = e.Graphics;
        printDpiX = g.DpiX;
        printDpiY = g.DpiY;

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        // Gom Text có TextPosition nằm trong span của QR về cùng Index
        MergeQRTextRows(template.Content);

        float y = 0;
        int maxIndex = template.Content.Max(x => x.Index);

        for (int index = 0; index <= maxIndex; index++)
        {
            var row = template.Content.Where(i => i.Index == index).ToList();
            if (!row.Any()) continue;

            float rowH = MeasureRowHeight(g, row);
            DrawRow(g, row, y);
            y += rowH;
        }
    }

    // Designer dùng ROW_HEIGHT = 40px; QR cao H px → chiếm ceil(H/40) rows.
    // Bất kỳ Text có TextPosition nằm trong khoảng đó sẽ được kéo về cùng Index QR.
    void MergeQRTextRows(List<PrintItem> content)
    {
        const int DESIGNER_ROW_H = 40;

        var qrItems = content.Where(i => i.Type == "QR").ToList();

        foreach (var qr in qrItems)
        {
            int qrSize = Math.Clamp((int)Math.Min(qr.Width, qr.Height), 60, (int)RECEIPT_WIDTH);
            int rowSpan = (int)Math.Ceiling(qrSize / (double)DESIGNER_ROW_H);

            for (int offset = 1; offset <= rowSpan; offset++)
            {
                int spanIndex = qr.Index + offset;
                foreach (var item in content.Where(i =>
                    i.Index == spanIndex &&
                    i.Type == "Text" &&
                    !string.IsNullOrEmpty(i.TextPosition)))
                {
                    item.Index = qr.Index; // kéo về cùng hàng QR
                }
            }
        }
    }

    float MeasureRowHeight(Graphics g, List<PrintItem> row)
    {
        if (row.Any(i => i.Type == "Line"))
            return ToPx(8, printDpiY);

        float height = 0;
        foreach (var item in row)
        {
            float h;
            if (item.Type == "Text")
            {
                using Font font = MakeFont(item);
                h = g.MeasureString(
                    string.IsNullOrEmpty(item.Value) ? " " : item.Value,
                    font, (int)RECEIPT_WIDTH).Height;
            }
            else if (item.Type == "Barcode")
            {
                h = Math.Clamp((float)item.Height * 0.5f, 40f, 120f); // sync với DrawBarcode
            }
            else if (item.Type == "QR")
            {
                // Dùng kích thước designer trực tiếp, không ToPx
                h = Math.Clamp((int)Math.Min(item.Width, item.Height), 60, (int)RECEIPT_WIDTH);
            }
            else
            {
                h = ToPx(item.Height, printDpiY);
            }
            height = Math.Max(height, h);
        }
        return height + ToPx(4, printDpiY);
    }

    Font MakeFont(PrintItem item)
    {
        FontStyle style = FontStyle.Regular;
        if (item.Bold) style |= FontStyle.Bold;
        if (item.Italic) style |= FontStyle.Italic;
        return new Font("Arial", Math.Max(item.FontSize, 4f), style, GraphicsUnit.Point);
    }

    float GetZoneX(string zone, float itemW)
    {
        return zone switch
        {
            "Center" => (RECEIPT_WIDTH - itemW) / 2f,
            "Right" => RECEIPT_WIDTH - itemW,
            _ => 0f
        };
    }

    void DrawRow(Graphics g, List<PrintItem> row, float y)
    {
        // LINE
        if (row.Any(i => i.Type == "Line"))
        {
            var line = row.First(i => i.Type == "Line");

            float ly = y + ToPx(3, printDpiY);

            using Pen pen = new Pen(Color.Black, 2);

            // 🎯 APPLY STYLE
            pen.DashStyle = line.LineStyle switch
            {
                "Dashed" => System.Drawing.Drawing2D.DashStyle.Dash,
                "Dotted" => System.Drawing.Drawing2D.DashStyle.Dot,
                "DashDot" => System.Drawing.Drawing2D.DashStyle.DashDot,
                _ => System.Drawing.Drawing2D.DashStyle.Solid
            };

            g.DrawLine(pen, 0, ly, RECEIPT_WIDTH, ly);
            return;
        }

        // IMAGE độc lập
        if (row.Count == 1 && row[0].Type == "Image")
        {
            var item = row[0];

            float maxW = RECEIPT_WIDTH;

            // scale width theo khổ giấy trước
            float w = Math.Min(ToPx(item.Width, printDpiX), maxW);
            float h = ToPx(item.Height, printDpiY);

            // Nếu ảnh quá cao → scale lại
            if (h > 300) // giới hạn chiều cao (tuỳ bạn)
            {
                float ratio = 300f / h;
                h *= ratio;
                w *= ratio;
            }

            float x = GetZoneX(item.Zone, w);
            DrawImage(g, item.Value, x, y, w, h);
            return;
        }

        // BARCODE độc lập
        // Trong DrawRow, phần BARCODE độc lập — giữ nguyên
        if (row.Count == 1 && row[0].Type == "Barcode")
        {
            var item = row[0];

            // Dùng thẳng kích thước designer, không ToPx
            float w = Math.Min((float)item.Width, RECEIPT_WIDTH);
            float h = Math.Clamp((float)item.Height * 0.5f, 40f, 120f);

            float x = GetZoneX(item.Zone, w);

            DrawBarcode(g, item.Value, x, y, (int)w, (int)h);
            return;
        }

        // QR độc lập hoặc QR + Text
        if (row.Any(i => i.Type == "QR"))
        {
            var qrItem = row.First(i => i.Type == "QR");
            var texts = row.Where(i => i.Type == "Text").ToList();

            int size = Math.Clamp((int)Math.Min(qrItem.Width, qrItem.Height), 60, (int)RECEIPT_WIDTH);

            float qrX = GetZoneX(qrItem.Zone, size);

            DrawQR(g, qrItem.Value, qrX, y, size, size);

            // Tính vùng text bên cạnh QR
            float textAreaX, textAreaW;
            if (qrItem.Zone == "Right")
            {
                textAreaX = 0;
                textAreaW = qrX - ToPx(4, printDpiX);
            }
            else if (qrItem.Zone == "Left")
            {
                textAreaX = qrX + size + ToPx(4, printDpiX);
                textAreaW = RECEIPT_WIDTH - textAreaX;
            }
            else
            {
                textAreaX = 0;
                textAreaW = RECEIPT_WIDTH;
            }

            // Nhóm text theo TextPosition
            var topTexts    = texts.Where(t => (t.TextPosition ?? "Middle") == "Top").ToList();
            var middleTexts = texts.Where(t => (t.TextPosition ?? "Middle") == "Middle").ToList();
            var bottomTexts = texts.Where(t => (t.TextPosition ?? "Middle") == "Bottom").ToList();

            float MeasureGroupH(List<PrintItem> group) =>
                group.Sum(t => { using Font f = MakeFont(t); return g.MeasureString(t.Value ?? " ", f).Height; });

            void DrawGroup(List<PrintItem> group, float startY)
            {
                float offsetY = startY;
                foreach (var text in group)
                {
                    using Font font = MakeFont(text);
                    float textH = g.MeasureString(text.Value ?? " ", font).Height;
                    var fmt = new StringFormat
                    {
                        Alignment = qrItem.Zone == "Center" ? StringAlignment.Center : StringAlignment.Near
                    };
                    g.DrawString(text.Value, font, Brushes.Black,
                        new RectangleF(textAreaX, offsetY, textAreaW, textH), fmt);
                    offsetY += textH;
                }
            }

            // Top: stack từ đầu QR xuống
            DrawGroup(topTexts, y);

            // Middle: căn giữa theo tổng chiều cao nhóm
            if (middleTexts.Count > 0)
                DrawGroup(middleTexts, y + (size - MeasureGroupH(middleTexts)) / 2f);

            // Bottom: stack từ cuối QR lên
            if (bottomTexts.Count > 0)
                DrawGroup(bottomTexts, y + size - MeasureGroupH(bottomTexts));

            return;
        }

        // CENTER đơn
        if (row.Count == 1 && row[0].Zone == "Center")
        {
            DrawText(g, row[0], y, StringAlignment.Center);
            return;
        }

        // LEFT / CENTER / RIGHT (text)
        if (row.All(i => i.Type == "Text") && row.Count <= 3)
        {
            var left = row.FirstOrDefault(i => i.Zone == "Left");
            var center = row.FirstOrDefault(i => i.Zone == "Center");
            var right = row.FirstOrDefault(i => i.Zone == "Right");

            if (left != null) DrawText(g, left, y, StringAlignment.Near);
            if (center != null) DrawText(g, center, y, StringAlignment.Center);
            if (right != null) DrawText(g, right, y, StringAlignment.Far);
            return;
        }

        // TABLE — chia đều cột
        float colW = RECEIPT_WIDTH / Math.Max(row.Count, 1);
        float cx = 0;
        foreach (var item in row)
        {
            using Font font = MakeFont(item);
            g.DrawString(item.Value, font, Brushes.Black,
                new RectangleF(cx, y, colW, 200));
            cx += colW;
        }
    }

    void DrawText(Graphics g, PrintItem item, float y, StringAlignment align)
    {
        if (string.IsNullOrEmpty(item.Value)) return;
        using Font font = MakeFont(item);
        g.DrawString(item.Value, font, Brushes.Black,
            new RectangleF(0, y, RECEIPT_WIDTH, 500),
            new StringFormat { Alignment = align });
    }

    void DrawImage(Graphics g, string value, float x, float y, float w, float h)
    {
        if (string.IsNullOrEmpty(value)) return;

        try
        {
            byte[] data;

            if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !File.Exists(value))
                data = Convert.FromBase64String(value);
            else if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var client = new HttpClient();
                data = client.GetByteArrayAsync(value).GetAwaiter().GetResult();
            }
            else
                data = File.ReadAllBytes(value);

            using var ms = new MemoryStream(data);
            using var img = Image.FromStream(ms);

            // 🔥 FIX 1: scale theo DPI thật của ảnh
            float imgW = img.Width * 96f / img.HorizontalResolution;
            float imgH = img.Height * 96f / img.VerticalResolution;

            // 🔥 FIX 2: fit vào box (giữ tỷ lệ)
            float scale = Math.Min(w / imgW, h / imgH);

            float drawW = imgW * scale;
            float drawH = imgH * scale;

            // 🔥 FIX 3: center trong vùng
            float drawX = x + (w - drawW) / 2f;
            float drawY = y + (h - drawH) / 2f;

            // 🔥 FIX 4: chống blur
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            g.DrawImage(img, drawX, drawY, drawW, drawH);
        }
        catch { }
    }

    void DrawBarcode(Graphics g, string value, float x, float y, int w, int h)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Any(c => c > 127)) return;

        try
        {
            // Dùng đúng w và h được truyền vào, không override
            int printW = w;
            int printH = Math.Clamp(h, 30, 120);

            using Bitmap bmp = new BarcodeWriter
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions
                {
                    Width = printW,
                    Height = printH,
                    Margin = 0,
                    PureBarcode = true
                }
            }.Write(value);

            g.DrawImage(bmp, x, y, printW, printH);
        }
        catch { }
    }

    void DrawQR(Graphics g, string value, float x, float y, int w, int h)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            int size = Math.Min(w, h); // đã clamp từ trước

            using Bitmap bmp = new BarcodeWriter
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions
                {
                    Width = size,
                    Height = size,
                    Margin = 0
                }
            }.Write(value);

            g.DrawImage(bmp, x, y, size, size);
        }
        catch { }
    }
}
