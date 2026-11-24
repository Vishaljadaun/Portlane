using System.Web.Mvc;
using System.Configuration;

namespace Storefront.Controllers
{
    public class OrdersController : Controller
    {
        public ActionResult Index()
        {
            var owner = Session["UserId"];
            var region = ConfigurationManager.AppSettings["Region"];
            return View(new { Owner = owner, Region = region });
        }
    }
}
