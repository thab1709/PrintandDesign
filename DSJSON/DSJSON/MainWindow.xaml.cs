using DSJSON.Models;
using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Security.Policy;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace DSJSON
{
    public partial class MainWindow : Window
    {
        const int ROW_HEIGHT = 40;
        const double MIN_WIDTH = 20;
        const double MIN_HEIGHT = 20;
        int currentPaperSize = 60; 
        Border selectedItem;
        List<Border> selectedItems = new();
       
        bool dragging;
        bool resizing;

        Point start;

        Rectangle selectionBox;
        Point selectionStart;
        bool isSelecting = false;

        public MainWindow()
        {
            
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            this.Loaded += (s, e) =>
            {
                var screen = SystemParameters.WorkArea; // WorkArea = màn hình trừ taskbar

                if (this.Height > screen.Height)
                    this.Height = screen.Height;

                if (this.Width > screen.Width)
                    this.Width = screen.Width;

                // Căn giữa lại sau khi resize
                this.Left = (screen.Width - this.Width) / 2 + screen.Left;
                this.Top = (screen.Height - this.Height) / 2 + screen.Top;
            };

            DesignCanvas.Background = Brushes.White;

            DrawGrid();
            DrawZones();
            this.PreviewKeyDown += Window_KeyDown;
            this.PreviewMouseLeftButtonUp += GlobalMouseUp;
        }
        void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyPaperSize();
            ShowDesignProperties();
        }
        // GRID
        void DrawGrid()
        {
            for (double y = 0; y < DesignCanvas.Height; y += ROW_HEIGHT)
            {
                Line line = new Line
                {
                    X1 = 0,
                    X2 = DesignCanvas.Width,
                    Y1 = y,
                    Y2 = y,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1
                };

                DesignCanvas.Children.Add(line);
            }
        }
        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && selectedItem != null)
            {
                PushUndo();
                foreach (var item in selectedItems.ToList())
                {
                    DesignCanvas.Children.Remove(item);
                }

                selectedItems.Clear();
                selectedItem = null;
                ClearProperties();
            }

            // Phím tắt
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.I)
                {
                    ImportJson_Click(null, null);
                    e.Handled = true;
                }
                else if (e.Key == Key.E)
                {
                    ExportJson_Click(null, null);
                    e.Handled = true;
                }
                else if (e.Key == Key.X)
                {
                    ClearCanvas_Click(null, null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Z)
                {
                    Undo(); e.Handled = true; return;

                }
                else if (e.Key == Key.Y)
                {
                    Redo(); e.Handled = true; return;
                }
                else if (e.Key == Key.D)
                {
                    DuplicateSelectedItem();
                    e.Handled = true;
                }
                else if (e.Key == Key.P)
                {
                    PrintPreview();
                    e.Handled = true;
                }
            }
            // MOVE BY ARROW KEY (SNAP MODE)
            if (selectedItems.Count > 0 &&
                Keyboard.Modifiers == ModifierKeys.None &&
                (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down))
            {
                e.Handled = true;
                PushUndo();

                foreach (var item in selectedItems)
                {
                    var data = item.Tag as DesignerItemData;
                    if (data == null) continue;

                    int index = (int)Math.Round(Canvas.GetTop(item) / ROW_HEIGHT);
                    string zone = GetZone(Canvas.GetLeft(item));

                    switch (e.Key)
                    {
                        case Key.Left:
                            if (data.Type != "Line")
                            {
                                if (zone == "Center") zone = "Left";
                                else if (zone == "Right") zone = "Center";
                            }
                            break;
                        case Key.Right:
                            if (data.Type != "Line")
                            {
                                if (zone == "Left") zone = "Center";
                                else if (zone == "Center") zone = "Right";
                            }
                            break;
                        case Key.Up:
                            index = Math.Max(0, index - 1);
                            break;
                        case Key.Down:
                            index++;
                            break;
                    }

                    Canvas.SetTop(item, index * ROW_HEIGHT);
                    Canvas.SetLeft(item, data.Type == "Line" ? 0 : GetZoneX(zone));
                }

                UpdateProperties();
            }
        }
        void PrintPreview()
        {
            PrintDialog dlg = new PrintDialog();

            if (dlg.ShowDialog() == true)
            {
                // 🔥 tạo document
                FlowDocument doc = new FlowDocument();
                doc.PageWidth = dlg.PrintableAreaWidth;
                doc.PageHeight = dlg.PrintableAreaHeight;
                doc.PagePadding = new Thickness(10);

                // clone canvas thành ảnh
                var bmp = RenderCanvas();

                Image img = new Image
                {
                    Source = bmp,
                    Width = doc.PageWidth
                };

                BlockUIContainer container = new BlockUIContainer(img);
                doc.Blocks.Add(container);

                IDocumentPaginatorSource idpSource = doc;
                dlg.PrintDocument(idpSource.DocumentPaginator, "Receipt Print");
            }
        }
        RenderTargetBitmap RenderCanvas()
        {
            var rtb = new RenderTargetBitmap(
                (int)DesignCanvas.Width,
                (int)DesignCanvas.Height,
                96, 96,
                PixelFormats.Pbgra32);

            rtb.Render(DesignCanvas);
            return rtb;
        }
        void DuplicateSelectedItem()
        {
            if (selectedItems.Count == 0) return;

            PushUndo();

            // deselect originals
            foreach (var it in selectedItems)
                it.BorderBrush = Brushes.DeepSkyBlue;

            var originals = selectedItems.ToList();
            selectedItems.Clear();

            foreach (var source in originals)
            {
                var data = source.Tag as DesignerItemData;
                if (data == null) continue;

                Border newItem = CreateItem(data.Type);
                newItem.Width = source.Width;
                newItem.Height = source.Height;

                var newData = newItem.Tag as DesignerItemData;
                newData.Value = data.Value;

                double left = Canvas.GetLeft(source) + 20;
                double top = Canvas.GetTop(source) + 20;
                if (left + newItem.Width > DesignCanvas.Width) left = 0;
                if (top + newItem.Height > DesignCanvas.Height) top = 0;

                Canvas.SetLeft(newItem, left);
                Canvas.SetTop(newItem, top);

                if (data.Type == "Text")
                {
                    if (source.Child is Grid og && og.Children[0] is TextBlock otb &&
                        newItem.Child is Grid ng && ng.Children[0] is TextBlock ntb)
                    {
                        ntb.Text = otb.Text;
                        ntb.FontSize = otb.FontSize;
                        ntb.FontWeight = otb.FontWeight;
                        ntb.FontStyle = otb.FontStyle;
                    }
                }
                else if (data.Type == "Image" || data.Type == "QR" || data.Type == "Barcode")
                {
                    if (source.Child is Grid og && og.Children[0] is Image oi &&
                        newItem.Child is Grid ng && ng.Children[0] is Image ni)
                        ni.Source = oi.Source;
                }
                else if (data.Type == "Line")
                {
                    newData.LineStyle = data.LineStyle ?? "Solid";
                    if (source.Child is Grid og && og.Children[0] is System.Windows.Shapes.Path op &&
                        newItem.Child is Grid ng && ng.Children[0] is System.Windows.Shapes.Path np)
                        ApplyLineStyle(np, newData.LineStyle);
                }

                DesignCanvas.Children.Add(newItem);
                newItem.BorderBrush = Brushes.Red;
                selectedItems.Add(newItem);
            }

            selectedItem = selectedItems.LastOrDefault();
            UpdateProperties();
        }
        void GlobalMouseUp(object sender, MouseButtonEventArgs e)
        {
            dragging = false;
            resizing = false;

            // Release capture trên tất cả selected items
            foreach (var item in selectedItems)
                item.ReleaseMouseCapture();
        }

        void ClearProperties()
        {
            UpdateItemBadge(null);
            propValue.Text = "";
            propWidth.Text = "";
            propHeight.Text = "";
            propFontSize.Text = "";
            propIndex.Text = "";
            propZone.Text = "";
            propZone.SelectedIndex = -1;
            propLineStyle.SelectedIndex = -1;
            chkBold.IsChecked = false;
            chkItalic.IsChecked = false;
        }

        // ZONES
        void DrawZones()
        {
            double zoneWidth = DesignCanvas.Width / 3;

            for (int i = 1; i < 3; i++)
            {
                Line line = new Line
                {
                    X1 = zoneWidth * i,
                    X2 = zoneWidth * i,
                    Y1 = 0,
                    Y2 = DesignCanvas.Height,
                    Stroke = Brushes.Gray,
                    StrokeDashArray = new DoubleCollection { 4 }
                };

                DesignCanvas.Children.Add(line);
            }
        }

        // TOOLBOX DRAG
        void Toolbox_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Button btn = sender as Button;
            DragDrop.DoDragDrop(btn, btn.Tag.ToString(), DragDropEffects.Copy);
        }

        // DROP ITEM
        void Canvas_Drop(object sender, DragEventArgs e)
        {
            string type = e.Data.GetData(typeof(string)) as string;
            PushUndo();

            Point pos = e.GetPosition(DesignCanvas);

            Border item = CreateItem(type);

            int rowIndex = (int)Math.Round(pos.Y / ROW_HEIGHT);
            Canvas.SetTop(item, rowIndex * ROW_HEIGHT);

            if (type == "Line")
                Canvas.SetLeft(item, 0);
            else
                Canvas.SetLeft(item, GetZoneX(GetZone(pos.X)));

            DesignCanvas.Children.Add(item);

            SelectItem(item);
        }

        // CREATE ITEM
        Border CreateItem(string type)
        {
            Border border = new Border
            {
                BorderBrush = Brushes.DeepSkyBlue,
                BorderThickness = new Thickness(1),
                Width = 150,
                Height = 40,
                Cursor = Cursors.SizeAll
            };

            border.Tag = new DesignerItemData { Type = type };

            Grid grid = new Grid
            {
                Background = Brushes.Transparent // 🔥 để nhận click
            };

            UIElement content = null;

            if (type == "Text")
            {
                content = new TextBlock
                {
                    Text = "Text",
                    Foreground = Brushes.Black,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else if (type == "Line")
            {
                border.Width = DesignCanvas.Width;

                content = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 0,0 L 1,0"),
                    Stroke = Brushes.Black,
                    StrokeThickness = 2,
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                content = new Image
                {
                    Stretch = Stretch.Uniform
                };
            }

            // 🔥 QUAN TRỌNG: disable hit test
            content.IsHitTestVisible = false;

            grid.Children.Add(content);

            Rectangle resizeHandle = new Rectangle
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.DeepSkyBlue,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE
            };

            resizeHandle.MouseLeftButtonDown += ResizeStart;

            grid.Children.Add(resizeHandle);

            border.Child = grid;
            // ─── CONTEXT MENU ───
            var menu = new ContextMenu();

            var menuDuplicate = new MenuItem
            {
                Header = "Duplicate",
                Icon = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Text = "\uE8C8",
                    FontSize = 13
                }
            };
            menuDuplicate.Click += (s, e) => DuplicateSelectedItem();

            var menuDelete = new MenuItem
            {
                Header = "Delete",
                Foreground = Brushes.OrangeRed,
                Icon = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Text = "\uE74D",
                    FontSize = 13,
                    Foreground = Brushes.OrangeRed
                }
            };
            menuDelete.Click += (s, e) =>
            {
                PushUndo();
                foreach (var item in selectedItems.ToList())
                    DesignCanvas.Children.Remove(item);
                selectedItems.Clear();
                selectedItem = null;
                ClearProperties();
            };

            menu.Items.Add(menuDuplicate);
            menu.Items.Add(new Separator());
            menu.Items.Add(menuDelete);

            border.ContextMenu = menu;

            // Khi right click → select item trước
            border.MouseRightButtonDown += (s, e) =>
            {
                // Nếu item chưa được select thì select nó
                if (!selectedItems.Contains(border))
                    SelectItem(border);
            };
            // ────────────────────
            border.MouseLeftButtonDown += ItemClick;
            border.MouseMove += ItemMove;
            border.MouseLeftButtonUp += ItemUp;

            return border;
        }

        // SELECT ITEM
        void SelectItem(Border item)
        {
            bool isCtrl = Keyboard.Modifiers == ModifierKeys.Control;

            if (!isCtrl)
            {
                // clear tất cả nếu không giữ Ctrl
                foreach (var it in selectedItems)
                    it.BorderBrush = Brushes.DeepSkyBlue;

                selectedItems.Clear();

                item.BorderBrush = Brushes.Red;
                selectedItems.Add(item);
            }
            else
            {
                // Ctrl mode → chỉ ADD, không remove
                if (!selectedItems.Contains(item))
                {
                    item.BorderBrush = Brushes.Red;
                    selectedItems.Add(item);
                }
            }

            selectedItem = item; // item vừa click là anchor
            UpdateProperties();
        }

        // CLICK
        void ItemClick(object sender, MouseButtonEventArgs e)
        {
            Border item = sender as Border;

            // 🔥 LUÔN select trước
            SelectItem(item);

            // Đảm bảo keyboard focus về window để phím mũi tên hoạt động
            this.Focus();

            if (e.ClickCount == 2)
            {
                HandleDoubleClick(item);
                e.Handled = true;
                return;
            }

            if (!resizing)
                dragging = true;

            start = e.GetPosition(DesignCanvas);

            item.CaptureMouse();
            e.Handled = true;
        }

        // START RESIZE
        void ResizeStart(object sender, MouseButtonEventArgs e)
        {
            resizing = true;

            start = e.GetPosition(DesignCanvas);

            selectedItem?.CaptureMouse();

            e.Handled = true;
        }

        // MOVE ITEM

        void ItemMove(object sender, MouseEventArgs e)
        {
            if (selectedItems.Count == 0) return;
            if (!dragging && !resizing) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                dragging = false;
                resizing = false;
                (sender as Border)?.ReleaseMouseCapture();
                return;
            }

            Point pos = e.GetPosition(DesignCanvas);
            double dx = pos.X - start.X;
            double dy = pos.Y - start.Y;

            if (dragging)
            {
                foreach (var item in selectedItems)
                {
                    var itemData = item.Tag as DesignerItemData;
                    if (itemData.Type == "Line")
                    {
                        Canvas.SetLeft(item, 0);
                        Canvas.SetTop(item, Math.Max(0, Canvas.GetTop(item) + dy));
                    }
                    else
                    {
                        double newLeft = Canvas.GetLeft(item) + dx;
                        double newTop = Canvas.GetTop(item) + dy;
                        newLeft = Math.Max(0, Math.Min(newLeft, DesignCanvas.Width - item.Width));
                        newTop = Math.Max(0, newTop);
                        Canvas.SetLeft(item, newLeft);
                        Canvas.SetTop(item, newTop);
                    }
                }
                UpdateProperties();
            }

            if (resizing)
            {
                var data = selectedItem.Tag as DesignerItemData;
                double newHeight = selectedItem.Height + dy;

                if (data.Type == "Line")
                {
                    selectedItem.Width = DesignCanvas.Width;
                    start = pos;
                    return;
                }

                double left = Canvas.GetLeft(selectedItem);
                double top = Canvas.GetTop(selectedItem);

                // 🔥 giới hạn theo canvas
                double maxWidth = DesignCanvas.Width - left;
                double maxHeight = DesignCanvas.Height - top;

                double newWidth = selectedItem.Width + dx;
                

                selectedItem.Width = Math.Max(MIN_WIDTH, Math.Min(newWidth, maxWidth));
                selectedItem.Height = Math.Max(MIN_HEIGHT, Math.Min(newHeight, maxHeight));

                if (selectedItem.Child is Grid g && g.Children[0] is Image img)
                {
                    if (data.Type == "QR")
                        img.Source = GenerateQR(data.Value, (int)selectedItem.Width, (int)selectedItem.Height);
                    else if (data.Type == "Barcode")
                        img.Source = GenerateBarcode(data.Value, (int)selectedItem.Width, (int)selectedItem.Height);
                }

                UpdateProperties();
            }

            start = pos;
        }

        // RELEASE
        void ItemUp(object sender, MouseButtonEventArgs e)
        {
            if (dragging || resizing) PushUndo();

            dragging = false;
            resizing = false;

            foreach (var item in selectedItems)
            {
                double y = Canvas.GetTop(item);
                int index = (int)Math.Round(y / ROW_HEIGHT);
                Canvas.SetTop(item, index * ROW_HEIGHT);

                var itemData = item.Tag as DesignerItemData;
                if (itemData.Type != "Line")
                {
                    double x = Canvas.GetLeft(item);
                    Canvas.SetLeft(item, GetZoneX(GetZone(x)));
                }
                else
                {
                    Canvas.SetLeft(item, 0);
                    item.Width = DesignCanvas.Width;
                }
            }

            UpdateProperties();
            (sender as Border)?.ReleaseMouseCapture();
        }

        // DOUBLE CLICK
        void HandleDoubleClick(Border item)
        {
            var data = item.Tag as DesignerItemData;

            if (data == null) return;

            if (data.Type == "Text")
            {
                string oldValue = data.Value; // lưu lại value cũ

                string val = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter text",
                    "Edit Text",
                    oldValue // 🔥 show value cũ luôn
                );

                // ❌ Cancel hoặc không nhập gì → giữ nguyên
                if (string.IsNullOrWhiteSpace(val))
                {
                    return;
                }

                // ✅ chỉ update khi có nhập thật
                if (item.Child is Grid g && g.Children[0] is TextBlock tb)
                {
                    tb.Text = val;
                    data.Value = val;
                }

                UpdateProperties();
            }

            if (data.Type == "Image")
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp";

                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        // Đọc file và convert sang base64
                        byte[] fileBytes = File.ReadAllBytes(dlg.FileName);
                        string base64 = Convert.ToBase64String(fileBytes);

                        // Lưu value dạng base64
                        data.Value = base64;
                        propValue.Text = base64;

                        // Load ảnh từ bytes để hiển thị
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = new MemoryStream(fileBytes);
                        bitmap.EndInit();
                        bitmap.Freeze();

                        Image img = new Image
                        {
                            Source = bitmap,
                            Stretch = Stretch.Uniform,
                            IsHitTestVisible = false
                        };

                        if (item.Child is Grid g)
                        {
                            var resizeHandle = g.Children.OfType<Rectangle>()
                                                .FirstOrDefault(r => r.Cursor == Cursors.SizeNWSE);
                            g.Children.Clear();
                            g.Children.Add(img);
                            if (resizeHandle != null) g.Children.Add(resizeHandle);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Image load error: " + ex.Message);
                    }
                }
            }
        }

        // UPDATE PROPERTY PANEL

        void UpdateProperties()
        {
            if (selectedItem == null) return;

            if (selectedItems.Count > 1)
            {
                UpdatePropertiesMulti();
                return;
            }

            _updatingProperties = true;

            // Restore zone dropdown if it was replaced by paper size options
            if (propZone.Items.Count == 0 || (propZone.Items[0] as ComboBoxItem)?.Content?.ToString() != "Left")
            {
                propZone.Items.Clear();
                propZone.Items.Add(new ComboBoxItem { Content = "Left" });
                propZone.Items.Add(new ComboBoxItem { Content = "Center" });
                propZone.Items.Add(new ComboBoxItem { Content = "Right" });
                lblZone.Text = "ZONE";
            }

            propWidth.Text = selectedItem.Width.ToString("0");
            propHeight.Text = selectedItem.Height.ToString("0");

            double x = Canvas.GetLeft(selectedItem);
            double y = Canvas.GetTop(selectedItem);

            propIndex.Text = ((int)(y / ROW_HEIGHT)).ToString();

            // Set ComboBox zone
            string zone = GetZone(x);
            foreach (ComboBoxItem item in propZone.Items)
            {
                if (item.Content.ToString() == zone)
                {
                    propZone.SelectedItem = item;
                    break;
                }
            }

            var data = selectedItem.Tag as DesignerItemData;
            if (data == null) return;
            UpdateItemBadge(data.Type);

            UpdatePropertyVisibility(data.Type);

            propValue.Text = data.Value;

            if (data.Type == "Text")
            {
                if (selectedItem.Child is Grid g && g.Children[0] is TextBlock tb)
                {
                    propFontSize.Text = tb.FontSize.ToString();

                    // Các lệnh này sẽ trigger FontPropertyChanged,
                    // nhưng nhờ flag ở trên nên nó sẽ return ngay lập tức.
                    chkBold.IsChecked = tb.FontWeight == FontWeights.Bold;
                    chkItalic.IsChecked = tb.FontStyle == FontStyles.Italic;
                }

                string pos = data.TextPosition ?? "Middle";
                foreach (ComboBoxItem ci in propTextPosition.Items)
                {
                    if (ci.Content.ToString() == pos) { propTextPosition.SelectedItem = ci; break; }
                }
            }

            if (data.Type == "Line")
            {
                string ls = data.LineStyle ?? "Solid";
                foreach (ComboBoxItem ci in propLineStyle.Items)
                {
                    if (ci.Content.ToString() == ls)
                    {
                        propLineStyle.SelectedItem = ci;
                        break;
                    }
                }
            }
            _updatingProperties = false;
        }

        void UpdatePropertiesMulti()
        {
            _updatingProperties = true;

            var items    = selectedItems;
            var dataList = items.Select(i => i.Tag as DesignerItemData).Where(d => d != null).ToList();
            var types    = dataList.Select(d => d.Type).Distinct().ToList();

            bool allText   = types.All(t => t == "Text");
            bool allLine   = types.All(t => t == "Line");
            bool anyLine   = types.Any(t => t == "Line");

            // Restore zone dropdown
            if (propZone.Items.Count == 0 || (propZone.Items[0] as ComboBoxItem)?.Content?.ToString() != "Left")
            {
                propZone.Items.Clear();
                propZone.Items.Add(new ComboBoxItem { Content = "Left" });
                propZone.Items.Add(new ComboBoxItem { Content = "Center" });
                propZone.Items.Add(new ComboBoxItem { Content = "Right" });
                lblZone.Text = "ZONE";
            }

            UpdateItemBadge($"{items.Count} items");

            // Determine which sections are visible
            bool showValue     = !anyLine && types.Count == 1;
            bool showFont      = allText;
            bool showLineStyle = allLine;
            bool showZoneSize  = !anyLine;

            propValue.Visibility     = showValue     ? Visibility.Visible : Visibility.Collapsed;
            lblValue.Visibility      = showValue     ? Visibility.Visible : Visibility.Collapsed;

            propFontSize.Visibility  = showFont      ? Visibility.Visible : Visibility.Collapsed;
            chkBold.Visibility       = showFont      ? Visibility.Visible : Visibility.Collapsed;
            chkItalic.Visibility     = showFont      ? Visibility.Visible : Visibility.Collapsed;
            lblFontSize.Visibility   = showFont      ? Visibility.Visible : Visibility.Collapsed;

            lblLineStyle.Visibility  = showLineStyle ? Visibility.Visible : Visibility.Collapsed;
            propLineStyle.Visibility = showLineStyle ? Visibility.Visible : Visibility.Collapsed;

            propZone.Visibility      = showZoneSize  ? Visibility.Visible : Visibility.Collapsed;
            lblZone.Visibility       = showZoneSize  ? Visibility.Visible : Visibility.Collapsed;
            propWidth.Visibility     = showZoneSize  ? Visibility.Visible : Visibility.Collapsed;
            propHeight.Visibility    = showZoneSize  ? Visibility.Visible : Visibility.Collapsed;
            lblWidth.Visibility      = showZoneSize  ? Visibility.Visible : Visibility.Collapsed;
            lblHeight.Visibility     = showZoneSize  ? Visibility.Visible : Visibility.Collapsed;

            lblIndex.Visibility  = Visibility.Visible;
            propIndex.Visibility = Visibility.Visible;

            // Common values
            if (showZoneSize)
            {
                var widths  = items.Select(i => i.Width).Distinct().ToList();
                propWidth.Text = widths.Count == 1 ? widths[0].ToString("0") : "";

                var heights = items.Select(i => i.Height).Distinct().ToList();
                propHeight.Text = heights.Count == 1 ? heights[0].ToString("0") : "";

                var zones = items.Select(i => GetZone(Canvas.GetLeft(i))).Distinct().ToList();
                if (zones.Count == 1)
                {
                    foreach (ComboBoxItem ci in propZone.Items)
                        if (ci.Content.ToString() == zones[0]) { propZone.SelectedItem = ci; break; }
                }
                else
                    propZone.SelectedIndex = -1;
            }

            var indices = items.Select(i => (int)(Canvas.GetTop(i) / ROW_HEIGHT)).Distinct().ToList();
            propIndex.Text = indices.Count == 1 ? indices[0].ToString() : "";

            if (showValue)
            {
                var values = dataList.Select(d => d.Value).Distinct().ToList();
                propValue.Text = values.Count == 1 ? values[0] : "";
            }

            if (showFont)
            {
                var fontSizes = items
                    .Select(i => (i.Child is Grid g && g.Children[0] is TextBlock tb) ? (double?)tb.FontSize : null)
                    .Where(f => f != null).Select(f => f!.Value).Distinct().ToList();
                propFontSize.Text = fontSizes.Count == 1 ? fontSizes[0].ToString() : "";

                var bolds = items
                    .Select(i => (i.Child is Grid g && g.Children[0] is TextBlock tb) ? (bool?)(tb.FontWeight == FontWeights.Bold) : null)
                    .Where(b => b != null).Select(b => b!.Value).Distinct().ToList();
                chkBold.IsChecked = bolds.Count == 1 ? bolds[0] : (bool?)null;

                var italics = items
                    .Select(i => (i.Child is Grid g && g.Children[0] is TextBlock tb) ? (bool?)(tb.FontStyle == FontStyles.Italic) : null)
                    .Where(it => it != null).Select(it => it!.Value).Distinct().ToList();
                chkItalic.IsChecked = italics.Count == 1 ? italics[0] : (bool?)null;
            }

            if (showLineStyle)
            {
                var lineStyles = dataList.Select(d => d.LineStyle ?? "Solid").Distinct().ToList();
                if (lineStyles.Count == 1)
                {
                    foreach (ComboBoxItem ci in propLineStyle.Items)
                        if (ci.Content.ToString() == lineStyles[0]) { propLineStyle.SelectedItem = ci; break; }
                }
                else
                    propLineStyle.SelectedIndex = -1;
            }

            _updatingProperties = false;
        }

        bool _updatingProperties = false;

        // ─── ZOOM ──────────────────────────────────────────────────
        double _zoomLevel = 1.0;
        bool _updatingZoom = false;

        void SetZoom(double zoom)
        {
            if (canvasScale == null) return; // chưa init xong XAML

            zoom = Math.Max(0.10, Math.Min(3.00, zoom));
            _zoomLevel = zoom;

            canvasScale.ScaleX = zoom;
            canvasScale.ScaleY = zoom;

            _updatingZoom = true;
            zoomSlider.Value = Math.Round(zoom * 100);
            lblZoomLevel.Text = $"{(int)(zoom * 100)}%";
            _updatingZoom = false;
        }

        void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            e.Handled = true;
            double step = e.Delta > 0 ? 0.1 : -0.1;
            SetZoom(_zoomLevel + step);
        }

        void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingZoom) return;
            SetZoom(zoomSlider.Value / 100.0);
        }

        void ZoomIn_Click(object sender, RoutedEventArgs e)    => SetZoom(_zoomLevel + 0.1);
        void ZoomOut_Click(object sender, RoutedEventArgs e)   => SetZoom(_zoomLevel - 0.1);
        void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);
        // ───────────────────────────────────────────────────────────

        void PropZone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Không xử lý khi đang update từ code
            if (_updatingProperties) return;
            if (selectedItem == null)
            {
                // 👉 đang chỉnh DESIGN
                if (propZone.SelectedItem is ComboBoxItem ps)
                {
                    string val = ps.Content.ToString();

                    currentPaperSize = val.Contains("60") ? 60 : 80;

                    ApplyPaperSize();
                }
                this.Focus();
                return;
            }
            if (propZone.SelectedItem is not ComboBoxItem selected) return;

            if (selectedItems.Count > 1)
            {
                string mZone = selected.Content.ToString();
                double mX = GetZoneX(mZone);
                foreach (var item in selectedItems)
                {
                    var d = item.Tag as DesignerItemData;
                    if (d?.Type != "Line")
                        Canvas.SetLeft(item, mX);
                }
                this.Focus();
                return;
            }

            var data = selectedItem.Tag as DesignerItemData;
            if (data == null || data.Type == "Line") return;

            string zone = selected.Content.ToString();
            double newX = GetZoneX(zone);
            Canvas.SetLeft(selectedItem, newX);
            this.Focus();
        }
        void ApplyPaperSize()
        {
            // 1mm ≈ 3.78 px
            double newWidth = currentPaperSize * 3.78;

            // Lưu zone của từng item TRƯỚC khi thay đổi Width
            var itemZones = new Dictionary<Border, string>();
            foreach (var child in DesignCanvas.Children)
            {
                if (child is not Border item) continue;
                var data = item.Tag as DesignerItemData;
                if (data == null || data.Type == "Line") continue;
                itemZones[item] = GetZone(Canvas.GetLeft(item));
            }

            DesignCanvas.Width = newWidth;

            // Reposition toàn bộ item theo zone đã lưu
            foreach (var child in DesignCanvas.Children)
            {
                if (child is not Border item) continue;

                var data = item.Tag as DesignerItemData;
                if (data == null) continue;

                double y = Canvas.GetTop(item);
                int index = (int)Math.Round(y / ROW_HEIGHT);
                Canvas.SetTop(item, index * ROW_HEIGHT);

                if (data.Type != "Line")
                {
                    string zone = itemZones.TryGetValue(item, out var z) ? z : "Left";
                    Canvas.SetLeft(item, GetZoneX(zone));
                }
                else
                {
                    Canvas.SetLeft(item, 0);
                    item.Width = DesignCanvas.Width;
                }
            }

            RedrawZones();
        }
        void RedrawZones()
        {
            var oldLines = DesignCanvas.Children.OfType<Line>().ToList();

            foreach (var l in oldLines)
                DesignCanvas.Children.Remove(l);

            DrawGrid();
            DrawZones();
        }
        // APPLY PROPERTY
        void ApplyProperty_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItem == null) return;

            if (selectedItems.Count > 1)
            {
                bool hasW  = double.TryParse(propWidth.Text,  out double mw);
                bool hasH  = double.TryParse(propHeight.Text, out double mh);
                bool hasFs = int.TryParse(propFontSize.Text,  out int mfs);

                foreach (var item in selectedItems)
                {
                    var d = item.Tag as DesignerItemData;
                    if (d == null || d.Type == "Line") continue;

                    if (hasW) item.Width  = Math.Max(mw, MIN_WIDTH);
                    if (hasH) item.Height = Math.Max(mh, MIN_HEIGHT);

                    if (showValue(d.Type) && !string.IsNullOrEmpty(propValue.Text))
                        d.Value = propValue.Text;

                    if (d.Type == "Text" && item.Child is Grid g && g.Children[0] is TextBlock tb)
                    {
                        if (hasFs) tb.FontSize = mfs;
                        if (chkBold.IsChecked.HasValue)
                            tb.FontWeight = chkBold.IsChecked.Value ? FontWeights.Bold : FontWeights.Regular;
                        if (chkItalic.IsChecked.HasValue)
                            tb.FontStyle = chkItalic.IsChecked.Value ? FontStyles.Italic : FontStyles.Normal;
                    }
                }

                UpdatePropertiesMulti();
                return;

                bool showValue(string type) => type != "Image" && type != "Line";
            }

            if (double.TryParse(propWidth.Text, out double w))
                selectedItem.Width = Math.Max(w, MIN_WIDTH);

            if (double.TryParse(propHeight.Text, out double h))
                selectedItem.Height = Math.Max(h, MIN_HEIGHT);

            var data = selectedItem.Tag as DesignerItemData;

            if (data == null) return;

            data.Value = propValue.Text;

            if (data.Type == "Text")
            {
                if (selectedItem.Child is Grid g && g.Children[0] is TextBlock tb)
                {
                    tb.Text = propValue.Text;

                    if (int.TryParse(propFontSize.Text, out int fs))
                        tb.FontSize = fs;

                    tb.FontWeight = chkBold.IsChecked == true ? FontWeights.Bold : FontWeights.Regular;
                    tb.FontStyle = chkItalic.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
                }
            }

            UpdateProperties();
        }

        // EXPORT JSON
        void ExportJson_Click(object sender, RoutedEventArgs e)
        {
            List<PrintItem> list = new();

            foreach (var child in DesignCanvas.Children)
            {
                if (child is Border item)
                {
                    double x = Canvas.GetLeft(item);
                    double y = Canvas.GetTop(item);

                    var data = item.Tag as DesignerItemData;

                    var printItem = new PrintItem
                    {
                        Index = (int)(y / ROW_HEIGHT),
                        Zone = GetZone(x),
                        Type = data.Type,
                        Value = data.Value,
                        Width = item.Width,
                        Height = item.Height,
                        LineStyle = data.Type == "Line" ? data.LineStyle : null,
                        TextPosition = data.Type == "Text" ? data.TextPosition : null
                    };

                    if (data.Type == "Text")
                    {
                        if (item.Child is Grid g && g.Children[0] is TextBlock tb)
                        {
                            printItem.FontSize = (int)tb.FontSize;
                            printItem.Bold = tb.FontWeight == FontWeights.Bold;
                            printItem.Italic = tb.FontStyle == FontStyles.Italic;
                        }
                    }

                    list.Add(printItem);
                }
            }

            var template = new PrintTemplate
            {
                PaperSize = currentPaperSize,
                Content = list
            };

            string json = JsonSerializer.Serialize(
                template,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            SaveFileDialog dlg = new SaveFileDialog
            {
                FileName = "template.json",
                Filter = "Json File|*.json"
            };

            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, json);
            }
        }

        // IMPORT JSON
        async void ImportJson_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "Json|*.json";

            if (dlg.ShowDialog() != true) return;

            string json = File.ReadAllText(dlg.FileName);
            var template = JsonSerializer.Deserialize<PrintTemplate>(json);

            if (template == null || template.Content == null) return;

            var oldItems = DesignCanvas.Children.OfType<Border>().ToList();
            foreach (var i in oldItems)
                DesignCanvas.Children.Remove(i);

            foreach (var item in template.Content)
            {
                Border d = CreateItem(item.Type);
                d.Width = item.Width;
                d.Height = item.Height;

                double x = GetZoneX(item.Zone);
                double y = item.Index * ROW_HEIGHT;

                Canvas.SetLeft(d, x);
                Canvas.SetTop(d, y);

                if (d.Tag is DesignerItemData data)
                {
                    data.Value = item.Value;
                    if (item.Type == "Text")
                        data.TextPosition = item.TextPosition ?? "Middle";
                }

                if (d.Child is Grid g)
                {
                    if (item.Type == "Text" && g.Children[0] is TextBlock tb)
                    {
                        tb.Text = item.Value;
                        tb.FontSize = item.FontSize;
                        tb.FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Regular;
                        tb.FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal;
                    }

                    if (item.Type == "Image")
                    {
                        BitmapImage bitmap = IsBase64(item.Value)
                            ? LoadFromBase64(item.Value)
                            : await LoadImageAsync(item.Value);

                        if (bitmap != null)
                        {
                            Image img = new Image
                            {
                                Source = bitmap,
                                Stretch = Stretch.Uniform,
                                IsHitTestVisible = false
                            };

                            g.Children.Clear();
                            g.Children.Add(img);
                            g.Children.Add(CreateResizeHandle());
                        }
                    }

                    if (item.Type == "QR" && g.Children[0] is Image qr)
                        qr.Source = GenerateQR(item.Value, (int)item.Width, (int)item.Height);

                    if (item.Type == "Barcode" && g.Children[0] is Image bc)
                        bc.Source = GenerateBarcode(item.Value, (int)item.Width, (int)item.Height);

                    if (item.Type == "Line" && g.Children[0] is System.Windows.Shapes.Path lp)
                    {
                        if (d.Tag is DesignerItemData ld) ld.LineStyle = item.LineStyle ?? "Solid";
                        ApplyLineStyle(lp, item.LineStyle ?? "Solid");
                    }
                }

                DesignCanvas.Children.Add(d);
            }

            currentPaperSize = template.PaperSize == 80 ? 80 : 60;
            ApplyPaperSize();
        }
        Rectangle CreateResizeHandle()
        {
            var handle = new Rectangle
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.DeepSkyBlue,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE
            };

            handle.MouseLeftButtonDown += ResizeStart;

            return handle;
        }
        double GetZoneX(string zone)
        {
            double zoneWidth = DesignCanvas.Width / 3;

            if (zone == "Left")
                return 0;

            if (zone == "Center")
                return zoneWidth;

            if (zone == "Right")
                return zoneWidth * 2;

            return 0;
        }
        void ApplyLineStyle(System.Windows.Shapes.Path path, string style)
        {
            path.StrokeDashArray = style switch
            {
                "Dashed"  => new DoubleCollection { 6, 3 },
                "Dotted"  => new DoubleCollection { 1, 3 },
                "DashDot" => new DoubleCollection { 6, 3, 1, 3 },
                _         => null   // Solid
            };
        }
        void PropLineStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingProperties) return;
            if (selectedItem == null) return;
            if (propLineStyle.SelectedItem is not ComboBoxItem selected) return;

            PushUndo();
            string style = selected.Content.ToString();

            foreach (var item in selectedItems)
            {
                var data = item.Tag as DesignerItemData;
                if (data?.Type != "Line") continue;

                data.LineStyle = style;

                if (item.Child is Grid g && g.Children[0] is System.Windows.Shapes.Path path)
                    ApplyLineStyle(path, data.LineStyle);
            }
            this.Focus();
        }

        void PropTextPosition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingProperties) return;
            if (selectedItem == null) return;
            if (propTextPosition.SelectedItem is not ComboBoxItem selected) return;

            string pos = selected.Content.ToString();
            foreach (var item in selectedItems)
            {
                var data = item.Tag as DesignerItemData;
                if (data?.Type == "Text")
                    data.TextPosition = pos;
            }
            this.Focus();
        }
        private CancellationTokenSource _imageLoadCts;
        void PropSize_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (selectedItem == null) return;

            if (sender == propWidth)
            {
                if (double.TryParse(propWidth.Text, out double w))
                {
                    foreach (var item in selectedItems)
                    {
                        var d = item.Tag as DesignerItemData;
                        if (d == null || d.Type == "Line") continue;
                        item.Width = Math.Max(w, MIN_WIDTH);

                        // 🔥 Regenerate QR/Barcode theo size mới
                        if (item.Child is Grid g && g.Children[0] is Image img)
                        {
                            if (d.Type == "QR")
                                img.Source = GenerateQR(d.Value, (int)item.Width, (int)item.Height);
                            else if (d.Type == "Barcode")
                                img.Source = GenerateBarcode(d.Value, (int)item.Width, (int)item.Height);
                        }
                    }
                }
            }
            else if (sender == propHeight)
            {
                if (double.TryParse(propHeight.Text, out double h))
                {
                    foreach (var item in selectedItems)
                    {
                        var d = item.Tag as DesignerItemData;
                        if (d == null || d.Type == "Line") continue;
                        item.Height = Math.Max(h, MIN_HEIGHT);

                        // 🔥 Regenerate QR/Barcode theo size mới
                        if (item.Child is Grid g && g.Children[0] is Image img)
                        {
                            if (d.Type == "QR")
                                img.Source = GenerateQR(d.Value, (int)item.Width, (int)item.Height);
                            else if (d.Type == "Barcode")
                                img.Source = GenerateBarcode(d.Value, (int)item.Width, (int)item.Height);
                        }
                    }
                }
            }
        }
        async void PropValue_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (selectedItem == null) return;

            var data = selectedItem.Tag as DesignerItemData;
            if (data == null) return;

            data.Value = propValue.Text;

            // Áp dụng cho tất cả selectedItems cùng type (multi-select)
            foreach (var item in selectedItems)
            {
                if (item == selectedItem) continue;
                var d = item.Tag as DesignerItemData;
                if (d == null || d.Type != data.Type) continue;

                d.Value = propValue.Text;

                if (item.Child is Grid gi)
                {
                    if (d.Type == "Text" && gi.Children[0] is TextBlock tbi)
                        tbi.Text = propValue.Text;
                    else if (d.Type == "QR" && gi.Children[0] is Image qri)
                        qri.Source = GenerateQR(propValue.Text, (int)item.Width, (int)item.Height);
                    else if (d.Type == "Barcode" && gi.Children[0] is Image bci)
                    {
                        string filtered = new string(propValue.Text.Where(c => c <= 127).ToArray());
                        d.Value = filtered;
                        bci.Source = GenerateBarcode(filtered, (int)item.Width, (int)item.Height);
                    }
                }
            }

            if (selectedItem.Child is Grid g)
            {
                if (data.Type == "Text" && g.Children[0] is TextBlock tb)
                {
                    tb.Text = propValue.Text;
                }

                if (data.Type == "QR")
                {
                    if (g.Children[0] is Image img)
                        img.Source = GenerateQR(
                            propValue.Text,
                            (int)selectedItem.Width,
                            (int)selectedItem.Height);
                }

                if (data.Type == "Barcode")
                {
                    if (g.Children[0] is Image img)
                    {
                        // Lọc im lặng, không warning
                        string filtered = new string(propValue.Text.Where(c => c <= 127).ToArray());
                        if (filtered != propValue.Text)
                        {
                            propValue.TextChanged -= PropValue_TextChanged;
                            propValue.Text = filtered;
                            propValue.CaretIndex = filtered.Length;
                            propValue.TextChanged += PropValue_TextChanged;
                            return; // data.Value đã bị set sai, gán lại
                        }

                        data.Value = filtered;
                        img.Source = GenerateBarcode(filtered, (int)selectedItem.Width, (int)selectedItem.Height);
                    }
                }

                if (data.Type == "Image")
                {
                    // Nếu value rỗng → xóa ảnh ngay lập tức
                    if (string.IsNullOrWhiteSpace(data.Value))
                    {
                        if (selectedItem.Child is Grid gClear)
                        {
                            var resizeHandle = gClear.Children.OfType<Rectangle>()
                                                .FirstOrDefault(r => r.Cursor == Cursors.SizeNWSE);
                            gClear.Children.Clear();
                            Image placeholder = new Image { IsHitTestVisible = false };
                            gClear.Children.Add(placeholder);
                            if (resizeHandle != null) gClear.Children.Add(resizeHandle);
                        }
                        return;
                    }

                    // Debounce: hủy request cũ nếu user vẫn đang gõ
                    _imageLoadCts?.Cancel();
                    _imageLoadCts = new CancellationTokenSource();
                    var token = _imageLoadCts.Token;

                    // Xóa ảnh cũ ngay lập tức, hiện placeholder trong khi chờ
                    if (g.Children[0] is Image oldImg)
                        oldImg.Source = null;

                    // Chờ 400ms sau khi ngừng gõ mới load
                    try { await Task.Delay(400, token); }
                    catch (TaskCanceledException) { return; }

                    if (token.IsCancellationRequested) return;

                    BitmapImage bmp;
                    if (IsBase64(data.Value))
                        bmp = LoadFromBase64(data.Value);
                    else
                        bmp = await LoadImageAsync(data.Value);

                    if (token.IsCancellationRequested) return;

                    // Nếu load thất bại → ảnh vẫn null (trống), không restore ảnh cũ
                    var resizeHandleFinal = g.Children.OfType<Rectangle>()
                                            .FirstOrDefault(r => r.Cursor == Cursors.SizeNWSE);

                    Image imgNew = new Image
                    {
                        Source = bmp, // null nếu load thất bại → hiện trống
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false
                    };

                    g.Children.Clear();
                    g.Children.Add(imgNew);
                    if (resizeHandleFinal != null) g.Children.Add(resizeHandleFinal);
                }
            }
        }
        bool IsBase64(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
            if (File.Exists(s)) return false;
            if (s.Length % 4 != 0) return false;
            try { Convert.FromBase64String(s); return true; }
            catch { return false; }
        }
        BitmapImage LoadFromBase64(string base64)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }
        void FontPropertyChanged(object sender, RoutedEventArgs e)
        {
            if (_updatingProperties || selectedItem == null) return;

            PushUndo();

            foreach (var item in selectedItems)
            {
                var data = item.Tag as DesignerItemData;
                if (data == null || data.Type != "Text") continue;

                if (item.Child is Grid g && g.Children[0] is TextBlock tb)
                {
                    if (int.TryParse(propFontSize.Text, out int fs))
                        tb.FontSize = fs;

                    tb.FontWeight = chkBold.IsChecked == true ? FontWeights.Bold : FontWeights.Regular;
                    tb.FontStyle = chkItalic.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
                }
            }
        }
        void UpdatePropertyVisibility(string type)
        {
            // reset — show all
            propValue.Visibility = Visibility.Visible;
            propFontSize.Visibility = Visibility.Visible;
            chkBold.Visibility = Visibility.Visible;
            chkItalic.Visibility = Visibility.Visible;
            propZone.Visibility = Visibility.Visible;
            propWidth.Visibility = Visibility.Visible;
            propHeight.Visibility = Visibility.Visible;
            lblValue.Visibility = Visibility.Visible;
            lblFontSize.Visibility = Visibility.Visible;
            lblWidth.Visibility = Visibility.Visible;
            lblHeight.Visibility = Visibility.Visible;
            lblZone.Visibility = Visibility.Visible;
            lblIndex.Visibility = Visibility.Visible;
            propIndex.Visibility = Visibility.Visible;

            // LineStyle hidden by default, shown only for Line
            lblLineStyle.Visibility = Visibility.Collapsed;
            propLineStyle.Visibility = Visibility.Collapsed;

            // TextPosition hidden by default, shown only for Text
            lblTextPosition.Visibility = Visibility.Collapsed;
            propTextPosition.Visibility = Visibility.Collapsed;

            if (type == "Line")
            {
                propValue.Visibility = Visibility.Collapsed;
                propFontSize.Visibility = Visibility.Collapsed;
                chkBold.Visibility = Visibility.Collapsed;
                chkItalic.Visibility = Visibility.Collapsed;
                propWidth.Visibility = Visibility.Collapsed;
                propHeight.Visibility = Visibility.Collapsed;
                propZone.Visibility = Visibility.Collapsed;

                lblValue.Visibility = Visibility.Collapsed;
                lblFontSize.Visibility = Visibility.Collapsed;
                lblWidth.Visibility = Visibility.Collapsed;
                lblHeight.Visibility = Visibility.Collapsed;
                lblZone.Visibility = Visibility.Collapsed;

                lblLineStyle.Visibility = Visibility.Visible;
                propLineStyle.Visibility = Visibility.Visible;
            }

            if (type == "Image" || type == "QR" || type == "Barcode")
            {
                propFontSize.Visibility = Visibility.Collapsed;
                chkBold.Visibility = Visibility.Collapsed;
                chkItalic.Visibility = Visibility.Collapsed;

                lblFontSize.Visibility = Visibility.Collapsed;
            }

            if (type == "Text")
            {
                lblTextPosition.Visibility = Visibility.Visible;
                propTextPosition.Visibility = Visibility.Visible;
            }
        }
        void ClearCanvas_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Clear all items?",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            // Nếu người dùng KHÔNG nhấn Yes thì thoát hàm luôn (return)
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // Nếu nhấn Yes thì mới chạy xuống đây
            PushUndo(); // Lưu trạng thái trước khi xóa để có thể Ctrl+Z

            var items = DesignCanvas.Children
                .OfType<Border>()
                .ToList();

            foreach (var item in items)
            {
                DesignCanvas.Children.Remove(item);
            }

            selectedItem = null;
            ClearProperties();
        }

        string GetZone(double x)
        {
            double zoneWidth = DesignCanvas.Width / 3;

            if (x < zoneWidth)
                return "Left";

            if (x < zoneWidth * 2)
                return "Center";

            return "Right";
        }
        BitmapImage GenerateQR(string text, int width = 200, int height = 200)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = 1,
                    PureBarcode = false
                },
                Renderer = new PixelDataRenderer
                {
                    Foreground = new PixelDataRenderer.Color { R = 0, G = 0, B = 0, A = 255 },
                    Background = new PixelDataRenderer.Color { R = 255, G = 255, B = 255, A = 255 }
                }
            };

            var pixelData = writer.Write(text);

            var bitmap = BitmapSource.Create(
                pixelData.Width, pixelData.Height,
                96, 96,
                PixelFormats.Bgr32,
                null,
                pixelData.Pixels,
                pixelData.Width * 4);  // stride = width * 4 bytes (Bgr32)

            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using MemoryStream ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;

            BitmapImage img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = ms;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }

        BitmapImage GenerateBarcode(string text, int width = 300, int height = 100)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Barcode CODE_128 chỉ hỗ trợ ASCII (0-127)
            // Kiểm tra trước, tránh crash
            if (text.Any(c => c > 127))
                return null; // hoặc hiện placeholder

            try
            {
                var writer = new BarcodeWriterPixelData
                {
                    Format = BarcodeFormat.CODE_128,
                    Options = new EncodingOptions
                    {
                        Width = width,
                        Height = height,
                        Margin = 0,
                        PureBarcode = true
                    },
                    Renderer = new PixelDataRenderer
                    {
                        Foreground = new PixelDataRenderer.Color { R = 0, G = 0, B = 0, A = 255 },
                        Background = new PixelDataRenderer.Color { R = 255, G = 255, B = 255, A = 255 }
                    }
                };

                var pixelData = writer.Write(text);

                var bitmap = BitmapSource.Create(
                    pixelData.Width, pixelData.Height,
                    96, 96,
                    PixelFormats.Bgr32,
                    null,
                    pixelData.Pixels,
                    pixelData.Width * 4);

                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using MemoryStream ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;

                BitmapImage img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch
            {
                return null; // không crash app
            }
        }
        void PropValue_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (selectedItem == null) return;
            var data = selectedItem.Tag as DesignerItemData;
            if (data?.Type != "Barcode") return;

            // Chặn ký tự không phải ASCII
            e.Handled = e.Text.Any(c => c > 127);
        }
        // Thay LoadImage thành async
        async Task<BitmapImage> LoadImageAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out Uri uriResult)
                    && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    using HttpClient client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    byte[] bytes = await client.GetByteArrayAsync(uriResult);

                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = new MemoryStream(bytes);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                else if (File.Exists(path))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                    bitmap.StreamSource = fs;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch { }
            return null;
        }
        void UpdateItemBadge(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                selectedItemBadge.Visibility = Visibility.Collapsed;
                return;
            }

            selectedItemBadge.Visibility = Visibility.Visible;

            switch (type)
            {
                case "Text":
                    selectedItemIcon.Text = "\uE8D2";
                    selectedItemIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 191, 255));
                    selectedItemType.Text = "Text";
                    selectedItemBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 191, 255));
                    break;
                case "Image":
                    selectedItemIcon.Text = "\uE91B";
                    selectedItemIcon.Foreground = new SolidColorBrush(Color.FromRgb(167, 139, 250));
                    selectedItemType.Text = "Image";
                    selectedItemBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 167, 139, 250));
                    break;
                case "Line":
                    selectedItemIcon.Text = "\uE7C3";
                    selectedItemIcon.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    selectedItemType.Text = "Line";
                    selectedItemBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 52, 211, 153));
                    break;
                case "Barcode":
                    selectedItemIcon.Text = "\uE8A7";
                    selectedItemIcon.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                    selectedItemType.Text = "Barcode";
                    selectedItemBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 251, 191, 36));
                    break;
                case "QR":
                    selectedItemIcon.Text = "\uE9F9";
                    selectedItemIcon.Foreground = new SolidColorBrush(Color.FromRgb(244, 114, 84));
                    selectedItemType.Text = "QR Code";
                    selectedItemBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 244, 114, 84));
                    break;
                default:
                    selectedItemIcon.Text = "";
                    selectedItemType.Text = type;
                    selectedItemBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(46, 46, 53));
                    break;
            }
        }
        void PropIndex_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (selectedItem == null) return;
            if (_updatingProperties) return;

            if (int.TryParse(propIndex.Text, out int index) && index >= 0)
            {
                double newY = index * ROW_HEIGHT;
                foreach (var item in selectedItems)
                    Canvas.SetTop(item, newY);
            }
        }
        // Chặn chữ của properties
        void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Chỉ cho phép nhập số 0-9
            e.Handled = !e.Text.All(char.IsDigit);
        }
        // Chặc paste chữ vào
        void NumericOnly_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (!text.All(char.IsDigit))
                    e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
            }
        }
        // ─── UNDO/REDO ─────────────────────────────────────────────
        readonly Stack<List<SnapItem>> _undoStack = new();
        readonly Stack<List<SnapItem>> _redoStack = new();
        bool _recordingSnapshot = false;
        List<SnapItem> TakeSnapshot()
        {
            var list = new List<SnapItem>();
            foreach (var child in DesignCanvas.Children)
            {
                if (child is not Border border) continue;
                var data = border.Tag as DesignerItemData;
                if (data == null) continue;

                var snap = new SnapItem
                {
                    Type = data.Type,
                    Value = data.Value,
                    Left = Canvas.GetLeft(border),
                    Top = Canvas.GetTop(border),
                    Width = border.Width,
                    Height = border.Height,
                    LineStyle = data.LineStyle ?? "Solid"
                };

                if (data.Type == "Text" && border.Child is Grid g && g.Children[0] is TextBlock tb)
                {
                    snap.FontSize = (int)tb.FontSize;
                    snap.Bold = tb.FontWeight == FontWeights.Bold;
                    snap.Italic = tb.FontStyle == FontStyles.Italic;
                    snap.TextPosition = data.TextPosition ?? "Middle";
                }

                list.Add(snap);
            }
            return list;
        }

        void PushUndo()
        {
            if (_recordingSnapshot) return;
            _undoStack.Push(TakeSnapshot());
            _redoStack.Clear();
            UpdateUndoRedoButtons();
        }

        async void RestoreSnapshot(List<SnapItem> snaps)
        {
            _recordingSnapshot = true;

            var oldItems = DesignCanvas.Children.OfType<Border>().ToList();
            foreach (var i in oldItems)
                DesignCanvas.Children.Remove(i);

            selectedItem = null;
            ClearProperties();

            foreach (var snap in snaps)
            {
                Border d = CreateItem(snap.Type);
                d.Width = snap.Width;
                d.Height = snap.Height;

                Canvas.SetLeft(d, snap.Left);
                Canvas.SetTop(d, snap.Top);

                if (d.Tag is DesignerItemData data)
                    data.Value = snap.Value;

                if (d.Child is Grid g)
                {
                    if (snap.Type == "Text" && g.Children[0] is TextBlock tb)
                    {
                        tb.Text = snap.Value;
                        tb.FontSize = snap.FontSize > 0 ? snap.FontSize : 12;
                        tb.FontWeight = snap.Bold ? FontWeights.Bold : FontWeights.Regular;
                        tb.FontStyle = snap.Italic ? FontStyles.Italic : FontStyles.Normal;
                        if (d.Tag is DesignerItemData td)
                            td.TextPosition = snap.TextPosition ?? "Middle";
                    }

                    if (snap.Type == "Image")
                    {
                        BitmapImage bmp = IsBase64(snap.Value)
                            ? LoadFromBase64(snap.Value)
                            : await LoadImageAsync(snap.Value);

                        if (bmp != null)
                        {
                            Image img = new Image { Source = bmp, Stretch = Stretch.Uniform, IsHitTestVisible = false };
                            g.Children.Clear();
                            g.Children.Add(img);
                            g.Children.Add(CreateResizeHandle());
                        }
                    }

                    if (snap.Type == "QR" && g.Children[0] is Image qr)
                        qr.Source = GenerateQR(snap.Value, (int)snap.Width, (int)snap.Height);

                    if (snap.Type == "Barcode" && g.Children[0] is Image bc)
                        bc.Source = GenerateBarcode(snap.Value, (int)snap.Width, (int)snap.Height);

                    if (snap.Type == "Line" && g.Children[0] is System.Windows.Shapes.Path lp)
                    {
                        if (d.Tag is DesignerItemData ld) ld.LineStyle = snap.LineStyle ?? "Solid";
                        ApplyLineStyle(lp, snap.LineStyle ?? "Solid");
                    }
                }

                DesignCanvas.Children.Add(d);
            }

            _recordingSnapshot = false;
            UpdateUndoRedoButtons();
        }

        void Undo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Push(TakeSnapshot());
            var snap = _undoStack.Pop();
            RestoreSnapshot(snap);
        }

        void Redo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Push(TakeSnapshot());
            var snap = _redoStack.Pop();
            RestoreSnapshot(snap);
        }

        void UpdateUndoRedoButtons()
        {
            btnUndo.IsEnabled = _undoStack.Count > 0;
            btnRedo.IsEnabled = _redoStack.Count > 0;
        }
        void BtnUndo_Click(object sender, RoutedEventArgs e) => Undo();
        void BtnRedo_Click(object sender, RoutedEventArgs e) => Redo();
        private void BtnDuplicate_Click(object sender, RoutedEventArgs e)
        {
            DuplicateSelectedItem();
        }
        void NumericOnly_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ❌ chặn SPACE
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }

            // ❌ chặn paste Ctrl+V nếu muốn strict
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                e.Handled = true;
            }

            // ✅ Enter → trả focus về window để arrow key hoạt động
            if (e.Key == Key.Enter)
            {
                this.Focus();
                e.Handled = true;
            }
        }
        void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 👉 Nếu click thường (không Ctrl) → chọn DESIGN
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                // clear item selection
                foreach (var it in selectedItems)
                    it.BorderBrush = Brushes.DeepSkyBlue;

                selectedItems.Clear();
                selectedItem = null;

                ShowDesignProperties(); // 🔥 thêm
                return;
            }

            // 👉 Ctrl → selection box
            isSelecting = true;
            selectionStart = e.GetPosition(DesignCanvas);

            selectionBox = new Rectangle
            {
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2 },
                Fill = new SolidColorBrush(Color.FromArgb(40, 0, 191, 255))
            };

            Canvas.SetLeft(selectionBox, selectionStart.X);
            Canvas.SetTop(selectionBox, selectionStart.Y);

            DesignCanvas.Children.Add(selectionBox);
        }
        void ShowDesignProperties()
        {
            _updatingProperties = true;

            UpdateItemBadge("Base Design");

            // Hide item-specific fields
            lblValue.Visibility = Visibility.Collapsed;
            propValue.Visibility = Visibility.Collapsed;
            lblWidth.Visibility = Visibility.Collapsed;
            propWidth.Visibility = Visibility.Collapsed;
            lblHeight.Visibility = Visibility.Collapsed;
            propHeight.Visibility = Visibility.Collapsed;
            lblFontSize.Visibility = Visibility.Collapsed;
            propFontSize.Visibility = Visibility.Collapsed;
            chkBold.Visibility = Visibility.Collapsed;
            chkItalic.Visibility = Visibility.Collapsed;
            lblIndex.Visibility = Visibility.Collapsed;
            propIndex.Visibility = Visibility.Collapsed;
            lblLineStyle.Visibility = Visibility.Collapsed;
            propLineStyle.Visibility = Visibility.Collapsed;
            lblTextPosition.Visibility = Visibility.Collapsed;
            propTextPosition.Visibility = Visibility.Collapsed;

            // Show paper size selector
            lblZone.Visibility = Visibility.Visible;
            propZone.Visibility = Visibility.Visible;

            propZone.Items.Clear();
            propZone.Items.Add(new ComboBoxItem { Content = "60mm" });
            propZone.Items.Add(new ComboBoxItem { Content = "80mm" });
            propZone.SelectedIndex = currentPaperSize == 60 ? 0 : 1;

            lblZone.Text = "Paper Size";

            _updatingProperties = false;
        }
        void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isSelecting || selectionBox == null) return;

            Point pos = e.GetPosition(DesignCanvas);

            double x = Math.Min(pos.X, selectionStart.X);
            double y = Math.Min(pos.Y, selectionStart.Y);

            double w = Math.Abs(pos.X - selectionStart.X);
            double h = Math.Abs(pos.Y - selectionStart.Y);

            Canvas.SetLeft(selectionBox, x);
            Canvas.SetTop(selectionBox, y);

            selectionBox.Width = w;
            selectionBox.Height = h;
        }
        void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!isSelecting) return;

            isSelecting = false;

            Rect selectionRect = new Rect(
                Canvas.GetLeft(selectionBox),
                Canvas.GetTop(selectionBox),
                selectionBox.Width,
                selectionBox.Height);

            // clear selection cũ nếu không giữ Ctrl
            foreach (var it in selectedItems)
                it.BorderBrush = Brushes.DeepSkyBlue;

            selectedItems.Clear();

            foreach (var child in DesignCanvas.Children)
            {
                if (child is Border item)
                {
                    Rect itemRect = new Rect(
                        Canvas.GetLeft(item),
                        Canvas.GetTop(item),
                        item.Width,
                        item.Height);

                    if (selectionRect.IntersectsWith(itemRect))
                    {
                        item.BorderBrush = Brushes.Red;
                        selectedItems.Add(item);
                    }
                }
            }

            selectedItem = selectedItems.LastOrDefault();
            UpdateProperties();

            DesignCanvas.Children.Remove(selectionBox);
            selectionBox = null;
        }
    }
}