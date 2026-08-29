using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PhasmaStrap.Server.WebServer.Services;

namespace PhasmaStrap.Server.WebServer.Controllers.Game;

[ApiController]
[Route("Game/GamePass/GamePassHandler.ashx")]
public class GamePassController : ControllerBase
{
	private readonly ILogger<GamePassController> _logger;

	public GamePassController(ILogger<GamePassController> logger)
	{
		_logger = logger;
	}

	[HttpGet]
	[HttpPost]
	public IActionResult Get([FromQuery(Name = "gpid")] long gamePassId = 0)
	{
		bool owns = gamePassId > 0 && GamePassService.Instance.Owns(gamePassId);
		return Content($"<Value Type=\"boolean\">{(owns ? "true" : "false")}</Value>", "text/html");
	}
}
