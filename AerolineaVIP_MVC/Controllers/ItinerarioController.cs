using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // Fila simple para mostrar un tramo en la vista
    public class TramoFila
    {
        public int TramoItinerarioId { get; set; }
        public int Orden { get; set; }
        public string Texto { get; set; }
    }

    // Datos de un vuelo ya "armados" (ruta, aeropuertos y horas completas)
    public class VueloInfo
    {
        public int VueloId { get; set; }
        public int OrigenId { get; set; }
        public int DestinoId { get; set; }
        public DateTime Salida { get; set; }
        public DateTime Llegada { get; set; }
        public string Estado { get; set; }
        public string Texto { get; set; }
    }

    // CONTROLADOR DE ITINERARIOS: maneja las tablas Itinerario y TramoItinerario.
    // Un itinerario es un viaje formado por uno o más vuelos (tramos) en orden.
    // Sus vistas están en Views/Itinerario (Index, Create, Edit, Delete, Tramos y _Campos).
    // Los datos vienen de la base AerolineaVIP (cadena AerolineaVIPEntities).
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class ItinerarioController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // LISTAR + FILTROS: texto y estado (lista desplegable)
        public ActionResult Index(string buscar, string estado)
        {
            try
            {
                var lista = db.Itinerario.AsQueryable();

                if (!string.IsNullOrWhiteSpace(buscar))
                    lista = lista.Where(i => i.Referencia.Contains(buscar) || i.Nombre.Contains(buscar));

                if (!string.IsNullOrEmpty(estado))
                    lista = lista.Where(i => i.EstadoItinerarioCodigo == estado);

                // Cantidad de tramos de cada itinerario (ItinerarioId -> cantidad)
                ViewBag.Cantidad = db.TramoItinerario
                    .GroupBy(t => t.ItinerarioId)
                    .Select(g => new { Id = g.Key, N = g.Count() })
                    .ToList().ToDictionary(x => x.Id, x => x.N);

                ViewBag.Buscar = buscar;
                ViewBag.Estados = new SelectList(db.EstadoItinerario.ToList(), "Codigo", "Descripcion", estado);

                return View(lista.OrderBy(i => i.Referencia).ToList());
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo cargar la lista: " + Mensaje(ex);
                ViewBag.Cantidad = new Dictionary<int, int>();
                ViewBag.Estados = new SelectList(new List<string>());
                return View(new List<Itinerario>());
            }
        }

        // CREAR (GET)
        public ActionResult Create()
        {
            try
            {
                CargarListas("ACTIVO");
                return View(new Itinerario { EstadoItinerarioCodigo = "ACTIVO" });
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
        public ActionResult Create([Bind(Include = "Referencia,Nombre,EstadoItinerarioCodigo")] Itinerario itinerario)
        {
            try
            {
                Validar(itinerario);

                if (ModelState.IsValid)
                {
                    db.Itinerario.Add(itinerario);
                    db.SaveChanges();                      // INSERT en SQL Server
                    // Después de crearlo, se van a agregar sus tramos
                    return RedirectToAction("Tramos", new { id = itinerario.ItinerarioId });
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo guardar: " + Mensaje(ex));
            }

            CargarListas(itinerario.EstadoItinerarioCodigo);
            return View(itinerario);
        }

        // EDITAR (GET)
        public ActionResult Edit(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Itinerario itinerario = db.Itinerario.Find(id);
                if (itinerario == null)
                    return HttpNotFound();

                CargarListas(itinerario.EstadoItinerarioCodigo);
                return View(itinerario);
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
        public ActionResult Edit([Bind(Include = "ItinerarioId,Referencia,Nombre,EstadoItinerarioCodigo")] Itinerario itinerario)
        {
            try
            {
                Validar(itinerario);

                if (ModelState.IsValid)
                {
                    db.Entry(itinerario).State = System.Data.Entity.EntityState.Modified;
                    db.SaveChanges();                      // UPDATE en SQL Server
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No se pudo actualizar: " + Mensaje(ex));
            }

            CargarListas(itinerario.EstadoItinerarioCodigo);
            return View(itinerario);
        }

        // ELIMINAR (GET): solo el SuperUsuario
        [Authorize(Roles = "SuperUsuario")]
        public ActionResult Delete(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Itinerario itinerario = db.Itinerario.Find(id);
                if (itinerario == null)
                    return HttpNotFound();

                // Si tiene tramos no se puede eliminar: primero se quitan los tramos
                if (db.TramoItinerario.Any(t => t.ItinerarioId == itinerario.ItinerarioId))
                {
                    TempData["Error"] = "Primero quite los tramos del itinerario y luego elimínelo.";
                    return RedirectToAction("Index");
                }

                return View(itinerario);
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
                if (db.TramoItinerario.Any(t => t.ItinerarioId == id))
                {
                    TempData["Error"] = "Primero quite los tramos del itinerario y luego elimínelo.";
                    return RedirectToAction("Index");
                }

                Itinerario itinerario = db.Itinerario.Find(id);
                db.Itinerario.Remove(itinerario);
                db.SaveChanges();                          // DELETE en SQL Server
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se puede eliminar: " + Mensaje(ex);
            }
            return RedirectToAction("Index");
        }

        // ---------- TRAMOS ----------

        // TRAMOS (GET): muestra los tramos del itinerario y el formulario para agregar otro
        public ActionResult Tramos(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Itinerario itinerario = db.Itinerario.Find(id);
                if (itinerario == null)
                    return HttpNotFound();

                CargarTramos(itinerario);
                return View(itinerario);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudieron cargar los tramos: " + Mensaje(ex);
                return RedirectToAction("Index");
            }
        }

        // AGREGAR TRAMO (POST): el tramo nuevo va al final del itinerario
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AgregarTramo(int itinerarioId, int vueloId)
        {
            try
            {
                Itinerario itinerario = db.Itinerario.Find(itinerarioId);
                if (itinerario == null)
                    return HttpNotFound();

                var info = InfoVuelos();
                var tramos = db.TramoItinerario.Where(t => t.ItinerarioId == itinerarioId)
                                               .OrderBy(t => t.Orden).ToList();
                string error = null;

                // VALIDACIONES
                if (!info.ContainsKey(vueloId))
                    error = "El vuelo seleccionado no existe.";
                else if (info[vueloId].Estado == "CANCELADO")
                    error = "No se puede agregar un vuelo cancelado.";
                else if (tramos.Any(t => t.VueloId == vueloId))
                    error = "Ese vuelo ya está en el itinerario.";
                else if (tramos.Count > 0)
                {
                    // Continuidad: el destino del último tramo debe ser el origen del nuevo
                    VueloInfo ultimo = info[tramos.Last().VueloId];
                    VueloInfo nuevo = info[vueloId];

                    if (ultimo.DestinoId != nuevo.OrigenId)
                        error = "El nuevo tramo debe salir del aeropuerto donde termina el tramo anterior.";
                    else if (nuevo.Salida < ultimo.Llegada)
                        error = "El nuevo tramo sale antes de que llegue el tramo anterior.";
                }

                if (error != null)
                {
                    TempData["Error"] = error;
                    return RedirectToAction("Tramos", new { id = itinerarioId });
                }

                int siguienteOrden = (db.TramoItinerario.Where(t => t.ItinerarioId == itinerarioId)
                                        .Select(t => (int?)t.Orden).Max() ?? 0) + 1;

                db.TramoItinerario.Add(new TramoItinerario
                {
                    ItinerarioId = itinerarioId,
                    VueloId = vueloId,
                    Orden = (byte)siguienteOrden
                });
                db.SaveChanges();                          // INSERT en SQL Server
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo agregar el tramo: " + Mensaje(ex);
            }
            return RedirectToAction("Tramos", new { id = itinerarioId });
        }

        // QUITAR TRAMO (POST): solo se puede quitar el último, para no romper la continuidad
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult QuitarTramo(int id)
        {
            int itinerarioId = 0;
            try
            {
                TramoItinerario tramo = db.TramoItinerario.Find(id);
                if (tramo == null)
                    return HttpNotFound();

                itinerarioId = tramo.ItinerarioId;

                int maximo = db.TramoItinerario.Where(t => t.ItinerarioId == itinerarioId).Max(t => (int)t.Orden);
                if (tramo.Orden != maximo)
                {
                    TempData["Error"] = "Solo se puede quitar el último tramo.";
                    return RedirectToAction("Tramos", new { id = itinerarioId });
                }

                db.TramoItinerario.Remove(tramo);
                db.SaveChanges();                          // DELETE en SQL Server
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo quitar el tramo: " + Mensaje(ex);
            }
            return RedirectToAction("Tramos", new { id = itinerarioId });
        }

        // ---------- Métodos de ayuda ----------

        // Reglas de negocio del itinerario
        private void Validar(Itinerario i)
        {
            i.Referencia = (i.Referencia ?? "").Trim().ToUpper();
            i.Nombre = (i.Nombre ?? "").Trim();

            if (i.Referencia.Length == 0 || i.Referencia.Length > 30)
                ModelState.AddModelError("Referencia", "La referencia es obligatoria (máximo 30 caracteres).");
            else if (db.Itinerario.Any(x => x.Referencia == i.Referencia && x.ItinerarioId != i.ItinerarioId))
                ModelState.AddModelError("Referencia", "Ya existe un itinerario con esa referencia.");

            if (i.Nombre.Length == 0 || i.Nombre.Length > 150)
                ModelState.AddModelError("Nombre", "El nombre es obligatorio (máximo 150 caracteres).");

            if (!db.EstadoItinerario.Any(e => e.Codigo == i.EstadoItinerarioCodigo))
                ModelState.AddModelError("EstadoItinerarioCodigo", "El estado seleccionado no existe.");
        }

        private void CargarListas(string estado)
        {
            ViewBag.ListaEstados = new SelectList(db.EstadoItinerario.ToList(), "Codigo", "Descripcion", estado);
        }

        // Prepara la lista de tramos y la lista de vuelos que se pueden agregar
        private void CargarTramos(Itinerario itinerario)
        {
            var info = InfoVuelos();
            var tramos = db.TramoItinerario.Where(t => t.ItinerarioId == itinerario.ItinerarioId)
                                           .OrderBy(t => t.Orden).ToList();

            ViewBag.Tramos = tramos.Select(t => new TramoFila
            {
                TramoItinerarioId = t.TramoItinerarioId,
                Orden = t.Orden,
                Texto = info.ContainsKey(t.VueloId) ? info[t.VueloId].Texto : ""
            }).ToList();

            // Vuelos disponibles para agregar: los que no están cancelados ni ya usados
            var usados = tramos.Select(t => t.VueloId).ToList();
            var opciones = info.Values
                .Where(v => v.Estado != "CANCELADO" && !usados.Contains(v.VueloId))
                .OrderBy(v => v.Salida)
                .Select(v => new { v.VueloId, v.Texto }).ToList();

            ViewBag.ListaVuelos = new SelectList(opciones, "VueloId", "Texto");
        }

        // Arma, para cada vuelo, su ruta y sus horas completas (fecha + hora)
        private Dictionary<int, VueloInfo> InfoVuelos()
        {
            var aeropuertos = db.Aeropuerto.ToList().ToDictionary(a => a.AeropuertoId, a => a.CodigoIATA);
            var rutas = db.Ruta.ToList().ToDictionary(r => r.RutaId);
            var frecuencias = db.Frecuencia.ToList().ToDictionary(f => f.FrecuenciaId);

            var resultado = new Dictionary<int, VueloInfo>();
            foreach (var v in db.Vuelo.ToList())
            {
                Frecuencia f = frecuencias[v.FrecuenciaId];
                Ruta r = rutas[f.RutaId];
                DateTime salida = v.FechaOperacion.Date + f.HoraSalida;
                DateTime llegada = v.FechaOperacion.Date + f.HoraLlegada;

                resultado[v.VueloId] = new VueloInfo
                {
                    VueloId = v.VueloId,
                    OrigenId = r.AeropuertoOrigenId,
                    DestinoId = r.AeropuertoDestinoId,
                    Salida = salida,
                    Llegada = llegada,
                    Estado = v.EstadoVueloCodigo,
                    Texto = salida.ToString("dd/MM/yyyy HH:mm") + " · "
                          + aeropuertos[r.AeropuertoOrigenId] + " → " + aeropuertos[r.AeropuertoDestinoId]
                };
            }
            return resultado;
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