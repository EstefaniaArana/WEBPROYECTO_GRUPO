using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // CONTROLADOR DE AERONAVES: maneja la tabla Aeronave (matrícula, modelo y estado).
    // Sus vistas están en Views/Aeronave (Index, Create, Edit, Delete y _Campos).
    // Los datos vienen de la base AerolineaVIP (cadena AerolineaVIPEntities).
    // Las listas desplegables leen las tablas ModeloAeronave y EstadoAeronave.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class AeronaveController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // LISTAR + FILTRO por estado (lista desplegable)
        public ActionResult Index(string estado)
        {
            try
            {
                var lista = db.Aeronave.AsQueryable();

                // FILTRO: si eligieron un estado, se muestran solo esas aeronaves
                if (!string.IsNullOrEmpty(estado))
                    lista = lista.Where(a => a.EstadoAeronaveCodigo == estado);

                ViewBag.Modelos = NombresModelos();      // ModeloAeronaveId -> "Embraer Phenom 300E"
                ViewBag.Estados = new SelectList(db.EstadoAeronave.ToList(), "Codigo", "Descripcion", estado);

                return View(lista.OrderBy(a => a.Matricula).ToList());
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo cargar la lista: " + ex.Message;
                ViewBag.Modelos = new Dictionary<int, string>();
                ViewBag.Estados = new SelectList(new List<string>());
                return View(new List<Aeronave>());
            }
        }

        // CREAR (GET)
        public ActionResult Create()
        {
            try
            {
                CargarListas(null, "DISPONIBLE");
                return View(new Aeronave { EstadoAeronaveCodigo = "DISPONIBLE" });
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo abrir el formulario: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // CREAR (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "Matricula,ModeloAeronaveId,EstadoAeronaveCodigo")] Aeronave aeronave)
        {
            try
            {
                Validar(aeronave);

                if (ModelState.IsValid)
                {
                    db.Aeronave.Add(aeronave);
                    db.SaveChanges();                      // INSERT en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo guardar: " + ex.Message);
            }

            CargarListas(aeronave.ModeloAeronaveId, aeronave.EstadoAeronaveCodigo);
            return View(aeronave);
        }

        // EDITAR (GET)
        public ActionResult Edit(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Aeronave aeronave = db.Aeronave.Find(id);
                if (aeronave == null)
                    return HttpNotFound();

                CargarListas(aeronave.ModeloAeronaveId, aeronave.EstadoAeronaveCodigo);
                return View(aeronave);
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
        public ActionResult Edit([Bind(Include = "AeronaveId,Matricula,ModeloAeronaveId,EstadoAeronaveCodigo")] Aeronave aeronave)
        {
            try
            {
                Validar(aeronave);

                if (ModelState.IsValid)
                {
                    db.Entry(aeronave).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();                      // UPDATE en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo actualizar: " + ex.Message);
            }

            CargarListas(aeronave.ModeloAeronaveId, aeronave.EstadoAeronaveCodigo);
            return View(aeronave);
        }

        // ELIMINAR (GET): solo el SuperUsuario
        [Authorize(Roles = "SuperUsuario")]
        public ActionResult Delete(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Aeronave aeronave = db.Aeronave.Find(id);
                if (aeronave == null)
                    return HttpNotFound();

                var modelos = NombresModelos();
                ViewBag.NombreModelo = modelos.ContainsKey(aeronave.ModeloAeronaveId) ? modelos[aeronave.ModeloAeronaveId] : "";
                return View(aeronave);
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
                Aeronave aeronave = db.Aeronave.Find(id);
                db.Aeronave.Remove(aeronave);
                db.SaveChanges();                          // DELETE en SQL Server
            }
            catch (Exception)
            {
                // Pasa cuando la aeronave ya está asignada a vuelos
                TempData["Error"] = "No se puede eliminar: la aeronave tiene vuelos asignados.";
            }
            return RedirectToAction("Index");
        }

        // ---------- Métodos de ayuda ----------

        // Reglas de negocio de la aeronave
        private void Validar(Aeronave a)
        {
            if (string.IsNullOrWhiteSpace(a.Matricula))
            {
                ModelState.AddModelError("Matricula", "La matrícula es obligatoria.");
                return;
            }

            a.Matricula = a.Matricula.Trim().ToUpper();

            if (a.Matricula.Length > 15)
                ModelState.AddModelError("Matricula", "La matrícula no puede superar 15 caracteres.");

            if (!db.ModeloAeronave.Any(m => m.ModeloAeronaveId == a.ModeloAeronaveId))
                ModelState.AddModelError("ModeloAeronaveId", "El modelo seleccionado no existe.");

            if (!db.EstadoAeronave.Any(e => e.Codigo == a.EstadoAeronaveCodigo))
                ModelState.AddModelError("EstadoAeronaveCodigo", "El estado seleccionado no existe.");

            // La matrícula no puede repetirse
            if (db.Aeronave.Any(x => x.Matricula == a.Matricula && x.AeronaveId != a.AeronaveId))
                ModelState.AddModelError("Matricula", "Ya existe una aeronave con esa matrícula.");
        }

        // Llena las listas desplegables de modelo y estado
        private void CargarListas(int? modelo, string estado)
        {
            var modelos = db.ModeloAeronave.ToList()
                .Select(m => new { m.ModeloAeronaveId, Texto = m.Fabricante + " " + m.Nombre }).ToList();

            ViewBag.ListaModelos = new SelectList(modelos, "ModeloAeronaveId", "Texto", modelo);
            ViewBag.ListaEstados = new SelectList(db.EstadoAeronave.ToList(), "Codigo", "Descripcion", estado);
        }

        // ModeloAeronaveId -> "Embraer Phenom 300E"
        private Dictionary<int, string> NombresModelos()
        {
            return db.ModeloAeronave.ToList()
                     .ToDictionary(m => m.ModeloAeronaveId, m => m.Fabricante + " " + m.Nombre);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}