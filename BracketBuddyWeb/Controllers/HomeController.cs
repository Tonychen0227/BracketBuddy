using System.Threading.Tasks;
using System.Web.Mvc;

namespace BracketBuddyWeb.Controllers
{
    public class HomeController : Controller
    {
        public async Task<ActionResult> Index()
        {
            MetricsManager.Instance.AddPageView(nameof(HomeController), string.Empty);

            ViewBag.Title = "Bracket Buddy";

            return View();
        }
    }
}
