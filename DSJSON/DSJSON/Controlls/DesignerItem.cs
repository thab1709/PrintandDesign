using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DSJSON.Controls
{
    public class DesignerItem : Border
    {
        bool dragging;
        bool resizing;

        Point start;

        Rectangle resizeHandle;
      

        public string ItemType { get; set; }

        public DesignerItem()
        {
            BorderBrush = Brushes.DeepSkyBlue;
            BorderThickness = new Thickness(1);
            Background = Brushes.Transparent;

            resizeHandle = new Rectangle
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.DeepSkyBlue,
                Cursor = Cursors.SizeNWSE
            };

            Grid g = new Grid();
            g.Children.Add(resizeHandle);

            Child = g;

            MouseLeftButtonDown += Down;
            MouseMove += Move;
            MouseLeftButtonUp += Up;

            resizeHandle.MouseLeftButtonDown += ResizeDown;
        }

        void Down(object sender, MouseButtonEventArgs e)
        {
            if (resizing) return;

            dragging = true;
            start = e.GetPosition(Parent as Canvas);
            CaptureMouse();
        }

        void ResizeDown(object sender, MouseButtonEventArgs e)
        {
            resizing = true;
            start = e.GetPosition(Parent as Canvas);
            CaptureMouse();
            e.Handled = true;
        }

        void Move(object sender, MouseEventArgs e)
        {
            Canvas canvas = Parent as Canvas;

            Point pos = e.GetPosition(canvas);

            double dx = pos.X - start.X;
            double dy = pos.Y - start.Y;

            if (dragging)
            {
                Canvas.SetLeft(this, Canvas.GetLeft(this) + dx);
                Canvas.SetTop(this, Canvas.GetTop(this) + dy);
            }

            if (resizing)
            {
                Width += dx;
                Height += dy;
            }

            start = pos;
        }

        void Up(object sender, MouseButtonEventArgs e)
        {
            dragging = false;
            resizing = false;
            ReleaseMouseCapture();
        }
    }
}