using System.Text.Json.Serialization;

namespace DSJSON.Models
{
    public class PrintItem
    {
        [JsonPropertyName("Index")]
        public int Index { get; set; }

        [JsonPropertyName("Zone")]
        public string Zone { get; set; } = "Left";

        [JsonPropertyName("Type")]
        public string Type { get; set; }

        [JsonPropertyName("Value")]
        public string Value { get; set; }

        [JsonPropertyName("Width")]
        public double Width { get; set; }

        [JsonPropertyName("Height")]
        public double Height { get; set; }

        [JsonPropertyName("FontSize")]
        public int FontSize { get; set; } = 8;

        [JsonPropertyName("Bold")]
        public bool Bold { get; set; }

        [JsonPropertyName("Italic")]
        public bool Italic { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string LineStyle { get; set; }

        // "Top" | "Middle" | "Bottom" — vị trí text theo chiều dọc so với QR
        [JsonPropertyName("TextPosition")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string TextPosition { get; set; } = "Middle";
    }
   public class DesignerItemData
    {
        public string Type { get; set; }
        public string Value { get; set; } = "";
        public string LineStyle { get; set; } = "Solid";
        // "Top" | "Middle" | "Bottom" — chỉ dùng cho Text item ghép với QR
        public string TextPosition { get; set; } = "Middle";
    }
}