using System;
using System.Collections.Generic;
using System.Text;

namespace DSJSON.Models
{
    public class PrintTemplate
    {
        public int PaperSize { get; set; } = 60; // default
        public List<PrintItem> Content { get; set; } = new();
    }
}
