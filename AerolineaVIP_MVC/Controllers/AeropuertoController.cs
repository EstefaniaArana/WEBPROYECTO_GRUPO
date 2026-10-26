using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // CONTROLADOR DE AEROPUERTOS: maneja la tabla Aeropuerto.
    // Sus vistas están en Views/Aeropuerto (Index, Details, Create, Edit, Delete).
    // Los datos vienen de la base AerolineaVIP, usando la cadena AerolineaVIPEntities del Web.config.
    // Solo pueden entrar el SuperUsuario y el Administrador.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class AeropuertoController : Controller
    {
        // db = conexión a la base de datos. Con db.Aeropuerto se lee la tabla Aeropuerto.
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // LISTAR + FILTRO
        // "buscar" es el texto de la caja de búsqueda de la vista Index.
        public ActionResult Index(string buscar)
        {
            try
            {
                // Se piden todos los registros a la tabla
                var lista = db.Aeropuerto.AsQueryable();

                // FILTRO: si escribieron algo, se dejan solo los que lo contienen
                if (!string.IsNullOrWhiteSpace(buscar))
                {
                    lista = lista.Where(a => a.Nombre.Contains(buscar)
                                          || a.Localidad.Contains(buscar)
                                          || a.CodigoIATA.Contains(buscar));
                }

                ViewBag.Buscar = buscar;                 // para que la caja conserve el texto
                return View(lista.OrderBy(a => a.Nombre).ToList());
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo cargar la lista: " + ex.Message;
                return View(new List<Aeropuerto>());
            }
        }

        // DETALLE: muestra un solo aeropuerto
        public ActionResult Details(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Aeropuerto aeropuerto = db.Aeropuerto.Find(id);   // busca por clave primaria
                if (aeropuerto == null)
                    return HttpNotFound();

                return View(aeropuerto);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar el detalle: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // CREAR (GET): muestra el formulario vacío
        public ActionResult Create()
        {
            return View();
        }

        // CREAR (POST): guarda el registro nuevo
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "CodigoIATA,Nombre,Localidad,Activo")] Aeropuerto aeropuerto)
        {
            try
            {
                // VALIDACIÓN PROPIA: el código IATA debe tener 3 letras y no repetirse
                if (!string.IsNullOrEmpty(aeropuerto.CodigoIATA))
                {
                    aeropuerto.CodigoIATA = aeropuerto.CodigoIATA.Trim().ToUpper();

                    if (aeropuerto.CodigoIATA.Length != 3)
                        ModelState.AddModelError("CodigoIATA", "El código IATA debe tener 3 letras.");
                    else if (db.Aeropuerto.Any(a => a.CodigoIATA == aeropuerto.CodigoIATA))
                        ModelState.AddModelError("CodigoIATA", "Ese código ya existe.");
                }

                if (ModelState.IsValid)
                {
                    db.Aeropuerto.Add(aeropuerto);
                    db.SaveChanges();                      // INSERT en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo guardar: " + ex.Message);
            }
            return View(aeropuerto);
        }

        // EDITAR (GET): muestra el formulario con los datos actuales
        public ActionResult Edit(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Aeropuerto aeropuerto = db.Aeropuerto.Find(id);
                if (aeropuerto == null)
                    return HttpNotFound();

                return View(aeropuerto);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar el registro: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // EDITAR (POST): guarda los cambios
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "AeropuertoId,CodigoIATA,Nombre,Localidad,Activo")] Aeropuerto aeropuerto)
        {
            try
            {
                if (!string.IsNullOrEmpty(aeropuerto.CodigoIATA))
                {
                    aeropuerto.CodigoIATA = aeropuerto.CodigoIATA.Trim().ToUpper();

                    if (aeropuerto.CodigoIATA.Length != 3)
                        ModelState.AddModelError("CodigoIATA", "El código IATA debe tener 3 letras.");
                    // Se revisa que otro aeropuerto no tenga el mismo código
                    else if (db.Aeropuerto.Any(a => a.CodigoIATA == aeropuerto.CodigoIATA
                                                 && a.AeropuertoId != aeropuerto.AeropuertoId))
                        ModelState.AddModelError("CodigoIATA", "Ese código ya existe.");
                }

                if (ModelState.IsValid)
                {
                    db.Entry(aeropuerto).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();                      // UPDATE en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo actualizar: " + ex.Message);
            }
            return View(aeropuerto);
        }

        // ELIMINAR (GET): pide confirmación. Solo el SuperUsuario puede eliminar.
        [Authorize(Roles = "SuperUsuario")]
        public ActionResult Delete(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Aeropuerto aeropuerto = db.Aeropuerto.Find(id);
                if (aeropuerto == null)
                    return HttpNotFound();

                return View(aeropuerto);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar el registro: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // ELIMINAR (POST): borra el registro
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperUsuario")]
        public ActionResult DeleteConfirmed(int id)
        {
            try
            {
                Aeropuerto aeropuerto = db.Aeropuerto.Find(id);
                db.Aeropuerto.Remove(aeropuerto);
                db.SaveChanges();                          // DELETE en SQL Server
            }
            catch (Exception)
            {
                // Pasa cuando el aeropuerto ya se usa en rutas (integridad referencial)
                TempData["Error"] = "No se puede eliminar: el aeropuerto tiene rutas relacionadas.";
            }
            return RedirectToAction("Index");
        }

        // Cierra la conexión a la base de datos al terminar
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}