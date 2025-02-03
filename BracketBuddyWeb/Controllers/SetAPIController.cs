using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace BracketBuddyWeb.Controllers
{
    public class SetAPIController : Controller
    {
        [HttpGet]
        public async Task<ActionResult> Index(string id)
        {
            var set = await BracketBuddyDatabase.Instance.GetSetAsync(id);
            var eventReportedMatches = BracketBuddyDatabase.Instance.GetEventReportedSets(set.EventId);

            if (eventReportedMatches != null && eventReportedMatches.Keys.Contains(set.Id))
            {
                set.ReportedScoreViaAPI = eventReportedMatches[set.Id];
            }

            return Content(JsonConvert.SerializeObject(await BracketBuddyDatabase.Instance.GetSetAsync(id)), "application/json");
        }
    }
}

