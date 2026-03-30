using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Printapi2.Controllers
{
    [ApiController]
    [Route("api/print")]
    public class PrintController : ControllerBase
    {
        PrintService printService = new PrintService();

        [HttpPost]
        public IActionResult Print([FromBody] JsonElement json)
        {
            string data = json.ToString();

            printService.Print(data);

            return Ok("Printed");
        }
    }
}
