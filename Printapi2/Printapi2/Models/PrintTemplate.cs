using System.Text.Json.Serialization;

namespace Printapi2.Models
{
    public class PrintTemplate
    {
        [JsonPropertyName("PaperSize")]
        public int PaperSize { get; set; } = 60;

        [JsonPropertyName("Content")]
        public List<PrintItem> Content { get; set; } = new();
    }
}

