using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AMS_WebAPI.Controllers
{
    [ApiController]
    //[Authorize]
    //[Authorize(Roles = UserRoles.DataSync_User)]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;

        public WeatherForecastController(ILogger<WeatherForecastController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet]
        public IEnumerable<WeatherForecast> Get()
        {
            var rng = new Random();
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = rng.Next(-20, 55),
                Summary = Summaries[rng.Next(Summaries.Length)]
            })
            .ToArray();
        }

        [HttpGet("{name}")]
        public string Get(string name)
        {
            _logger.LogInformation("...... Test from Get names");
            return _configuration.GetValue<string>(name);
        }

        //[Authorize]
        //[HttpGet("{name1}")]
        //public string GetAuthName(string name1)
        //{
        //    _logger.LogInformation("...... Test from Get names");
        //    return _configuration.GetValue<string>(name1);
        //}
    }
}
