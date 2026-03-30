namespace Printapi2.Models
{


    using System.Text.Json.Serialization;

    
    
        public class PrintItem
        {
            [JsonPropertyName("Index")]
            public int Index { get; set; }

            [JsonPropertyName("Zone")]
            public string Zone { get; set; }

            [JsonPropertyName("Type")]
            public string Type { get; set; }

            [JsonPropertyName("Value")]
            public string Value { get; set; }

            [JsonPropertyName("Width")]
            public double Width { get; set; }

            [JsonPropertyName("Height")]
            public double Height { get; set; }

            [JsonPropertyName("FontSize")]
            public int FontSize { get; set; } = 12;

            [JsonPropertyName("Bold")]
            public bool Bold { get; set; }

            [JsonPropertyName("Italic")]
            public bool Italic { get; set; }
            [JsonPropertyName("LineStyle")]
            public string LineStyle { get; set; } = "Solid";

            // "Top" | "Middle" | "Bottom" — vị trí text theo chiều dọc so với QR
            [JsonPropertyName("TextPosition")]
            public string TextPosition { get; set; } = "Middle";
    }
    }



