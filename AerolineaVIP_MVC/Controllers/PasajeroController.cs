using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // CONTROLADOR DE PASAJEROS: maneja la tabla Pasajero (la persona que viaja).
    // Sus vistas están en Views/Pasajero (Index, Create, Edit, Delete y _Campos).
    // Los datos vienen de la base AerolineaVIP (cadena AerolineaVIPEntities).
    // La lista de tipos de documento se lee de la tabla TipoDocumentoIdentidad.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class PasajeroController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // LISTAR + FILTROS: texto (nombre o documento) y tipo de documento (lista desplegable)
        public ActionResult Index(string buscar, string tipo)
        {
            try
            {
                var lista = db.Pasajero.AsQueryable();

                // FILTRO 1: texto en nombres, apellidos o número de documento
                if (!string.IsNullOrWhiteSpace(buscar))
                    lista = lista.Where(p => p.Nombres.Contains(buscar)
                                          || p.Apellidos.Contains(buscar)
                                          || p.NumeroDocumento.Contains(buscar));

                // FILTRO 2: tipo de documento
                if (!string.IsNullOrEmpty(tipo))
                    lista = lista.Where(p => p.TipoDocumentoCodigo == tipo);

                ViewBag.Buscar = buscar;
                ViewBag.Tipos = new SelectList(db.TipoDocumentoIdentidad.ToList(), "Codigo", "Descripcion", tipo);

                return View(lista.OrderBy(p => p.Apellidos).ThenBy(p => p.Nombres).ToList());
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo cargar la lista: " + Mensaje(ex);
                ViewBag.Tipos = new SelectList(new List<string>());
                return View(new List<Pasajero>());
            }
        }

        // CREAR (GET)
        public ActionResult Create()
        {
            try
            {
                CargarListas("CEDULA");
                return View(new Pasajero { TipoDocumentoCodigo = "CEDULA" });
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo abrir el formulario: " + Mensaje(ex);
                return RedirectToAction("Index");
            }
        }

        // CREAR (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "TipoDocumentoCodigo,NumeroDocumento,Nombres,Apellidos,FechaNacimiento,Correo,Telefono")] Pasajero pasajero)
        {
            try
            {
                Validar(pasajero);

                if (ModelState.IsValid)
                {
                    pasajero.FechaRegistro = DateTime.Now;     // fecha de registro automática
                    db.Pasajero.Add(pasajero);
                    db.SaveChanges();                          // INSERT en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo guardar: " + Mensaje(ex));
            }

            CargarListas(pasajero.TipoDocumentoCodigo);
            return View(pasajero);
        }

        // EDITAR (GET)
        public ActionResult Edit(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Pasajero pasajero = db.Pasajero.Find(id);
                if (pasajero == null)
                    return HttpNotFound();

                CargarListas(pasajero.TipoDocumentoCodigo);
                return View(pasajero);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar el registro: " + Mensaje(ex);
                return RedirectToAction("Index");
            }
        }

        // EDITAR (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "PasajeroId,TipoDocumentoCodigo,NumeroDocumento,Nombres,Apellidos,FechaNacimiento,Correo,Telefono")] Pasajero pasajero)
        {
            try
            {
                Validar(pasajero);

                if (ModelState.IsValid)
                {
                    // Se busca el registro original para NO perder la fecha de registro
                    Pasajero original = db.Pasajero.Find(pasajero.PasajeroId);
                    if (original == null)
                        return HttpNotFound();

                    original.TipoDocumentoCodigo = pasajero.TipoDocumentoCodigo;
                    original.NumeroDocumento = pasajero.NumeroDocumento;
                    original.Nombres = pasajero.Nombres;
                    original.Apellidos = pasajero.Apellidos;
                    original.FechaNacimiento = pasajero.FechaNacimiento;
                    original.Correo = pasajero.Correo;
                    original.Telefono = pasajero.Telefono;

                    db.SaveChanges();                          // UPDATE en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo actualizar: " + Mensaje(ex));
            }

            CargarListas(pasajero.TipoDocumentoCodigo);
            return View(pasajero);
        }

        // ELIMINAR (GET): solo el SuperUsuario
        [Authorize(Roles = "SuperUsuario")]
        public ActionResult Delete(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Pasajero pasajero = db.Pasajero.Find(id);
                if (pasajero == null)
                    return HttpNotFound();

                return View(pasajero);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar el registro: " + Mensaje(ex);
                return RedirectToAction("Index");
            }
        }

        // ELIMINAR (POST)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperUsuario")]
        public ActionResult DeleteConfirmed(int id)
        {
            try
            {
                Pasajero pasajero = db.Pasajero.Find(id);
                db.Pasajero.Remove(pasajero);
                db.SaveChanges();                              // DELETE en SQL Server
            }
            catch (Exception)
            {
                // Pasa cuando el pasajero ya tiene reservas (integridad referencial)
                TempData["Error"] = "No se puede eliminar: el pasajero tiene reservas relacionadas.";
            }
            return RedirectToAction("Index");
        }

        // ---------- Métodos de ayuda ----------

        // Reglas de negocio y validaciones del pasajero
        private void Validar(Pasajero p)
        {
            // Se limpian los espacios
            p.NumeroDocumento = (p.NumeroDocumento ?? "").Trim();
            p.Nombres = (p.Nombres ?? "").Trim();
            p.Apellidos = (p.Apellidos ?? "").Trim();
            p.Correo = string.IsNullOrWhiteSpace(p.Correo) ? null : p.Correo.Trim();
            p.Telefono = string.IsNullOrWhiteSpace(p.Telefono) ? null : p.Telefono.Trim();

            // Tipo de documento
            if (!db.TipoDocumentoIdentidad.Any(t => t.Codigo == p.TipoDocumentoCodigo))
                ModelState.AddModelError("TipoDocumentoCodigo", "El tipo de documento no existe.");

            // Número de documento
            if (p.NumeroDocumento.Length < 5 || p.NumeroDocumento.Length > 20)
                ModelState.AddModelError("NumeroDocumento", "El documento debe tener entre 5 y 20 caracteres.");
            else if (p.TipoDocumentoCodigo == "CEDULA" &&
                     (p.NumeroDocumento.Length != 10 || !p.NumeroDocumento.All(c => c >= '0' && c <= '9')))
                ModelState.AddModelError("NumeroDocumento", "La cédula debe tener 10 dígitos numéricos.");
            else if (db.Pasajero.Any(x => x.TipoDocumentoCodigo == p.TipoDocumentoCodigo
                                       && x.NumeroDocumento == p.NumeroDocumento
                                       && x.PasajeroId != p.PasajeroId))
                ModelState.AddModelError("NumeroDocumento", "Ya existe un pasajero con ese documento.");

            // Nombres y apellidos
            if (p.Nombres.Length == 0 || p.Nombres.Length > 100)
                ModelState.AddModelError("Nombres", "Los nombres son obligatorios (máximo 100 caracteres).");
            if (p.Apellidos.Length == 0 || p.Apellidos.Length > 100)
                ModelState.AddModelError("Apellidos", "Los apellidos son obligatorios (máximo 100 caracteres).");

            // Fecha de nacimiento (opcional): no puede ser futura ni anterior a 1900
            if (p.FechaNacimiento != null &&
                (p.FechaNacimiento.Value.Date > DateTime.Today || p.FechaNacimiento.Value.Year < 1900))
                ModelState.AddModelError("FechaNacimiento", "La fecha de nacimiento no es válida.");

            // Correo (opcional)
            if (p.Correo != null && !CorreoValido(p.Correo))
                ModelState.AddModelError("Correo", "El correo no es válido.");

            // Teléfono (opcional): solo dígitos, + y espacios
            if (p.Telefono != null &&
                (p.Telefono.Length > 20 || !p.Telefono.All(c => (c >= '0' && c <= '9') || c == '+' || c == ' ')))
                ModelState.AddModelError("Telefono", "El teléfono solo admite números, + y espacios (máximo 20).");
        }

        private bool CorreoValido(string correo)
        {
            try
            {
                var m = new MailAddress(correo);
                return m.Address == correo && correo.Length <= 256;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Llena la lista desplegable de tipos de documento
        private void CargarListas(string tipo)
        {
            ViewBag.ListaTipos = new SelectList(db.TipoDocumentoIdentidad.ToList(), "Codigo", "Descripcion", tipo);
        }

        // Obtiene el mensaje real del error (el de la base de datos está en la excepción interna)
        private string Mensaje(Exception ex)
        {
            while (ex.InnerException != null)
                ex = ex.InnerException;
            return ex.Message;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}