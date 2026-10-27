using System.Web.Mvc;

namespace AerolineaVIP_MVC.Controllers
{
    // CONTROLADOR DEL PANEL ADMINISTRATIVO (pantalla 17).
    // Solo muestra accesos a los módulos; el dashboard está excluido de esta entrega.
    // Su vista está en Views/Admin/Index.cshtml.
    // Solo entran el SuperUsuario y el Administrador.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class AdminController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }
    }
}