﻿using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace BracketBuddyWeb.Controllers
{
    public class CurrentTournamentAPIController : Controller
    {
        [HttpGet]
        public async Task<ActionResult> Index()
        {
            ViewBag.Title = "Current Tournament API";
            MetricsManager.Instance.AddPageView(nameof(CurrentTournamentAPIController), string.Empty);

            var retrievedEvent = await BracketBuddyDatabase.Instance.GetCurrentTournamentsAsync();

            if (retrievedEvent == null)
            {
                return new HttpNotFoundResult("Event not found");
            }

            return Content(JsonConvert.SerializeObject(retrievedEvent), "application/json");
        }
    }
}