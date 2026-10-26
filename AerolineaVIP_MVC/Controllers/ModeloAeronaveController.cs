using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // CONTROLADOR DE MODELOS DE AERONAVE: maneja la tabla ModeloAeronave.
    // Sus vistas están en Views/ModeloAeronave (Index, Create, Edit, Delete y _Campos).
    // Los datos vienen de la base AerolineaVIP (cadena AerolineaVIPEntities).
    // Regla de negocio: un modelo tiene como máximo 7 asientos.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class ModeloAeronaveController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // LISTAR + FILTRO por texto (fabricante o nombre)
        public ActionResult Index(string buscar)
        {
            try
            {
                var lista = db.ModeloAeronave.AsQueryable();

                if (!string.IsNullOrWhiteSpace(buscar))
                    lista = lista.Where(m => m.Fabricante.Contains(buscar) || m.Nombre.Contains(buscar));

                ViewBag.Buscar = buscar;
                return View(lista.OrderBy(m => m.Fabricante).ThenBy(m => m.Nombre).ToList());
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo cargar la lista: " + ex.Message;
                return View(new List<ModeloAeronave>());
            }
        }

        // CREAR (GET)
        public ActionResult Create()
        {
            return View();
        }

        // CREAR (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "Fabricante,Nombre,CapacidadAsientos")] ModeloAeronave modelo)
        {
            try
            {
                Validar(modelo);

                if (ModelState.IsValid)
                {
                    db.ModeloAeronave.Add(modelo);
                    db.SaveChanges();                      // INSERT en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo guardar: " + ex.Message);
            }
            return View(modelo);
        }

        // EDITAR (GET)
        public ActionResult Edit(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                ModeloAeronave modelo = db.ModeloAeronave.Find(id);
                if (modelo == null)
                    return HttpNotFound();

                return View(modelo);
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
        public ActionResult Edit([Bind(Include = "ModeloAeronaveId,Fabricante,Nombre,CapacidadAsientos")] ModeloAeronave modelo)
        {
            try
            {
                Validar(modelo);

                if (ModelState.IsValid)
                {
                    db.Entry(modelo).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();                      // UPDATE en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo actualizar: " + ex.Message);
            }
            return View(modelo);
        }

        // ELIMINAR (GET): solo el SuperUsuario
        [Authorize(Roles = "SuperUsuario")]
        public ActionResult Delete(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                ModeloAeronave modelo = db.ModeloAeronave.Find(id);
                if (modelo == null)
                    return HttpNotFound();

                return View(modelo);
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
                ModeloAeronave modelo = db.ModeloAeronave.Find(id);
                db.ModeloAeronave.Remove(modelo);
                db.SaveChanges();                          // DELETE en SQL Server
            }
            catch (Exception)
            {
                // Pasa cuando el modelo ya tiene aeronaves o asientos
                TempData["Error"] = "No se puede eliminar: el modelo tiene aeronaves o asientos relacionados.";
            }
            return RedirectToAction("Index");
        }

        // Reglas de negocio
        private void Validar(ModeloAeronave m)
        {
            if (string.IsNullOrWhiteSpace(m.Fabricante))
                ModelState.AddModelError("Fabricante", "El fabricante es obligatorio.");

            if (string.IsNullOrWhiteSpace(m.Nombre))
                ModelState.AddModelError("Nombre", "El nombre es obligatorio.");

            // RN-03: máximo 7 asientos
            if (m.CapacidadAsientos < 1 || m.CapacidadAsientos > 7)
                ModelState.AddModelError("CapacidadAsientos", "La capacidad debe estar entre 1 y 7 asientos.");

            // No puede repetirse fabricante + nombre
            if (!string.IsNullOrWhiteSpace(m.Fabricante) && !string.IsNullOrWhiteSpace(m.Nombre))
            {
                string fab = m.Fabricante.Trim();
                string nom = m.Nombre.Trim();
                bool repetido = db.ModeloAeronave.Any(x => x.Fabricante == fab && x.Nombre == nom
                                                        && x.ModeloAeronaveId != m.ModeloAeronaveId);
                if (repetido)
                    ModelState.AddModelError("", "Ya existe un modelo con ese fabricante y nombre.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}