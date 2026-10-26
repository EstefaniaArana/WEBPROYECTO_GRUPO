using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // CONTROLADOR DE VUELOS: maneja la tabla Vuelo (una frecuencia en una fecha concreta).
    // Sus vistas están en Views/Vuelo (Index, Create, Edit, Delete y _Campos).
    // Los datos vienen de la base AerolineaVIP (cadena AerolineaVIPEntities).
    // Para mostrar la ruta y el horario se leen también Frecuencia, Ruta y Aeropuerto.
    // Los triggers de SQL Server también validan estas reglas; aquí se validan antes
    // para mostrar un mensaje claro al usuario.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class VueloController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        private static readonly string[] Dias =
            { "", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo" };

        // LISTAR + FILTROS: fecha (calendario) y estado (lista desplegable)
        public ActionResult Index(DateTime? fecha, string estado)
        {
            try
            {
                var lista = db.Vuelo.AsQueryable();

                // FILTRO 1: una fecha concreta
                if (fecha != null)
                {
                    DateTime dia = fecha.Value.Date;
                    lista = lista.Where(v => v.FechaOperacion == dia);
                }

                // FILTRO 2: estado del vuelo
                if (!string.IsNullOrEmpty(estado))
                    lista = lista.Where(v => v.EstadoVueloCodigo == estado);

                // Se ordena por fecha y luego por hora de salida
                var frecuencias = db.Frecuencia.ToList().ToDictionary(f => f.FrecuenciaId);
                var datos = lista.ToList()
                                 .OrderBy(v => v.FechaOperacion)
                                 .ThenBy(v => frecuencias[v.FrecuenciaId].HoraSalida)
                                 .ToList();

                ViewBag.Frecuencias = TextoFrecuencias();            // FrecuenciaId -> "UIO → GYE (09:00 - 10:00)"
                ViewBag.Aeronaves = db.Aeronave.ToList().ToDictionary(a => a.AeronaveId, a => a.Matricula);
                ViewBag.Fecha = fecha != null ? fecha.Value.ToString("yyyy-MM-dd") : "";
                ViewBag.Estados = new SelectList(db.EstadoVuelo.ToList(), "Codigo", "Descripcion", estado);

                return View(datos);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo cargar la lista: " + Mensaje(ex);
                ViewBag.Frecuencias = new Dictionary<int, string>();
                ViewBag.Aeronaves = new Dictionary<int, string>();
                ViewBag.Fecha = "";
                ViewBag.Estados = new SelectList(new List<string>());
                return View(new List<Vuelo>());
            }
        }

        // CREAR (GET)
        public ActionResult Create()
        {
            try
            {
                CargarListas(null, null, "PROGRAMADO");
                return View(new Vuelo { FechaOperacion = DateTime.Today, EstadoVueloCodigo = "PROGRAMADO" });
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
        public ActionResult Create([Bind(Include = "FrecuenciaId,FechaOperacion,AeronaveId,EstadoVueloCodigo")] Vuelo vuelo)
        {
            try
            {
                Validar(vuelo);

                if (ModelState.IsValid)
                {
                    db.Vuelo.Add(vuelo);
                    db.SaveChanges();                      // INSERT en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo guardar: " + Mensaje(ex));
            }

            CargarListas(vuelo.FrecuenciaId, vuelo.AeronaveId, vuelo.EstadoVueloCodigo);
            return View(vuelo);
        }

        // EDITAR (GET)
        public ActionResult Edit(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Vuelo vuelo = db.Vuelo.Find(id);
                if (vuelo == null)
                    return HttpNotFound();

                CargarListas(vuelo.FrecuenciaId, vuelo.AeronaveId, vuelo.EstadoVueloCodigo);
                return View(vuelo);
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
        public ActionResult Edit([Bind(Include = "VueloId,FrecuenciaId,FechaOperacion,AeronaveId,EstadoVueloCodigo")] Vuelo vuelo)
        {
            try
            {
                Validar(vuelo);

                if (ModelState.IsValid)
                {
                    db.Entry(vuelo).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();                      // UPDATE en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo actualizar: " + Mensaje(ex));
            }

            CargarListas(vuelo.FrecuenciaId, vuelo.AeronaveId, vuelo.EstadoVueloCodigo);
            return View(vuelo);
        }

        // ELIMINAR (GET): solo el SuperUsuario
        [Authorize(Roles = "SuperUsuario")]
        public ActionResult Delete(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Vuelo vuelo = db.Vuelo.Find(id);
                if (vuelo == null)
                    return HttpNotFound();

                var textos = TextoFrecuencias();
                ViewBag.Ruta = textos.ContainsKey(vuelo.FrecuenciaId) ? textos[vuelo.FrecuenciaId] : "";
                return View(vuelo);
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
                Vuelo vuelo = db.Vuelo.Find(id);
                db.Vuelo.Remove(vuelo);
                db.SaveChanges();                          // DELETE en SQL Server
            }
            catch (Exception)
            {
                // Pasa cuando el vuelo ya tiene reservas o itinerarios
                TempData["Error"] = "No se puede eliminar: el vuelo tiene reservas o itinerarios relacionados.";
            }
            return RedirectToAction("Index");
        }

        // ---------- Métodos de ayuda ----------

        // Reglas de negocio del vuelo
        private void Validar(Vuelo v)
        {
            if (v.FechaOperacion == default(DateTime))
            {
                ModelState.AddModelError("FechaOperacion", "La fecha es obligatoria.");
                return;
            }
            v.FechaOperacion = v.FechaOperacion.Date;

            // Vuelo original (si se está editando); null si es nuevo
            Vuelo original = db.Vuelo.AsNoTracking().FirstOrDefault(x => x.VueloId == v.VueloId);

            // 1) La frecuencia debe existir
            Frecuencia frecuencia = db.Frecuencia.Find(v.FrecuenciaId);
            if (frecuencia == null)
            {
                ModelState.AddModelError("FrecuenciaId", "La frecuencia seleccionada no existe.");
                return;
            }

            // 2) La fecha debe caer en el día de la semana de la frecuencia (1 = lunes ... 7 = domingo)
            int diaFecha = ((int)v.FechaOperacion.DayOfWeek + 6) % 7 + 1;
            if (diaFecha != frecuencia.DiaSemana)
                ModelState.AddModelError("FechaOperacion",
                    "La fecha cae en " + Dias[diaFecha] + " pero la frecuencia opera los " + Dias[frecuencia.DiaSemana] + ".");

            // 3) No se programa un vuelo nuevo sobre una frecuencia inactiva
            if ((original == null || original.FrecuenciaId != v.FrecuenciaId) && !frecuencia.Activa)
                ModelState.AddModelError("FrecuenciaId", "La frecuencia está inactiva.");

            // 4) No puede repetirse la misma frecuencia en la misma fecha
            if (db.Vuelo.Any(x => x.FrecuenciaId == v.FrecuenciaId
                               && x.FechaOperacion == v.FechaOperacion
                               && x.VueloId != v.VueloId))
                ModelState.AddModelError("", "Ya existe un vuelo de esa frecuencia en esa fecha.");

            // 5) Estado válido
            if (!db.EstadoVuelo.Any(e => e.Codigo == v.EstadoVueloCodigo))
                ModelState.AddModelError("EstadoVueloCodigo", "El estado seleccionado no existe.");

            // 6) Si ya tiene reservas, no se puede cambiar la frecuencia ni la fecha
            if (original != null
                && (original.FrecuenciaId != v.FrecuenciaId || original.FechaOperacion != v.FechaOperacion)
                && db.ReservaPasajeroVuelo.Any(r => r.VueloId == v.VueloId))
                ModelState.AddModelError("", "El vuelo ya tiene reservas: no se puede cambiar la frecuencia ni la fecha.");

            // 7) Aeronave (opcional)
            if (v.AeronaveId != null)
            {
                Aeronave aeronave = db.Aeronave.Find(v.AeronaveId);
                if (aeronave == null)
                {
                    ModelState.AddModelError("AeronaveId", "La aeronave seleccionada no existe.");
                }
                else
                {
                    // Solo aeronaves disponibles para nuevas asignaciones
                    if ((original == null || original.AeronaveId != v.AeronaveId)
                        && aeronave.EstadoAeronaveCodigo != "DISPONIBLE")
                        ModelState.AddModelError("AeronaveId", "La aeronave no está disponible para nuevas asignaciones.");

                    // La aeronave no puede tener otro vuelo con horario superpuesto el mismo día
                    if (v.EstadoVueloCodigo != "CANCELADO")
                    {
                        var otros = db.Vuelo.Where(x => x.AeronaveId == v.AeronaveId
                                                     && x.VueloId != v.VueloId
                                                     && x.FechaOperacion == v.FechaOperacion
                                                     && x.EstadoVueloCodigo != "CANCELADO").ToList();
                        foreach (var otro in otros)
                        {
                            Frecuencia f2 = db.Frecuencia.Find(otro.FrecuenciaId);
                            if (frecuencia.HoraSalida < f2.HoraLlegada && f2.HoraSalida < frecuencia.HoraLlegada)
                            {
                                ModelState.AddModelError("AeronaveId", "La aeronave ya tiene otro vuelo en un horario que se cruza.");
                                break;
                            }
                        }
                    }
                }
            }
        }

        // Llena las listas desplegables de frecuencia, aeronave y estado
        private void CargarListas(int? frecuencia, int? aeronave, string estado)
        {
            var aeropuertos = db.Aeropuerto.ToList().ToDictionary(a => a.AeropuertoId, a => a.CodigoIATA);
            var rutas = db.Ruta.ToList().ToDictionary(r => r.RutaId);

            var frecuencias = db.Frecuencia.ToList()
                .OrderBy(f => f.DiaSemana).ThenBy(f => f.HoraSalida)
                .Select(f => new
                {
                    f.FrecuenciaId,
                    Texto = Dias[f.DiaSemana] + " · "
                          + aeropuertos[rutas[f.RutaId].AeropuertoOrigenId] + " → "
                          + aeropuertos[rutas[f.RutaId].AeropuertoDestinoId] + " · "
                          + f.HoraSalida.ToString(@"hh\:mm")
                }).ToList();

            ViewBag.ListaFrecuencias = new SelectList(frecuencias, "FrecuenciaId", "Texto", frecuencia);
            ViewBag.ListaAeronaves = new SelectList(db.Aeronave.OrderBy(a => a.Matricula).ToList(), "AeronaveId", "Matricula", aeronave);
            ViewBag.ListaEstados = new SelectList(db.EstadoVuelo.ToList(), "Codigo", "Descripcion", estado);
        }

        // FrecuenciaId -> "UIO → GYE (09:00 - 10:00)"
        private Dictionary<int, string> TextoFrecuencias()
        {
            var aeropuertos = db.Aeropuerto.ToList().ToDictionary(a => a.AeropuertoId, a => a.CodigoIATA);
            var rutas = db.Ruta.ToList().ToDictionary(r => r.RutaId);

            return db.Frecuencia.ToList().ToDictionary(
                f => f.FrecuenciaId,
                f => aeropuertos[rutas[f.RutaId].AeropuertoOrigenId] + " → "
                   + aeropuertos[rutas[f.RutaId].AeropuertoDestinoId]
                   + " (" + f.HoraSalida.ToString(@"hh\:mm") + " - " + f.HoraLlegada.ToString(@"hh\:mm") + ")");
        }

        // Obtiene el mensaje real del error (el del trigger está en la excepción interna)
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