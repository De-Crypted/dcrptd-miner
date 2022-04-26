using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace dcrpt_miner 
{
    public class StatsController : Controller
    {
        private readonly IConfiguration configuration;

        public StatsController(IConfiguration configuration) => 
            this.configuration = configuration ?? throw new System.ArgumentNullException(nameof(configuration));

        [HttpGet("stats")]
        public IActionResult GetStats()
        {
            var accessToken = configuration.GetValue<string>("api:access_token");

            if (!string.IsNullOrEmpty(accessToken)) {
                if (Request.HttpContext.Request.Headers.Authorization.Count != 1) {
                    return BadRequest();
                }

                var token = Request.HttpContext.Request.Headers.Authorization[0];

                if (token != accessToken) {
                    return Unauthorized();
                }
            }

            return Content("hello");
        }
    }
}