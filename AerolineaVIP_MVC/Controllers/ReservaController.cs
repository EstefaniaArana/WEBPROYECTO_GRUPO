using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // Fila simple para mostrar un asiento de la reserva en la vista
    public class AsignacionFila
    {
        public string Pasajero { get; set; }
        public string Vuelo { get; set; }
        public string Asiento { get; set; }
        public decimal Importe { get; set; }
        public string Estado { get; set; }
    }

    // CONTROLADOR DE RESERVAS (ADMINISTRACIÓN): lista, detalle y cancelación.
    // Sus vistas están en Views/Reserva (Index y Details).
    // Datos de la reserva: base AerolineaVIP (cadena AerolineaVIPEntities).
    // Nombre del usuario que reservó: base AerolineaVIP_Identity (ApplicationDbContext).
    // Reserva.UsuarioId guarda el Id de AspNetUsers; como está en OTRA base de datos,
    // no hay llave foránea y el nombre se busca aparte.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class ReservaController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // LISTAR + FILTROS: código de reserva (texto) y estado (lista desplegable)
        public ActionResult Index(string buscar, string estado)
        {
            try
            {
                var lista = db.Reserva.AsQueryable();

                if (!string.IsNullOrWhiteSpace(buscar))
                    lista = lista.Where(r => r.CodigoReserva.Contains(buscar));

                if (!string.IsNullOrEmpty(estado))
                    lista = lista.Where(r => r.EstadoReservaCodigo == estado);

                var datos = lista.OrderByDescending(r => r.ReservaId).ToList();
                var ids = datos.Select(r => r.ReservaId).ToList();

                // Usuario (base Identity): UsuarioId -> nombre de usuario
                ViewBag.Usuarios = NombresUsuarios(datos.Select(r => r.UsuarioId).Distinct().ToList());

                // Cantidad de asientos por reserva
                ViewBag.Asientos = db.ReservaPasajeroVuelo.Where(a => ids.Contains(a.ReservaId))
                    .GroupBy(a => a.ReservaId)
                    .Select(g => new { Id = g.Key, N = g.Count() })
                    .ToList().ToDictionary(x => x.Id, x => x.N);

                // Estado de la compra de cada reserva (la más reciente)
                ViewBag.Compras = db.Compra.Where(c => ids.Contains(c.ReservaId)).ToList()
                    .GroupBy(c => c.ReservaId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.CompraId).First().EstadoCompraCodigo);

                ViewBag.Buscar = buscar;
                ViewBag.Estados = new SelectList(db.EstadoReserva.ToList(), "Codigo", "Descripcion", estado);

                return View(datos);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo cargar la lista: " + Mensaje(ex);
                ViewBag.Usuarios = new Dictionary<string, string>();
                ViewBag.Asientos = new Dictionary<int, int>();
                ViewBag.Compras = new Dictionary<int, string>();
                ViewBag.Estados = new SelectList(new List<string>());
                return View(new List<Reserva>());
            }
        }

        // DETALLE: la reserva con sus pasajeros, vuelos y asientos
        public ActionResult Details(int? id)
        {
            try
            {
                if (id == null)
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                Reserva reserva = db.Reserva.Find(id);
                if (reserva == null)
                    return HttpNotFound();

                // Usuario que hizo la reserva
                var usuarios = NombresUsuarios(new List<string> { reserva.UsuarioId });
                ViewBag.Usuario = usuarios.ContainsKey(reserva.UsuarioId)
                                  ? usuarios[reserva.UsuarioId]
                                  : "(usuario no encontrado en Identity)";

                // Compra más reciente (si existe)
                Compra compra = db.Compra.Where(c => c.ReservaId == reserva.ReservaId)
                                         .OrderByDescending(c => c.CompraId).FirstOrDefault();
                ViewBag.Compra = compra == null
                    ? "Sin compra"
                    : compra.EstadoCompraCodigo + " · " + compra.Importe.ToString("N2") + " " + compra.MonedaCodigo;

                // Asientos de la reserva
                var filas = new List<AsignacionFila>();
                var asignaciones = db.ReservaPasajeroVuelo.Where(a => a.ReservaId == reserva.ReservaId)
                                                          .OrderBy(a => a.VueloId).ThenBy(a => a.NumeroAsiento).ToList();
                foreach (var a in asignaciones)
                {
                    Pasajero p = db.Pasajero.Find(a.PasajeroId);
                    filas.Add(new AsignacionFila
                    {
                        Pasajero = p != null ? p.Apellidos + " " + p.Nombres : "",
                        Vuelo = TextoVuelo(a.VueloId),
                        Asiento = CodigoAsiento(a.VueloId, a.NumeroAsiento),
                        Importe = a.ImporteTramo,
                        Estado = a.EstadoAsignacionCodigo
                    });
                }
                ViewBag.Filas = filas;

                return View(reserva);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar el detalle: " + Mensaje(ex);
                return RedirectToAction("Index");
            }
        }

        // CANCELAR (POST): cancela la reserva y libera sus asientos (vuelven a verde)
        // Política de reembolso pendiente (DP-08): NO se modifica la compra ni el pago.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Cancelar(int id)
        {
            try
            {
                Reserva reserva = db.Reserva.Find(id);
                if (reserva == null)
                    return HttpNotFound();

                // Solo se cancelan reservas pendientes o confirmadas
                if (reserva.EstadoReservaCodigo != "PENDIENTE" && reserva.EstadoReservaCodigo != "CONFIRMADA")
                {
                    TempData["Error"] = "Solo se pueden cancelar reservas pendientes o confirmadas.";
                    return RedirectToAction("Details", new { id = id });
                }

                reserva.EstadoReservaCodigo = "CANCELADA";

                // Los asientos que ocupaban inventario pasan a CANCELADA
                var asientos = db.ReservaPasajeroVuelo.Where(a => a.ReservaId == id
                                  && (a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA")).ToList();
                foreach (var a in asientos)
                    a.EstadoAsignacionCodigo = "CANCELADA";

                db.SaveChanges();                          // UPDATE en una sola transacción
                TempData["Ok"] = "Reserva cancelada. Asientos liberados: " + asientos.Count + ".";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cancelar: " + Mensaje(ex);
            }
            return RedirectToAction("Details", new { id = id });
        }

        // ---------- Métodos de ayuda ----------

        // Busca en la base de Identity el nombre de cada usuario (UsuarioId -> UserName)
        private Dictionary<string, string> NombresUsuarios(List<string> ids)
        {
            try
            {
                using (var identidad = new ApplicationDbContext())
                {
                    return identidad.Users.Where(u => ids.Contains(u.Id))
                                    .ToDictionary(u => u.Id, u => u.UserName);
                }
            }
            catch (Exception)
            {
                // Si la base de Identity falla, la pantalla igual se muestra
                return new Dictionary<string, string>();
            }
        }

        // Texto de un vuelo: "08/11/2026 09:00 · UIO → GYE"
        private string TextoVuelo(int vueloId)
        {
            Vuelo v = db.Vuelo.Find(vueloId);
            if (v == null) return "";
            Frecuencia f = db.Frecuencia.Find(v.FrecuenciaId);
            Ruta r = db.Ruta.Find(f.RutaId);
            Aeropuerto o = db.Aeropuerto.Find(r.AeropuertoOrigenId);
            Aeropuerto d = db.Aeropuerto.Find(r.AeropuertoDestinoId);
            DateTime salida = v.FechaOperacion.Date + f.HoraSalida;
            return salida.ToString("dd/MM/yyyy HH:mm") + " · " + o.CodigoIATA + " → " + d.CodigoIATA;
        }

        // Código del asiento (1D, 2I...) según el modelo de la aeronave del vuelo
        private string CodigoAsiento(int vueloId, int numero)
        {
            Vuelo v = db.Vuelo.Find(vueloId);
            if (v == null || v.AeronaveId == null) return numero.ToString();

            Aeronave a = db.Aeronave.Find(v.AeronaveId);
            if (a == null) return numero.ToString();

            Asiento s = db.Asiento.FirstOrDefault(x => x.ModeloAeronaveId == a.ModeloAeronaveId
                                                    && x.NumeroAsiento == numero);
            return s != null ? s.Codigo : numero.ToString();
        }

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