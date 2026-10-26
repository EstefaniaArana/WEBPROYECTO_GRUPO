using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // CONTROLADOR DE FRECUENCIAS: maneja la tabla Frecuencia (ruta, día de la semana y horas).
    // Sus vistas están en Views/Frecuencia (Index, Create, Edit, Delete y _Campos).
    // Los datos vienen de la base AerolineaVIP (cadena AerolineaVIPEntities).
    // Para mostrar el nombre de la ruta se leen también las tablas Ruta y Aeropuerto.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class FrecuenciaController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // Nombres de los días: la posición 1 es lunes ... la 7 es domingo
        private static readonly string[] Dias =
            { "", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo" };

        // LISTAR + FILTRO por día de la semana (lista desplegable)
        public ActionResult Index(int? dia)
        {
            try
            {
                var lista = db.Frecuencia.AsQueryable();

                // FILTRO: si eligieron un día, se muestran solo las frecuencias de ese día
                if (dia != null)
                    lista = lista.Where(f => f.DiaSemana == dia);

                ViewBag.Rutas = NombresRutas();          // RutaId -> "UIO → GYE"
                ViewBag.NombresDias = Dias;
                ViewBag.FiltroDias = ListaDias(dia);

                return View(lista.OrderBy(f => f.DiaSemana).ThenBy(f => f.HoraSalida).ToList());
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo cargar la lista: " + ex.Message;
                ViewBag.Rutas = new Dictionary<int, string>();
                ViewBag.NombresDias = Dias;
                ViewBag.FiltroDias = ListaDias(null);
                return View(new List<Frecuencia>());
            }
        }

        // CREAR (GET)
        public ActionResult Create()
        {
            try
            {
                CargarListas(null, null);
                return View(new Frecuencia { Activa = true });
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
        public ActionResult Create([Bind(Include = "RutaId,DiaSemana,HoraSalida,HoraLlegada,Activa")] Frecuencia frecuencia)
        {
            try
            {
                Validar(frecuencia);

                if (ModelState.IsValid)
                {
                    db.Frecuencia.Add(frecuencia);
                    db.SaveChanges();                      // INSERT en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo guardar: " + ex.Message);
            }

            CargarListas(frecuencia.RutaId, frecuencia.DiaSemana);
            return View(frecuencia);
        }

        // EDITAR (GET)
        public ActionResult Edit(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Frecuencia frecuencia = db.Frecuencia.Find(id);
                if (frecuencia == null)
                    return HttpNotFound();

                CargarListas(frecuencia.RutaId, frecuencia.DiaSemana);
                return View(frecuencia);
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
        public ActionResult Edit([Bind(Include = "FrecuenciaId,RutaId,DiaSemana,HoraSalida,HoraLlegada,Activa")] Frecuencia frecuencia)
        {
            try
            {
                Validar(frecuencia);

                if (ModelState.IsValid)
                {
                    db.Entry(frecuencia).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();                      // UPDATE en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo actualizar: " + ex.Message);
            }

            CargarListas(frecuencia.RutaId, frecuencia.DiaSemana);
            return View(frecuencia);
        }

        // ELIMINAR (GET): confirmación. Solo el SuperUsuario.
        [Authorize(Roles = "SuperUsuario")]
        public ActionResult Delete(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Frecuencia frecuencia = db.Frecuencia.Find(id);
                if (frecuencia == null)
                    return HttpNotFound();

                var rutas = NombresRutas();
                ViewBag.Ruta = rutas.ContainsKey(frecuencia.RutaId) ? rutas[frecuencia.RutaId] : "";
                ViewBag.Dia = Dias[frecuencia.DiaSemana];
                return View(frecuencia);
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
                Frecuencia frecuencia = db.Frecuencia.Find(id);
                db.Frecuencia.Remove(frecuencia);
                db.SaveChanges();                          // DELETE en SQL Server
            }
            catch (Exception)
            {
                // Pasa cuando la frecuencia ya tiene vuelos (integridad referencial)
                TempData["Error"] = "No se puede eliminar: la frecuencia tiene vuelos relacionados.";
            }
            return RedirectToAction("Index");
        }

        // ---------- Métodos de ayuda ----------

        // Reglas de negocio de la frecuencia
        private void Validar(Frecuencia f)
        {
            if (f.DiaSemana < 1 || f.DiaSemana > 7)
                ModelState.AddModelError("DiaSemana", "El día debe estar entre lunes y domingo.");

            if (f.HoraLlegada <= f.HoraSalida)
                ModelState.AddModelError("HoraLlegada", "La hora de llegada debe ser mayor que la de salida.");

            if (!db.Ruta.Any(r => r.RutaId == f.RutaId))
                ModelState.AddModelError("RutaId", "La ruta seleccionada no existe.");

            // No puede repetirse la misma ruta, día y hora de salida
            bool repetida = db.Frecuencia.Any(x => x.RutaId == f.RutaId
                                                && x.DiaSemana == f.DiaSemana
                                                && x.HoraSalida == f.HoraSalida
                                                && x.FrecuenciaId != f.FrecuenciaId);
            if (repetida)
                ModelState.AddModelError("", "Ya existe una frecuencia con esa ruta, día y hora de salida.");
        }

        // Llena las listas desplegables de ruta y día
        private void CargarListas(int? ruta, int? dia)
        {
            var rutas = NombresRutas().Select(x => new { Id = x.Key, Texto = x.Value }).ToList();
            ViewBag.Ruta = new SelectList(rutas, "Id", "Texto", ruta);
            ViewBag.DiaLista = ListaDias(dia);
        }

        // Lista de días: valor 1..7 y texto "Lunes"..."Domingo"
        private SelectList ListaDias(int? seleccionado)
        {
            var dias = new List<object>();
            for (int i = 1; i <= 7; i++)
                dias.Add(new { Valor = i, Texto = Dias[i] });
            return new SelectList(dias, "Valor", "Texto", seleccionado);
        }

        // RutaId -> "UIO → GYE"
        private Dictionary<int, string> NombresRutas()
        {
            var aeropuertos = db.Aeropuerto.ToList().ToDictionary(a => a.AeropuertoId, a => a.CodigoIATA);
            return db.Ruta.ToList().ToDictionary(
                r => r.RutaId,
                r => aeropuertos[r.AeropuertoOrigenId] + " → " + aeropuertos[r.AeropuertoDestinoId]);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}