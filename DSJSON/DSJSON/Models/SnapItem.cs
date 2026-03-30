using System;
using System.Collections.Generic;
using System.Text;

namespace DSJSON.Models
{
    class SnapItem
    {
        public string Type { get; set; }
        public string Value { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int FontSize { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string LineStyle { get; set; } = "Solid";
        public string TextPosition { get; set; } = "Middle";
    }
}
