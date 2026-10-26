using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // CONTROLADOR DE RUTAS: maneja la tabla Ruta (origen, destino y tarifa).
    // Sus vistas están en Views/Ruta (Index, Create, Edit, Delete).
    // Los datos vienen de la base AerolineaVIP (cadena AerolineaVIPEntities).
    // Para mostrar los nombres de los aeropuertos también se lee la tabla Aeropuerto.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class RutaController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // LISTAR + FILTRO por aeropuerto de origen (lista desplegable)
        public ActionResult Index(int? origenId)
        {
            try
            {
                var lista = db.Ruta.AsQueryable();

                // FILTRO: si eligieron un origen, se muestran solo sus rutas
                if (origenId != null)
                    lista = lista.Where(r => r.AeropuertoOrigenId == origenId);

                // Diccionario: Id del aeropuerto -> "UIO - Quito" (para mostrar texto en vez de números)
                ViewBag.Nombres = NombresAeropuertos();

                // Lista desplegable del filtro
                ViewBag.Origenes = new SelectList(db.Aeropuerto.OrderBy(a => a.CodigoIATA).ToList(),
                                                  "AeropuertoId", "CodigoIATA", origenId);

                return View(lista.OrderBy(r => r.RutaId).ToList());
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo cargar la lista: " + ex.Message;
                ViewBag.Nombres = new Dictionary<int, string>();
                ViewBag.Origenes = new SelectList(new List<Aeropuerto>());
                return View(new List<Ruta>());
            }
        }

        // CREAR (GET): formulario vacío con las listas de aeropuertos
        public ActionResult Create()
        {
            try
            {
                CargarListas(null, null);
                return View(new Ruta { Activa = true });
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo abrir el formulario: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // CREAR (POST): valida y guarda
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "AeropuertoOrigenId,AeropuertoDestinoId,TarifaReferencial,Activa")] Ruta ruta)
        {
            try
            {
                Validar(ruta);

                if (ModelState.IsValid)
                {
                    db.Ruta.Add(ruta);
                    db.SaveChanges();                      // INSERT en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo guardar: " + ex.Message);
            }

            CargarListas(ruta.AeropuertoOrigenId, ruta.AeropuertoDestinoId);
            return View(ruta);
        }

        // EDITAR (GET)
        public ActionResult Edit(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Ruta ruta = db.Ruta.Find(id);
                if (ruta == null)
                    return HttpNotFound();

                CargarListas(ruta.AeropuertoOrigenId, ruta.AeropuertoDestinoId);
                return View(ruta);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar el registro: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // EDITAR (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "RutaId,AeropuertoOrigenId,AeropuertoDestinoId,TarifaReferencial,Activa")] Ruta ruta)
        {
            try
            {
                Validar(ruta);

                if (ModelState.IsValid)
                {
                    db.Entry(ruta).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();                      // UPDATE en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo actualizar: " + ex.Message);
            }

            CargarListas(ruta.AeropuertoOrigenId, ruta.AeropuertoDestinoId);
            return View(ruta);
        }

        // ELIMINAR (GET): confirmación. Solo el SuperUsuario.
        [Authorize(Roles = "SuperUsuario")]
        public ActionResult Delete(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Ruta ruta = db.Ruta.Find(id);
                if (ruta == null)
                    return HttpNotFound();

                var nombres = NombresAeropuertos();
                ViewBag.Origen = nombres.ContainsKey(ruta.AeropuertoOrigenId) ? nombres[ruta.AeropuertoOrigenId] : "";
                ViewBag.Destino = nombres.ContainsKey(ruta.AeropuertoDestinoId) ? nombres[ruta.AeropuertoDestinoId] : "";
                return View(ruta);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar el registro: " + ex.Message;
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
                Ruta ruta = db.Ruta.Find(id);
                db.Ruta.Remove(ruta);
                db.SaveChanges();                          // DELETE en SQL Server
            }
            catch (Exception)
            {
                // Pasa cuando la ruta ya tiene frecuencias (integridad referencial)
                TempData["Error"] = "No se puede eliminar: la ruta tiene frecuencias relacionadas.";
            }
            return RedirectToAction("Index");
        }

        // ---------- Métodos de ayuda ----------

        // Reglas de negocio de la ruta
        private void Validar(Ruta ruta)
        {
            if (ruta.AeropuertoOrigenId == ruta.AeropuertoDestinoId)
                ModelState.AddModelError("AeropuertoDestinoId", "El origen y el destino deben ser distintos.");

            if (ruta.TarifaReferencial < 0)
                ModelState.AddModelError("TarifaReferencial", "La tarifa no puede ser negativa.");

            // No puede existir otra ruta con el mismo origen y destino
            bool repetida = db.Ruta.Any(r => r.AeropuertoOrigenId == ruta.AeropuertoOrigenId
                                          && r.AeropuertoDestinoId == ruta.AeropuertoDestinoId
                                          && r.RutaId != ruta.RutaId);
            if (repetida)
                ModelState.AddModelError("", "Ya existe una ruta con ese origen y destino.");
        }

        // Llena las dos listas desplegables (origen y destino)
        private void CargarListas(int? origen, int? destino)
        {
            var aeropuertos = db.Aeropuerto.OrderBy(a => a.CodigoIATA).ToList()
                .Select(a => new { a.AeropuertoId, Texto = a.CodigoIATA + " - " + a.Localidad }).ToList();

            ViewBag.Origen = new SelectList(aeropuertos, "AeropuertoId", "Texto", origen);
            ViewBag.Destino = new SelectList(aeropuertos, "AeropuertoId", "Texto", destino);
        }

        // Id del aeropuerto -> "UIO - Quito"
        private Dictionary<int, string> NombresAeropuertos()
        {
            return db.Aeropuerto.ToList()
                     .ToDictionary(a => a.AeropuertoId, a => a.CodigoIATA + " - " + a.Localidad);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}