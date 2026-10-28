using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // ---------- Clases simples para llevar datos a las vistas ----------

    // Un vuelo de la reserva, con su mapa de asientos y los asientos elegidos
    public class TramoSeleccion
    {
        public int VueloId { get; set; }
        public int ModeloAeronaveId { get; set; }
        public string Texto { get; set; }            // "08/11/2026 09:00 · UIO → GYE"
        public decimal Tarifa { get; set; }          // tarifa por pasajero de este tramo
        public int Disponibles { get; set; }
        public List<AsientoVista> Asientos { get; set; }
        public List<int> Elegidos { get; set; }      // números de asiento elegidos

        public TramoSeleccion()
        {
            Asientos = new List<AsientoVista>();
            Elegidos = new List<int>();
        }
    }

    // Datos de un pasajero tal como se escriben en el formulario
    public class PasajeroForm
    {
        public string TipoDocumentoCodigo { get; set; }
        public string NumeroDocumento { get; set; }
        public string Nombres { get; set; }
        public string Apellidos { get; set; }
        public DateTime? FechaNacimiento { get; set; }
        public string Correo { get; set; }
        public string Telefono { get; set; }
    }

    // Todo lo que necesitan las pantallas 9 y 10
    public class DatosReservaVista
    {
        public List<TramoSeleccion> Tramos { get; set; }
        public List<PasajeroForm> Pasajeros { get; set; }
        public int Cantidad { get; set; }
        public decimal Total { get; set; }

        public DatosReservaVista()
        {
            Tramos = new List<TramoSeleccion>();
            Pasajeros = new List<PasajeroForm>();
        }
    }

    // Una fila de la pantalla "Mis reservas"
    public class FilaReserva
    {
        public int ReservaId { get; set; }
        public string Codigo { get; set; }
        public DateTime? FechaViaje { get; set; }
        public string Ruta { get; set; }
        public int Pasajeros { get; set; }
        public string EstadoReserva { get; set; }
        public string EstadoCompra { get; set; }
        public DateTime? Vence { get; set; }
        public bool PuedePagar { get; set; }
    }

    // CONTROLADOR DE RESERVAS DEL CLIENTE (pantallas 8, 9, 10, 13 y 14).
    // Flujo: SeleccionAsiento -> DatosPasajeros -> Resumen -> Confirmar -> (Compras/Pagar o Compras/Confirmacion).
    // Sus vistas están en Views/Reservas.
    // Los datos vienen de la base AerolineaVIP (cadena AerolineaVIPEntities).
    // El usuario que reserva se toma de la sesión de Identity (User.Identity.GetUserId()).
    // OJO: la administración de reservas está en ReservaController (singular).
    [Authorize]
    public class ReservasController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // Mismo supuesto que en VuelosController (DP-10 pendiente de aprobación)
        private const int MinutosConexion = 30;

        // SUPUESTO (DP-07 pendiente de aprobación): tiempo para pagar antes de que expire la reserva
        private const int MinutosExpiracion = 15;

        private const int ReservasPorPagina = 10;

        // ---------- PANTALLA 8: SELECCIÓN DE ASIENTOS ----------
        public ActionResult SeleccionAsiento(int? id, int? tramo2, int? pasajeros)
        {
            try
            {
                AyudaReservas.LiberarVencidas();   // libera asientos de reservas vencidas

                int cantidad = pasajeros ?? 1;
                if (id == null || cantidad < 1 || cantidad > 7)
                {
                    TempData["Error"] = "Seleccione un vuelo y una cantidad de pasajeros entre 1 y 7.";
                    return RedirectToAction("Buscar", "Vuelos");
                }

                var ids = new List<int> { id.Value };
                if (tramo2 != null)
                    ids.Add(tramo2.Value);

                string error;
                var tramos = ArmarTramos(ids, out error);

                if (error == null && tramos.Any(t => t.Disponibles < cantidad))
                    error = "No hay asientos suficientes para " + cantidad + " pasajero(s).";

                if (error != null)
                {
                    TempData["Error"] = error;
                    return RedirectToAction("Buscar", "Vuelos");
                }

                ViewBag.Cantidad = cantidad;
                return View(tramos);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo abrir la selección de asientos: " + Mensaje(ex);
                return RedirectToAction("Buscar", "Vuelos");
            }
        }

        // ---------- PANTALLA 9: DATOS DE LOS PASAJEROS (POST desde la pantalla 8) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DatosPasajeros(int[] vuelos, int cantidad, FormCollection form)
        {
            try
            {
                string error;
                var vista = Preparar(vuelos, cantidad, form, null, out error);

                if (error != null)
                {
                    TempData["Error"] = error;
                    return VolverASeleccion(vuelos, cantidad);
                }

                CargarTipos();
                return View(vista);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo continuar: " + Mensaje(ex);
                return VolverASeleccion(vuelos, cantidad);
            }
        }

        // ---------- PANTALLA 10: RESUMEN DE LA RESERVA (POST desde la pantalla 9) ----------
        // Valida todo y muestra el resumen con la casilla de condiciones. Todavía NO guarda nada.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Resumen(int[] vuelos, int cantidad, List<PasajeroForm> pasajerosForm, FormCollection form)
        {
            try
            {
                string error;
                var vista = Preparar(vuelos, cantidad, form, pasajerosForm, out error);
                if (error != null)
                {
                    TempData["Error"] = error;
                    return VolverASeleccion(vuelos, cantidad);
                }

                var errores = ValidarPasajeros(vista);
                if (errores.Count > 0)
                {
                    ViewBag.Errores = errores;
                    CargarTipos();
                    return View("DatosPasajeros", vista);
                }

                return View(vista);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo continuar: " + Mensaje(ex);
                return VolverASeleccion(vuelos, cantidad);
            }
        }

        // ---------- CONFIRMAR RESERVA (POST desde la pantalla 10) ----------
        // accion = "reservar" (solo reserva) o "pagar" (reserva y va a pagar)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Confirmar(int[] vuelos, int cantidad, List<PasajeroForm> pasajerosForm,
                                      FormCollection form, string accion, bool? acepta)
        {
            DatosReservaVista vista = null;
            try
            {
                // 1) Se vuelve a validar TODO en el servidor: vuelos, asientos y disponibilidad
                string error;
                vista = Preparar(vuelos, cantidad, form, pasajerosForm, out error);
                if (error != null)
                {
                    TempData["Error"] = error;
                    return VolverASeleccion(vuelos, cantidad);
                }

                // 2) Validación de los datos de los pasajeros
                var errores = ValidarPasajeros(vista);
                if (errores.Count > 0)
                {
                    ViewBag.Errores = errores;
                    CargarTipos();
                    return View("DatosPasajeros", vista);
                }

                // 3) El cliente debe aceptar las condiciones
                if (acepta != true)
                {
                    ViewBag.Errores = new List<string> { "Debe aceptar las condiciones de la reserva." };
                    return View("Resumen", vista);
                }

                // 4) Se guarda todo en una sola transacción: o se guarda todo o no se guarda nada
                int reservaId;
                using (var tx = db.Database.BeginTransaction())
                {
                    // Pasajeros: si ya existe uno con el mismo documento, se reutiliza
                    var entidades = new List<Pasajero>();
                    foreach (var p in vista.Pasajeros)
                    {
                        string tipo = p.TipoDocumentoCodigo;
                        string numero = p.NumeroDocumento;

                        Pasajero pasajero = db.Pasajero.FirstOrDefault(x => x.TipoDocumentoCodigo == tipo
                                                                         && x.NumeroDocumento == numero);
                        if (pasajero == null)
                        {
                            pasajero = new Pasajero
                            {
                                TipoDocumentoCodigo = tipo,
                                NumeroDocumento = numero,
                                Nombres = p.Nombres,
                                Apellidos = p.Apellidos,
                                FechaNacimiento = p.FechaNacimiento,
                                Correo = p.Correo,
                                Telefono = p.Telefono,
                                FechaRegistro = DateTime.Now
                            };
                            db.Pasajero.Add(pasajero);
                        }
                        entidades.Add(pasajero);
                    }

                    // Reserva: queda PENDIENTE hasta que se pague
                    var reserva = new Reserva
                    {
                        UsuarioId = User.Identity.GetUserId(),
                        FechaCreacion = DateTime.Now,
                        FechaExpiracion = DateTime.Now.AddMinutes(MinutosExpiracion),
                        EstadoReservaCodigo = "PENDIENTE"
                    };
                    db.Reserva.Add(reserva);
                    db.SaveChanges();                      // aquí se obtienen los Id nuevos

                    // Asientos: el pasajero 1 recibe el primer asiento elegido, el 2 el segundo, etc.
                    foreach (var t in vista.Tramos)
                    {
                        for (int i = 0; i < cantidad; i++)
                        {
                            int numeroAsiento = t.Elegidos[i];
                            Asiento asiento = db.Asiento.First(x => x.ModeloAeronaveId == t.ModeloAeronaveId
                                                                 && x.NumeroAsiento == numeroAsiento);

                            db.ReservaPasajeroVuelo.Add(new ReservaPasajeroVuelo
                            {
                                ReservaId = reserva.ReservaId,
                                PasajeroId = entidades[i].PasajeroId,
                                VueloId = t.VueloId,
                                NumeroAsiento = asiento.NumeroAsiento,
                                ImporteTramo = t.Tarifa,
                                EstadoAsignacionCodigo = "PENDIENTE"
                            });
                        }
                    }
                    db.SaveChanges();                      // los triggers de SQL Server validan aquí
                    tx.Commit();
                    reservaId = reserva.ReservaId;
                }

                // 5) "Confirmar y pagar" va al pago; "Confirmar reserva" va a la confirmación
                if (accion == "pagar")
                    return RedirectToAction("Pagar", "Compras", new { id = reservaId });

                return RedirectToAction("Confirmacion", "Compras", new { id = reservaId });
            }
            catch (Exception ex)
            {
                // Un trigger de SQL Server o un asiento tomado por otra persona llegan hasta aquí
                if (vista != null && vista.Tramos.Count > 0)
                {
                    ViewBag.Errores = new List<string> { "No se pudo guardar la reserva: " + Mensaje(ex) };
                    return View("Resumen", vista);
                }
                TempData["Error"] = "No se pudo guardar la reserva: " + Mensaje(ex);
                return RedirectToAction("Buscar", "Vuelos");
            }
        }

        // ---------- PANTALLA 13: MIS RESERVAS ----------
        // Filtros: fecha de viaje desde/hasta y estado. Siempre se filtra por el usuario con sesión.
        public ActionResult MisReservas(DateTime? desde, DateTime? hasta, string estado, int? pagina)
        {
            try
            {
                AyudaReservas.LiberarVencidas();
                string usuario = User.Identity.GetUserId();

                // Validación del rango (el servidor repite la validación del cliente)
                bool rangoValido = !(desde != null && hasta != null && hasta.Value.Date < desde.Value.Date);
                if (!rangoValido)
                    ViewBag.ErrorFechas = "La fecha «Hasta» no puede ser anterior a «Desde».";

                // 1) Reservas del usuario (nunca de otro), con filtro por estado
                var consulta = db.Reserva.Where(r => r.UsuarioId == usuario);
                if (!string.IsNullOrEmpty(estado))
                    consulta = consulta.Where(r => r.EstadoReservaCodigo == estado);
                var lista = consulta.OrderByDescending(r => r.ReservaId).ToList();
                var ids = lista.Select(r => r.ReservaId).ToList();

                // 2) Datos para armar el texto de cada fila
                var asignaciones = db.ReservaPasajeroVuelo.Where(a => ids.Contains(a.ReservaId)).ToList();
                var vuelos = db.Vuelo.ToList().ToDictionary(v => v.VueloId);
                var frecuencias = db.Frecuencia.ToList().ToDictionary(f => f.FrecuenciaId);
                var rutas = db.Ruta.ToList().ToDictionary(r => r.RutaId);
                var codigos = db.Aeropuerto.ToList().ToDictionary(a => a.AeropuertoId, a => a.CodigoIATA);
                var compras = db.Compra.Where(c => ids.Contains(c.ReservaId)).ToList();

                // 3) Una fila por reserva
                var filas = new List<FilaReserva>();
                foreach (var r in lista)
                {
                    var vuelosReserva = asignaciones.Where(a => a.ReservaId == r.ReservaId)
                        .Select(a => a.VueloId).Distinct()
                        .Select(vid => vuelos[vid])
                        .OrderBy(v => v.FechaOperacion).ThenBy(v => frecuencias[v.FrecuenciaId].HoraSalida)
                        .ToList();

                    // Ruta: "UIO → GYE → UIO"
                    string ruta = "";
                    if (vuelosReserva.Count > 0)
                    {
                        Ruta primera = rutas[frecuencias[vuelosReserva[0].FrecuenciaId].RutaId];
                        ruta = codigos[primera.AeropuertoOrigenId];
                        foreach (var v in vuelosReserva)
                            ruta += " → " + codigos[rutas[frecuencias[v.FrecuenciaId].RutaId].AeropuertoDestinoId];
                    }

                    Compra compra = compras.Where(c => c.ReservaId == r.ReservaId)
                                           .OrderByDescending(c => c.CompraId).FirstOrDefault();

                    filas.Add(new FilaReserva
                    {
                        ReservaId = r.ReservaId,
                        Codigo = r.CodigoReserva,
                        FechaViaje = vuelosReserva.Count > 0 ? (DateTime?)vuelosReserva[0].FechaOperacion : null,
                        Ruta = ruta,
                        Pasajeros = asignaciones.Where(a => a.ReservaId == r.ReservaId).Select(a => a.PasajeroId).Distinct().Count(),
                        EstadoReserva = AyudaReservas.Bonito(r.EstadoReservaCodigo),
                        EstadoCompra = compra != null ? AyudaReservas.Bonito(compra.EstadoCompraCodigo) : "Sin compra",
                        Vence = r.FechaExpiracion,
                        PuedePagar = r.EstadoReservaCodigo == "PENDIENTE"
                    });
                }

                // 4) Filtro por fecha de viaje
                if (rangoValido)
                {
                    if (desde != null)
                        filas = filas.Where(f => f.FechaViaje != null && f.FechaViaje.Value.Date >= desde.Value.Date).ToList();
                    if (hasta != null)
                        filas = filas.Where(f => f.FechaViaje != null && f.FechaViaje.Value.Date <= hasta.Value.Date).ToList();
                }

                // 5) Paginación
                int totalPaginas = Math.Max(1, (int)Math.Ceiling(filas.Count / (double)ReservasPorPagina));
                int actual = Math.Min(Math.Max(pagina ?? 1, 1), totalPaginas);
                var pagina10 = filas.Skip((actual - 1) * ReservasPorPagina).Take(ReservasPorPagina).ToList();

                ViewBag.Pagina = actual;
                ViewBag.TotalPaginas = totalPaginas;
                ViewBag.Desde = desde != null ? desde.Value.ToString("yyyy-MM-dd") : "";
                ViewBag.Hasta = hasta != null ? hasta.Value.ToString("yyyy-MM-dd") : "";
                ViewBag.Estado = estado ?? "";
                ViewBag.Estados = new SelectList(db.EstadoReserva.ToList(), "Codigo", "Descripcion", estado);

                return View(pagina10);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudieron cargar sus reservas: " + Mensaje(ex);
                ViewBag.Pagina = 1;
                ViewBag.TotalPaginas = 1;
                ViewBag.Desde = "";
                ViewBag.Hasta = "";
                ViewBag.Estado = "";
                ViewBag.Estados = new SelectList(new List<string>());
                return View(new List<FilaReserva>());
            }
        }

        // ---------- PANTALLA 14: GESTIÓN Y CANCELACIÓN (GET) ----------
        public ActionResult Gestionar(int? id)
        {
            try
            {
                AyudaReservas.LiberarVencidas();

                if (id == null)
                    return RedirectToAction("MisReservas");

                Reserva reserva = db.Reserva.Find(id);
                if (reserva == null)
                    return HttpNotFound();
                if (!EsTitularOPersonal(reserva))
                    return RedirectToAction("AccesoDenegado", "Home");

                decimal total;
                var filas = AyudaReservas.Filas(db, reserva.ReservaId, out total);
                Compra compra = db.Compra.Where(c => c.ReservaId == reserva.ReservaId)
                                         .OrderByDescending(c => c.CompraId).FirstOrDefault();

                ViewBag.Filas = filas;
                ViewBag.Total = total;
                ViewBag.EstadoCompra = compra != null ? AyudaReservas.Bonito(compra.EstadoCompraCodigo) : "Sin compra";
                ViewBag.CompraPagada = compra != null && compra.EstadoCompraCodigo == "PAGADA";
                ViewBag.Asientos = string.Join(", ", filas.Where(f => f.Estado == "PENDIENTE" || f.Estado == "CONFIRMADA")
                                                           .Select(f => f.Asiento).Distinct());

                string motivoNo = MotivoNoCancelar(reserva);
                ViewBag.PuedeCancelar = motivoNo == null;
                ViewBag.MotivoNoCancelar = motivoNo;

                return View(reserva);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar la reserva: " + Mensaje(ex);
                return RedirectToAction("MisReservas");
            }
        }

        // ---------- CANCELAR RESERVA (POST desde la pantalla 14) ----------
        // Cancela la reserva y libera los asientos (vuelven a verde).
        // Política de reembolso pendiente (DP-08): NO se modifica la compra pagada ni el pago.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CancelarReserva(int id, string motivo)
        {
            try
            {
                Reserva reserva = db.Reserva.Find(id);
                if (reserva == null)
                    return HttpNotFound();
                if (!EsTitularOPersonal(reserva))
                    return RedirectToAction("AccesoDenegado", "Home");

                // El motivo es opcional; si se escribe, debe tener entre 10 y 300 caracteres
                motivo = (motivo ?? "").Trim();
                if (motivo.Length > 0 && (motivo.Length < 10 || motivo.Length > 300))
                {
                    TempData["Error"] = "El motivo debe tener entre 10 y 300 caracteres.";
                    return RedirectToAction("Gestionar", new { id = id });
                }

                string razon = MotivoNoCancelar(reserva);
                if (razon != null)
                {
                    TempData["Error"] = razon;
                    return RedirectToAction("Gestionar", new { id = id });
                }

                reserva.EstadoReservaCodigo = "CANCELADA";

                var asientos = db.ReservaPasajeroVuelo.Where(a => a.ReservaId == id
                    && (a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA")).ToList();
                foreach (var a in asientos)
                    a.EstadoAsignacionCodigo = "CANCELADA";

                // Una compra que aún no se pagó se anula; una compra pagada no se toca (DP-08)
                foreach (var c in db.Compra.Where(x => x.ReservaId == id && x.EstadoCompraCodigo == "PENDIENTE").ToList())
                    c.EstadoCompraCodigo = "ANULADA";

                db.SaveChanges();
                TempData["Ok"] = "Reserva " + reserva.CodigoReserva + " cancelada. Asientos liberados: " + asientos.Count + ".";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cancelar la reserva: " + Mensaje(ex);
            }
            return RedirectToAction("Gestionar", new { id = id });
        }

        // ---------- Métodos de ayuda ----------

        // El titular de la reserva o el personal administrativo
        private bool EsTitularOPersonal(Reserva r)
        {
            return r.UsuarioId == User.Identity.GetUserId()
                || User.IsInRole("Administrador") || User.IsInRole("SuperUsuario");
        }

        // Devuelve el motivo por el que NO se puede cancelar, o null si sí se puede
        private string MotivoNoCancelar(Reserva r)
        {
            if (r.EstadoReservaCodigo != "PENDIENTE" && r.EstadoReservaCodigo != "CONFIRMADA")
                return "Solo se pueden cancelar reservas pendientes o confirmadas.";

            var activas = db.ReservaPasajeroVuelo.Where(a => a.ReservaId == r.ReservaId
                && (a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA")).ToList();

            foreach (var a in activas)
            {
                if (AyudaReservas.SalidaVuelo(db, a.VueloId) <= DateTime.Now)
                    return "La reserva incluye vuelos que ya salieron; no se puede cancelar.";
            }

            var idsAsignacion = activas.Select(a => a.AsignacionId).ToList();
            if (db.CheckIn.Any(c => idsAsignacion.Contains(c.AsignacionId) && c.EstadoCheckInCodigo == "COMPLETADO"))
                return "Ya se realizó el check-in de esta reserva; no se puede cancelar.";

            return null;
        }

        // Arma los vuelos de la reserva con su mapa de asientos y valida las reglas de negocio.
        // Si algo no se puede reservar, devuelve el motivo en "error".
        private List<TramoSeleccion> ArmarTramos(List<int> ids, out string error)
        {
            error = null;
            var tramos = new List<TramoSeleccion>();

            if (ids.Distinct().Count() != ids.Count)
            {
                error = "Los vuelos seleccionados están repetidos.";
                return tramos;
            }

            var estadosVisual = db.EstadoAsignacion.ToDictionary(e => e.Codigo, e => e.EstadoVisual);
            Ruta rutaAnterior = null;
            DateTime llegadaAnterior = DateTime.MinValue;

            foreach (int vueloId in ids)
            {
                Vuelo v = db.Vuelo.Find(vueloId);
                if (v == null)
                {
                    error = "El vuelo seleccionado no existe.";
                    return tramos;
                }
                if (v.EstadoVueloCodigo != "PROGRAMADO" && v.EstadoVueloCodigo != "CONFIRMADO")
                {
                    error = "Uno de los vuelos no admite reservas en su estado actual.";
                    return tramos;
                }
                if (v.AeronaveId == null)
                {
                    error = "Uno de los vuelos no tiene aeronave asignada; sus asientos no pueden reservarse.";
                    return tramos;
                }

                Frecuencia f = db.Frecuencia.Find(v.FrecuenciaId);
                Ruta r = db.Ruta.Find(f.RutaId);
                Aeropuerto origen = db.Aeropuerto.Find(r.AeropuertoOrigenId);
                Aeropuerto destino = db.Aeropuerto.Find(r.AeropuertoDestinoId);
                DateTime salida = v.FechaOperacion.Date + f.HoraSalida;
                DateTime llegada = v.FechaOperacion.Date + f.HoraLlegada;

                if (salida <= DateTime.Now)
                {
                    error = "Uno de los vuelos ya salió.";
                    return tramos;
                }

                // Continuidad del viaje con escala
                if (rutaAnterior != null)
                {
                    if (rutaAnterior.AeropuertoDestinoId != r.AeropuertoOrigenId)
                    {
                        error = "El segundo vuelo debe salir del aeropuerto donde termina el primero.";
                        return tramos;
                    }
                    if (salida < llegadaAnterior.AddMinutes(MinutosConexion))
                    {
                        error = "El tiempo de conexión entre los dos vuelos es insuficiente.";
                        return tramos;
                    }
                }

                // Mapa de asientos: PENDIENTE = RESERVADO, CONFIRMADA = OCUPADO
                Aeronave aeronave = db.Aeronave.Find(v.AeronaveId);
                var asignaciones = db.ReservaPasajeroVuelo
                    .Where(x => x.VueloId == vueloId
                             && (x.EstadoAsignacionCodigo == "PENDIENTE" || x.EstadoAsignacionCodigo == "CONFIRMADA"))
                    .ToList();

                var tramo = new TramoSeleccion
                {
                    VueloId = vueloId,
                    ModeloAeronaveId = aeronave.ModeloAeronaveId,
                    Texto = salida.ToString("dd/MM/yyyy HH:mm") + " · " + origen.CodigoIATA + " → " + destino.CodigoIATA,
                    Tarifa = r.TarifaReferencial
                };

                var asientos = db.Asiento.Where(s => s.ModeloAeronaveId == aeronave.ModeloAeronaveId)
                                         .OrderBy(s => s.NumeroAsiento).ToList();
                foreach (var s in asientos)
                {
                    var asignacion = asignaciones.FirstOrDefault(x => x.NumeroAsiento == s.NumeroAsiento);
                    tramo.Asientos.Add(new AsientoVista
                    {
                        NumeroAsiento = s.NumeroAsiento,
                        Codigo = s.Codigo,
                        Fila = s.Fila,
                        Lado = s.Lado,
                        Ubicacion = s.Ubicacion,
                        EstadoVisual = asignacion != null ? estadosVisual[asignacion.EstadoAsignacionCodigo] : "DISPONIBLE"
                    });
                }
                tramo.Disponibles = tramo.Asientos.Count(a => a.EstadoVisual == "DISPONIBLE");

                tramos.Add(tramo);
                rutaAnterior = r;
                llegadaAnterior = llegada;
            }
            return tramos;
        }

        // Lee los asientos elegidos del formulario y valida que sean correctos y estén libres
        private DatosReservaVista Preparar(int[] vuelos, int cantidad, FormCollection form,
                                           List<PasajeroForm> pasajeros, out string error)
        {
            error = null;
            var vista = new DatosReservaVista { Cantidad = cantidad };

            if (vuelos == null || vuelos.Length < 1 || vuelos.Length > 2)
            {
                error = "No se recibieron vuelos válidos.";
                return vista;
            }
            if (cantidad < 1 || cantidad > 7)
            {
                error = "La cantidad de pasajeros debe estar entre 1 y 7.";
                return vista;
            }

            vista.Tramos = ArmarTramos(vuelos.ToList(), out error);
            if (error != null)
                return vista;

            foreach (var t in vista.Tramos)
            {
                // Asientos marcados en el formulario para este vuelo
                var elegidos = new List<int>();
                string[] valores = form.GetValues("asientos_" + t.VueloId);
                if (valores != null)
                {
                    foreach (string texto in valores)
                    {
                        int n;
                        if (int.TryParse(texto, out n))
                            elegidos.Add(n);
                    }
                }
                elegidos = elegidos.Distinct().OrderBy(n => n).ToList();

                if (elegidos.Count != cantidad)
                {
                    error = "Seleccione exactamente " + cantidad + " asiento(s) en el vuelo " + t.Texto + ".";
                    return vista;
                }

                foreach (int n in elegidos)
                {
                    var asiento = t.Asientos.FirstOrDefault(a => a.NumeroAsiento == n);
                    if (asiento == null)
                    {
                        error = "Uno de los asientos seleccionados no es válido.";
                        return vista;
                    }
                    if (asiento.EstadoVisual != "DISPONIBLE")
                    {
                        error = "El asiento " + asiento.Codigo + " del vuelo " + t.Texto + " ya no está disponible. Elija otro.";
                        return vista;
                    }
                }
                t.Elegidos = elegidos;
            }

            vista.Pasajeros = pasajeros ?? Enumerable.Range(0, cantidad)
                .Select(i => new PasajeroForm { TipoDocumentoCodigo = "CEDULA" }).ToList();

            // El formulario debe traer exactamente un pasajero por asiento
            if (vista.Pasajeros.Count != cantidad)
            {
                error = "Los datos de los pasajeros están incompletos.";
                return vista;
            }

            vista.Total = vista.Tramos.Sum(t => t.Tarifa) * cantidad;
            return vista;
        }

        // Validaciones de los datos de los pasajeros. Devuelve la lista de errores (vacía si todo está bien).
        private List<string> ValidarPasajeros(DatosReservaVista vista)
        {
            var errores = new List<string>();
            var tipos = db.TipoDocumentoIdentidad.Select(t => t.Codigo).ToList();
            var vistos = new HashSet<string>();

            for (int i = 0; i < vista.Pasajeros.Count; i++)
            {
                var p = vista.Pasajeros[i];
                string quien = "Pasajero " + (i + 1) + ": ";

                // Se limpian los espacios
                p.TipoDocumentoCodigo = (p.TipoDocumentoCodigo ?? "").Trim();
                p.NumeroDocumento = (p.NumeroDocumento ?? "").Trim();
                p.Nombres = (p.Nombres ?? "").Trim();
                p.Apellidos = (p.Apellidos ?? "").Trim();
                p.Correo = string.IsNullOrWhiteSpace(p.Correo) ? null : p.Correo.Trim();
                p.Telefono = string.IsNullOrWhiteSpace(p.Telefono) ? null : p.Telefono.Trim();

                if (!tipos.Contains(p.TipoDocumentoCodigo))
                    errores.Add(quien + "el tipo de documento no es válido.");

                if (p.NumeroDocumento.Length < 5 || p.NumeroDocumento.Length > 20)
                    errores.Add(quien + "el documento debe tener entre 5 y 20 caracteres.");
                else if (p.TipoDocumentoCodigo == "CEDULA"
                         && (p.NumeroDocumento.Length != 10 || !p.NumeroDocumento.All(c => c >= '0' && c <= '9')))
                    errores.Add(quien + "la cédula debe tener 10 dígitos numéricos.");
                else if (!vistos.Add(p.TipoDocumentoCodigo + "|" + p.NumeroDocumento))
                    errores.Add(quien + "el documento está repetido en esta reserva.");

                if (p.Nombres.Length == 0 || p.Nombres.Length > 100)
                    errores.Add(quien + "los nombres son obligatorios (máximo 100 caracteres).");
                if (p.Apellidos.Length == 0 || p.Apellidos.Length > 100)
                    errores.Add(quien + "los apellidos son obligatorios (máximo 100 caracteres).");

                if (p.FechaNacimiento != null
                    && (p.FechaNacimiento.Value.Date > DateTime.Today || p.FechaNacimiento.Value.Year < 1900))
                    errores.Add(quien + "la fecha de nacimiento no es válida.");

                if (p.Correo != null && !CorreoValido(p.Correo))
                    errores.Add(quien + "el correo no es válido.");

                if (p.Telefono != null
                    && (p.Telefono.Length > 20 || !p.Telefono.All(c => (c >= '0' && c <= '9') || c == '+' || c == ' ')))
                    errores.Add(quien + "el teléfono solo admite números, + y espacios (máximo 20).");
            }

            // Un pasajero no puede tener dos asientos en el mismo vuelo
            if (errores.Count == 0)
            {
                foreach (var p in vista.Pasajeros)
                {
                    string tipo = p.TipoDocumentoCodigo;
                    string numero = p.NumeroDocumento;
                    Pasajero existente = db.Pasajero.FirstOrDefault(x => x.TipoDocumentoCodigo == tipo
                                                                      && x.NumeroDocumento == numero);
                    if (existente == null)
                        continue;

                    foreach (var t in vista.Tramos)
                    {
                        int vueloId = t.VueloId;
                        int pasajeroId = existente.PasajeroId;
                        bool yaTiene = db.ReservaPasajeroVuelo.Any(a => a.PasajeroId == pasajeroId
                            && a.VueloId == vueloId
                            && (a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA"));
                        if (yaTiene)
                            errores.Add(p.Apellidos + " " + p.Nombres + " ya tiene un asiento en el vuelo " + t.Texto + ".");
                    }
                }
            }
            return errores;
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

        // Lista de tipos de documento para el formulario: código -> descripción
        private void CargarTipos()
        {
            ViewBag.Tipos = db.TipoDocumentoIdentidad.ToList()
                .Select(t => new KeyValuePair<string, string>(t.Codigo, t.Descripcion)).ToList();
        }

        // Si algo falla, el cliente regresa a elegir asientos
        private ActionResult VolverASeleccion(int[] vuelos, int cantidad)
        {
            if (vuelos == null || vuelos.Length == 0)
                return RedirectToAction("Buscar", "Vuelos");

            return RedirectToAction("SeleccionAsiento", new
            {
                id = vuelos[0],
                tramo2 = vuelos.Length > 1 ? (int?)vuelos[1] : null,
                pasajeros = cantidad
            });
        }

        private string Mensaje(Exception ex)
        {
            return AyudaReservas.Mensaje(ex);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}