using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // Una fila de la tabla de usuarios
    public class FilaUsuario
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string Nombre { get; set; }
        public string Correo { get; set; }
        public string Rol { get; set; }
        public bool EsPropio { get; set; }
    }

    // Una fila del historial de reservas de un usuario
    public class FilaHistorial
    {
        public int ReservaId { get; set; }
        public string Codigo { get; set; }
        public DateTime Creada { get; set; }
        public string Estado { get; set; }
        public string Compra { get; set; }
        public int Asientos { get; set; }
    }

    // CONTROLADOR DE USUARIOS (pantalla 20, pestaña Usuarios).
    // Sus vistas están en Views/Usuarios (Index y Historial).
    // Los usuarios y roles vienen de la base AerolineaVIP_Identity (ApplicationDbContext).
    // El historial de reservas viene de la base AerolineaVIP (AerolineaVIPEntities).
    // El Administrador puede consultar; solo el SuperUsuario puede asignar roles o eliminar.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class UsuariosController : Controller
    {
        private ApplicationDbContext contexto = new ApplicationDbContext();
        private AerolineaVIPEntities db = new AerolineaVIPEntities();
        private UserManager<ApplicationUser> usuarios;
        private RoleManager<IdentityRole> roles;

        public UsuariosController()
        {
            usuarios = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(contexto));
            roles = new RoleManager<IdentityRole>(new RoleStore<IdentityRole>(contexto));
        }

        // LISTAR + FILTROS: texto (usuario o correo) y rol (lista desplegable)
        public ActionResult Index(string buscar, string rol)
        {
            try
            {
                var consulta = contexto.Users.AsQueryable();
                if (!string.IsNullOrWhiteSpace(buscar))
                    consulta = consulta.Where(u => u.UserName.Contains(buscar) || u.Email.Contains(buscar));

                string yo = User.Identity.GetUserId();
                var filas = new List<FilaUsuario>();

                foreach (var u in consulta.OrderBy(x => x.UserName).ToList())
                {
                    string nombreRol = usuarios.GetRoles(u.Id).FirstOrDefault() ?? "Sin rol";
                    if (!string.IsNullOrEmpty(rol) && nombreRol != rol)
                        continue;

                    var claims = usuarios.GetClaims(u.Id);
                    string nombres = claims.Where(c => c.Type == "Nombres").Select(c => c.Value).FirstOrDefault();
                    string apellidos = claims.Where(c => c.Type == "Apellidos").Select(c => c.Value).FirstOrDefault();

                    filas.Add(new FilaUsuario
                    {
                        Id = u.Id,
                        UserName = u.UserName,
                        Nombre = ((nombres ?? "") + " " + (apellidos ?? "")).Trim(),
                        Correo = u.Email,
                        Rol = nombreRol,
                        EsPropio = u.Id == yo
                    });
                }

                ViewBag.Buscar = buscar;
                ViewBag.Roles = new SelectList(roles.Roles.Select(r => r.Name).OrderBy(n => n).ToList(), rol);
                ViewBag.PuedeAsignar = User.IsInRole("SuperUsuario");
                return View(filas);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo cargar la lista: " + AyudaReservas.Mensaje(ex);
                ViewBag.Roles = new SelectList(new List<string>());
                ViewBag.PuedeAsignar = false;
                return View(new List<FilaUsuario>());
            }
        }

        // ASIGNAR ROL (POST): solo el SuperUsuario
        [Authorize(Roles = "SuperUsuario")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AsignarRol(string id, string rol)
        {
            try
            {
                if (rol != "Cliente" && rol != "Administrador")
                    TempData["Error"] = "Rol no permitido.";
                else if (usuarios.FindById(id) == null)
                    TempData["Error"] = "No se encontró el usuario.";
                else if (id == User.Identity.GetUserId())
                    TempData["Error"] = "No puede cambiar su propio rol.";
                else if (usuarios.IsInRole(id, "SuperUsuario"))
                    TempData["Error"] = "No se modifica el rol de un SuperUsuario.";
                else
                {
                    foreach (string actual in usuarios.GetRoles(id).ToList())
                        usuarios.RemoveFromRole(id, actual);
                    usuarios.AddToRole(id, rol);
                    TempData["Ok"] = "Rol actualizado: " + rol + ".";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo asignar el rol: " + AyudaReservas.Mensaje(ex);
            }
            return RedirectToAction("Index");
        }

        // ELIMINAR (POST): solo el SuperUsuario; no se elimina un usuario que tiene reservas
        [Authorize(Roles = "SuperUsuario")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Eliminar(string id)
        {
            try
            {
                var usuario = usuarios.FindById(id);
                if (usuario == null)
                    TempData["Error"] = "No se encontró el usuario.";
                else if (id == User.Identity.GetUserId())
                    TempData["Error"] = "No puede eliminar su propia cuenta.";
                else if (usuarios.IsInRole(id, "SuperUsuario"))
                    TempData["Error"] = "No se elimina a un SuperUsuario.";
                else if (db.Reserva.Any(r => r.UsuarioId == id))
                    TempData["Error"] = "No se puede eliminar: el usuario tiene reservas asociadas.";
                else
                {
                    var resultado = usuarios.Delete(usuario);
                    if (resultado.Succeeded)
                        TempData["Ok"] = "Usuario eliminado.";
                    else
                        TempData["Error"] = string.Join(" ", resultado.Errors);
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo eliminar: " + AyudaReservas.Mensaje(ex);
            }
            return RedirectToAction("Index");
        }

        // HISTORIAL: reservas, compras y asientos del usuario
        public ActionResult Historial(string id)
        {
            try
            {
                var usuario = usuarios.FindById(id);
                if (usuario == null)
                {
                    TempData["Error"] = "No se encontró el usuario.";
                    return RedirectToAction("Index");
                }

                var reservas = db.Reserva.Where(r => r.UsuarioId == id).OrderByDescending(r => r.ReservaId).ToList();
                var ids = reservas.Select(r => r.ReservaId).ToList();
                var compras = db.Compra.Where(c => ids.Contains(c.ReservaId)).ToList();
                var asientos = db.ReservaPasajeroVuelo.Where(a => ids.Contains(a.ReservaId))
                    .GroupBy(a => a.ReservaId).Select(g => new { Id = g.Key, N = g.Count() })
                    .ToList().ToDictionary(x => x.Id, x => x.N);

                var filas = reservas.Select(r =>
                {
                    Compra compra = compras.Where(c => c.ReservaId == r.ReservaId).OrderByDescending(c => c.CompraId).FirstOrDefault();
                    return new FilaHistorial
                    {
                        ReservaId = r.ReservaId,
                        Codigo = r.CodigoReserva,
                        Creada = r.FechaCreacion,
                        Estado = AyudaReservas.Bonito(r.EstadoReservaCodigo),
                        Compra = compra != null ? AyudaReservas.Bonito(compra.EstadoCompraCodigo) : "Sin compra",
                        Asientos = asientos.ContainsKey(r.ReservaId) ? asientos[r.ReservaId] : 0
                    };
                }).ToList();

                ViewBag.UserName = usuario.UserName;
                return View(filas);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar el historial: " + AyudaReservas.Mensaje(ex);
                return RedirectToAction("Index");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                contexto.Dispose();
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}