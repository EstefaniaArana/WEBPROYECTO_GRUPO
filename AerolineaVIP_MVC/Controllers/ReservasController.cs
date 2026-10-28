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

    // Todo lo que necesita la pantalla 9
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

    // CONTROLADOR DE RESERVAS DEL CLIENTE (pantallas 8, 9 y 10).
    // Flujo: SeleccionAsiento -> DatosPasajeros -> Confirmar -> Confirmacion.
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

        // SUPUESTO: tiempo que tiene el cliente para pagar antes de que expire la reserva
        private const int MinutosExpiracion = 15;

        // ---------- PANTALLA 8: SELECCIÓN DE ASIENTOS (GET) ----------
        public ActionResult SeleccionAsiento(int? id, int? tramo2, int? pasajeros)
        {
            try
            {
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

        // ---------- CONFIRMAR RESERVA (POST desde la pantalla 9) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Confirmar(int[] vuelos, int cantidad, List<PasajeroForm> pasajerosForm, FormCollection form)
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

                // 3) Se guarda todo en una sola transacción: o se guarda todo o no se guarda nada
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

                return RedirectToAction("Confirmacion", new { id = reservaId });
            }
            catch (Exception ex)
            {
                // Un trigger de SQL Server o un asiento tomado por otra persona llegan hasta aquí
                if (vista != null && vista.Tramos.Count > 0)
                {
                    ViewBag.Errores = new List<string> { "No se pudo guardar la reserva: " + Mensaje(ex) };
                    CargarTipos();
                    return View("DatosPasajeros", vista);
                }
                TempData["Error"] = "No se pudo guardar la reserva: " + Mensaje(ex);
                return RedirectToAction("Buscar", "Vuelos");
            }
        }

        // ---------- PANTALLA 10: CONFIRMACIÓN DE LA RESERVA ----------
        public ActionResult Confirmacion(int? id)
        {
            try
            {
                if (id == null)
                    return RedirectToAction("MisReservas");

                Reserva reserva = db.Reserva.Find(id);
                if (reserva == null)
                    return HttpNotFound();

                // Un cliente solo ve SUS reservas; el personal administrativo ve todas
                bool esPersonal = User.IsInRole("Administrador") || User.IsInRole("SuperUsuario");
                if (reserva.UsuarioId != User.Identity.GetUserId() && !esPersonal)
                    return RedirectToAction("AccesoDenegado", "Home");

                var filas = new List<AsignacionFila>();
                decimal total = 0;
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
                    if (a.EstadoAsignacionCodigo != "CANCELADA")
                        total += a.ImporteTramo;
                }

                ViewBag.Filas = filas;
                ViewBag.Total = total;
                return View(reserva);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar la reserva: " + Mensaje(ex);
                return RedirectToAction("MisReservas");
            }
        }

        // ---------- MIS RESERVAS: lista de las reservas del usuario con sesión ----------
        public ActionResult MisReservas()
        {
            try
            {
                string usuario = User.Identity.GetUserId();
                var lista = db.Reserva.Where(r => r.UsuarioId == usuario)
                                      .OrderByDescending(r => r.ReservaId).ToList();
                var ids = lista.Select(r => r.ReservaId).ToList();

                // Cantidad de asientos de cada reserva
                ViewBag.Asientos = db.ReservaPasajeroVuelo.Where(a => ids.Contains(a.ReservaId))
                    .GroupBy(a => a.ReservaId)
                    .Select(g => new { Id = g.Key, N = g.Count() })
                    .ToList().ToDictionary(x => x.Id, x => x.N);

                return View(lista);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudieron cargar sus reservas: " + Mensaje(ex);
                ViewBag.Asientos = new Dictionary<int, int>();
                return View(new List<Reserva>());
            }
        }

        // ---------- Métodos de ayuda ----------

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