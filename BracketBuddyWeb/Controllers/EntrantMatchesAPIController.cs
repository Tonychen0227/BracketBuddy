﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace BracketBuddyWeb.Controllers
{
    public class EntrantMatchesAPIController : Controller
    {
        [HttpGet]
        public async Task<ActionResult> Index(string id)
        {
            MetricsManager.Instance.AddPageView(nameof(EntrantMatchesAPIController), id);

            dynamic ret = new System.Dynamic.ExpandoObject();

            var entrant = await BracketBuddyDatabase.Instance.GetEntrantAsync(id);

            if (entrant != null)
            {
                var entrantMatches = await BracketBuddyDatabase.Instance.GetSetsAsync(entrant.EventId);
                var eventReportedMatches = BracketBuddyDatabase.Instance.GetEventReportedSets(entrant.EventId);

                foreach (var entrantMatch in entrantMatches)
                {
                    if (eventReportedMatches != null && eventReportedMatches.Keys.Contains(entrantMatch.Id))
                    {
                        entrantMatch.ReportedScoreViaAPI = eventReportedMatches[entrantMatch.Id];
                    }
                }

                ret = entrantMatches.Where(x => x.EntrantIds.Contains(id)).OrderBy(x => x.PhaseOrder).ThenBy(x => Math.Abs(x.Round ?? 999)).ToList();
            }

            return Content(JsonConvert.SerializeObject(ret), "application/json");
        }
    }
}

