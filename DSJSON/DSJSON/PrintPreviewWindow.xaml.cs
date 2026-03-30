using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DSJSON
{
    public partial class PrintPreviewWindow : Window
    {
        public PrintPreviewWindow(BitmapSource bitmap)
        {
            InitializeComponent();

            // A4 at 96dpi: 794 x 1123
            double pageW = 794;
            double pageH = 1123;
            double margin = 30;

            FixedDocument doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = new Size(pageW, pageH);

            FixedPage page = new FixedPage { Width = pageW, Height = pageH };

            var img = new Image
            {
                Source = bitmap,
                Width = pageW - margin * 2,
                Stretch = Stretch.Uniform
            };

            FixedPage.SetLeft(img, margin);
            FixedPage.SetTop(img, margin);
            page.Children.Add(img);

            PageContent content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            doc.Pages.Add(content);

            docViewer.Document = doc;
        }
    }
}
