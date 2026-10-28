using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // ---------- Clases simples para llevar datos a las vistas ----------

    // Un vuelo ya "armado" con su ruta, horas y disponibilidad
    public class TarjetaVuelo
    {
        public int VueloId { get; set; }
        public string Codigo { get; set; }
        public int OrigenId { get; set; }
        public int DestinoId { get; set; }
        public string Origen { get; set; }          // código IATA, ej. UIO
        public string Destino { get; set; }
        public string CiudadOrigen { get; set; }
        public string CiudadDestino { get; set; }
        public DateTime Salida { get; set; }
        public DateTime Llegada { get; set; }
        public string Matricula { get; set; }
        public string Modelo { get; set; }
        public int Capacidad { get; set; }
        public int Ocupados { get; set; }
        public int Disponibles { get; set; }
        public string Estado { get; set; }
        public bool TieneAeronave { get; set; }
    }

    // Una opción de viaje: 1 tramo (directo) o 2 tramos (con escala)
    public class OpcionViaje
    {
        public List<TarjetaVuelo> Tramos { get; set; }
        public bool Vendible { get; set; }
        public string Mensaje { get; set; }          // motivo por el que no se puede seleccionar
        public string Aviso { get; set; }            // advertencia (pocos asientos)
        public int Disponibles { get; set; }
    }

    // Un asiento del mapa
    public class AsientoVista
    {
        public int NumeroAsiento { get; set; }
        public string Codigo { get; set; }
        public int Fila { get; set; }
        public string Lado { get; set; }
        public string Ubicacion { get; set; }
        public string EstadoVisual { get; set; }     // DISPONIBLE, RESERVADO u OCUPADO
    }

    // Datos de la pantalla 7
    public class DisponibilidadVista
    {
        public TarjetaVuelo Vuelo { get; set; }
        public List<AsientoVista> Asientos { get; set; }
        public int Disponibles { get; set; }
        public int Reservados { get; set; }
        public int Ocupados { get; set; }
        public DateTime Corte { get; set; }
        public bool PuedeContinuar { get; set; }
        public string Mensaje { get; set; }

        public DisponibilidadVista() { Asientos = new List<AsientoVista>(); }
    }

    // Fila de la tabla "Días de operación"
    public class DiaRuta
    {
        public string Ruta { get; set; }
        public string Dias { get; set; }
    }

    // CONTROLADOR DE VUELOS (pantallas 5, 6 y 7): búsqueda, resultados y disponibilidad.
    // Es PÚBLICO: no lleva [Authorize], cualquier visitante puede consultar.
    // Sus vistas están en Views/Vuelos (Buscar, Resultados y Disponibilidad).
    // Los datos vienen de la base AerolineaVIP (cadena AerolineaVIPEntities).
    public class VuelosController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // Tiempo mínimo entre la llegada del primer tramo y la salida del siguiente.
        // SUPUESTO de la entrega (DP-10 pendiente de aprobación): se deja en una sola constante.
        private const int MinutosConexion = 30;

        private static readonly string[] Dias =
            { "", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo" };

        // Catálogos que se cargan una vez por petición (Id -> registro)
        private Dictionary<int, Aeropuerto> aeropuertos;
        private Dictionary<int, Ruta> rutas;
        private Dictionary<int, Frecuencia> frecuencias;
        private Dictionary<int, Aeronave> aeronaves;
        private Dictionary<int, ModeloAeronave> modelos;

        // ---------- PANTALLA 5: BUSCAR ----------
        public ActionResult Buscar(string origen, string destino, DateTime? fecha, int? pasajeros)
        {
            try
            {
                CargarFormulario(origen, destino, fecha, pasajeros);
                ViewBag.DiasRutas = DiasDeOperacion();
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudieron cargar los datos: " + Mensaje(ex);
                ViewBag.Origenes = new SelectList(new List<string>());
                ViewBag.Hoy = DateTime.Today.ToString("yyyy-MM-dd");
                ViewBag.DiasRutas = new List<DiaRuta>();
            }
            return View();
        }

        // DESTINOS (JSON): lo llama jQuery cuando cambia el origen.
        // Incluye destinos directos y destinos con una escala (ej. Quito → Galápagos).
        public JsonResult Destinos(string origen)
        {
            try
            {
                Aeropuerto ap = db.Aeropuerto.FirstOrDefault(a => a.CodigoIATA == origen && a.Activo);
                if (ap == null)
                    return Json(new List<object>(), JsonRequestBehavior.AllowGet);

                CargarCatalogos();
                var ids = DestinosAlcanzables(ap.AeropuertoId);

                var lista = aeropuertos.Values
                    .Where(a => a.Activo && ids.Contains(a.AeropuertoId))
                    .OrderBy(a => a.Localidad)
                    .Select(a => new { codigo = a.CodigoIATA, texto = a.Localidad + " (" + a.CodigoIATA + ")" })
                    .ToList();

                return Json(lista, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        // ---------- PANTALLA 6: RESULTADOS ----------
        public ActionResult Resultados(string origen, string destino, DateTime? fecha, int? pasajeros, string horario)
        {
            try
            {
                CargarCatalogos();

                // 1) El servidor repite TODAS las validaciones (no confía en el navegador)
                string error = ValidarBusqueda(origen, destino, fecha, pasajeros);
                if (error != null)
                {
                    TempData["Error"] = error;
                    return RedirectToAction("Buscar", new
                    {
                        origen = origen,
                        destino = destino,
                        fecha = fecha != null ? fecha.Value.ToString("yyyy-MM-dd") : "",
                        pasajeros = pasajeros
                    });
                }

                Aeropuerto apOrigen = aeropuertos.Values.First(a => a.CodigoIATA == origen);
                Aeropuerto apDestino = aeropuertos.Values.First(a => a.CodigoIATA == destino);
                DateTime dia = fecha.Value.Date;
                int n = pasajeros.Value;

                // 2) Búsqueda de vuelos directos o con escala
                var opciones = BuscarOpciones(apOrigen.AeropuertoId, apDestino.AeropuertoId, dia, n);

                // 3) Filtro opcional por franja horaria (lista desplegable)
                if (!string.IsNullOrEmpty(horario))
                    opciones = opciones.Where(o => Franja(o.Tramos[0].Salida) == horario).ToList();

                // 4) Si no hay resultados, se informa qué días opera la ruta
                string info = null;
                if (!opciones.Any())
                {
                    Ruta directa = rutas.Values.FirstOrDefault(r => r.Activa
                        && r.AeropuertoOrigenId == apOrigen.AeropuertoId
                        && r.AeropuertoDestinoId == apDestino.AeropuertoId);
                    if (directa != null)
                    {
                        var diasRuta = frecuencias.Values.Where(f => f.Activa && f.RutaId == directa.RutaId)
                                                         .Select(f => (int)f.DiaSemana);
                        info = "La ruta " + apOrigen.Localidad + "–" + apDestino.Localidad
                             + " opera: " + DiasTexto(diasRuta) + ". Elija otra fecha.";
                    }
                }

                ViewBag.Resumen = apOrigen.Localidad + " → " + apDestino.Localidad + " · "
                                + dia.ToString("dd/MM/yyyy") + " · " + n + (n == 1 ? " pasajero" : " pasajeros");
                ViewBag.Origen = origen;
                ViewBag.Destino = destino;
                ViewBag.Fecha = dia.ToString("yyyy-MM-dd");
                ViewBag.Pasajeros = n;
                ViewBag.Info = info;
                ViewBag.Horarios = new SelectList(new List<string> { "Mañana", "Tarde", "Noche" }, horario);

                return View(opciones);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo realizar la búsqueda: " + Mensaje(ex);
                return RedirectToAction("Buscar");
            }
        }

        // ---------- PANTALLA 7: DISPONIBILIDAD (MAPA DE ASIENTOS) ----------
        public ActionResult Disponibilidad(int? id)
        {
            try
            {
                if (id == null)
                    return RedirectToAction("Buscar");

                Vuelo v = db.Vuelo.Find(id);
                if (v == null || v.EstadoVueloCodigo == "CANCELADO")
                {
                    TempData["Error"] = "El vuelo seleccionado no está disponible.";
                    return RedirectToAction("Buscar");
                }

                CargarCatalogos();
                var ocupados = OcupadosPorVuelo(new List<int> { v.VueloId });
                var vista = new DisponibilidadVista { Vuelo = Armar(v, ocupados), Corte = DateTime.Now };

                if (!vista.Vuelo.TieneAeronave)
                {
                    vista.Mensaje = "Este vuelo aún no tiene aeronave asignada; sus asientos no pueden reservarse.";
                    return View(vista);
                }

                // Estado visual de cada asiento: viene de la tabla EstadoAsignacion
                // (PENDIENTE = RESERVADO morado, CONFIRMADA = OCUPADO rojo)
                var estados = db.EstadoAsignacion.ToDictionary(e => e.Codigo, e => e.EstadoVisual);
                var asignaciones = db.ReservaPasajeroVuelo
                    .Where(r => r.VueloId == v.VueloId
                             && (r.EstadoAsignacionCodigo == "PENDIENTE" || r.EstadoAsignacionCodigo == "CONFIRMADA"))
                    .ToList();

                Aeronave aeronave = aeronaves[v.AeronaveId.Value];
                var asientos = db.Asiento.Where(s => s.ModeloAeronaveId == aeronave.ModeloAeronaveId)
                                         .OrderBy(s => s.NumeroAsiento).ToList();

                foreach (var s in asientos)
                {
                    var asignacion = asignaciones.FirstOrDefault(r => r.NumeroAsiento == s.NumeroAsiento);
                    vista.Asientos.Add(new AsientoVista
                    {
                        NumeroAsiento = s.NumeroAsiento,
                        Codigo = s.Codigo,
                        Fila = s.Fila,
                        Lado = s.Lado,
                        Ubicacion = s.Ubicacion,
                        EstadoVisual = asignacion != null ? estados[asignacion.EstadoAsignacionCodigo] : "DISPONIBLE"
                    });
                }

                vista.Disponibles = vista.Asientos.Count(a => a.EstadoVisual == "DISPONIBLE");
                vista.Reservados = vista.Asientos.Count(a => a.EstadoVisual == "RESERVADO");
                vista.Ocupados = vista.Asientos.Count(a => a.EstadoVisual == "OCUPADO");

                // ¿Se puede continuar a la selección de asientos?
                if (vista.Vuelo.Estado != "PROGRAMADO" && vista.Vuelo.Estado != "CONFIRMADO")
                    vista.Mensaje = "El vuelo no admite reservas en su estado actual.";
                else if (vista.Vuelo.Salida <= DateTime.Now)
                    vista.Mensaje = "El vuelo ya salió.";
                else if (vista.Disponibles == 0)
                    vista.Mensaje = "No quedan asientos disponibles en este vuelo.";
                else
                    vista.PuedeContinuar = true;

                return View(vista);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo consultar la disponibilidad: " + Mensaje(ex);
                return RedirectToAction("Buscar");
            }
        }

        // ---------- Métodos de ayuda ----------

        // Llena la lista de origen y los valores del formulario
        private void CargarFormulario(string origen, string destino, DateTime? fecha, int? pasajeros)
        {
            string seleccionado = string.IsNullOrEmpty(origen) ? "UIO" : origen;

            var lista = db.Aeropuerto.Where(a => a.Activo).OrderBy(a => a.Localidad).ToList()
                .Select(a => new { a.CodigoIATA, Texto = a.Localidad + " (" + a.CodigoIATA + ")" }).ToList();

            ViewBag.Origenes = new SelectList(lista, "CodigoIATA", "Texto", seleccionado);
            ViewBag.Hoy = DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.FechaValor = fecha != null ? fecha.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.PasajerosValor = pasajeros ?? 1;
            ViewBag.DestinoInicial = destino ?? "";
        }

        private void CargarCatalogos()
        {
            aeropuertos = db.Aeropuerto.ToList().ToDictionary(a => a.AeropuertoId);
            rutas = db.Ruta.ToList().ToDictionary(r => r.RutaId);
            frecuencias = db.Frecuencia.ToList().ToDictionary(f => f.FrecuenciaId);
            aeronaves = db.Aeronave.ToList().ToDictionary(a => a.AeronaveId);
            modelos = db.ModeloAeronave.ToList().ToDictionary(m => m.ModeloAeronaveId);
        }

        // Validaciones del servidor. Devuelve el mensaje de error o null si todo está bien.
        private string ValidarBusqueda(string origen, string destino, DateTime? fecha, int? pasajeros)
        {
            if (string.IsNullOrEmpty(origen) || string.IsNullOrEmpty(destino))
                return "Seleccione el origen y el destino.";
            if (origen == destino)
                return "El origen y el destino deben ser distintos.";
            if (fecha == null)
                return "Indique la fecha de salida.";
            if (fecha.Value.Date < DateTime.Today)
                return "La fecha no puede ser anterior a hoy.";
            if (pasajeros == null || pasajeros < 1 || pasajeros > 7)
                return "La cantidad de pasajeros debe estar entre 1 y 7.";

            Aeropuerto apOrigen = aeropuertos.Values.FirstOrDefault(a => a.CodigoIATA == origen && a.Activo);
            Aeropuerto apDestino = aeropuertos.Values.FirstOrDefault(a => a.CodigoIATA == destino && a.Activo);
            if (apOrigen == null || apDestino == null)
                return "El origen o el destino no existen.";

            if (!DestinosAlcanzables(apOrigen.AeropuertoId).Contains(apDestino.AeropuertoId))
                return "No existe una ruta entre el origen y el destino seleccionados.";

            return null;
        }

        // Destinos a los que se puede llegar desde un origen: directos y con una escala
        private HashSet<int> DestinosAlcanzables(int origenId)
        {
            var resultado = new HashSet<int>();
            var rutasActivas = rutas.Values.Where(r => r.Activa).ToList();
            var frecActivas = frecuencias.Values.Where(f => f.Activa).ToList();

            foreach (var r1 in rutasActivas.Where(r => r.AeropuertoOrigenId == origenId))
            {
                resultado.Add(r1.AeropuertoDestinoId);                 // destino directo

                // Destinos con escala: hay una ruta r2 que sale desde el destino de r1
                foreach (var r2 in rutasActivas.Where(r => r.AeropuertoOrigenId == r1.AeropuertoDestinoId
                                                        && r.AeropuertoDestinoId != origenId))
                {
                    // Solo si existe un día en que el segundo tramo sale después de llegar el primero
                    bool hayConexion = frecActivas.Where(f => f.RutaId == r1.RutaId).Any(f1 =>
                        frecActivas.Any(f2 => f2.RutaId == r2.RutaId
                                           && f2.DiaSemana == f1.DiaSemana
                                           && f2.HoraSalida >= f1.HoraLlegada.Add(TimeSpan.FromMinutes(MinutosConexion))));
                    if (hayConexion)
                        resultado.Add(r2.AeropuertoDestinoId);
                }
            }
            return resultado;
        }

        // Busca los vuelos de ese día: directos, o con una escala si no existe ruta directa
        private List<OpcionViaje> BuscarOpciones(int origenId, int destinoId, DateTime dia, int pasajeros)
        {
            var vuelosDia = db.Vuelo.Where(v => v.FechaOperacion == dia).ToList();
            var ocupados = OcupadosPorVuelo(vuelosDia.Select(v => v.VueloId).ToList());
            var tarjetas = vuelosDia.Select(v => Armar(v, ocupados)).ToList();
            var opciones = new List<OpcionViaje>();

            bool hayRutaDirecta = rutas.Values.Any(r => r.Activa
                && r.AeropuertoOrigenId == origenId && r.AeropuertoDestinoId == destinoId);

            if (hayRutaDirecta)
            {
                // Vuelos directos (los cancelados se muestran solo como información)
                foreach (var t in tarjetas.Where(x => x.OrigenId == origenId && x.DestinoId == destinoId)
                                          .OrderBy(x => x.Salida))
                    opciones.Add(Evaluar(new List<TarjetaVuelo> { t }, pasajeros));
            }
            else
            {
                // Con una escala: primer vuelo desde el origen, segundo hacia el destino
                foreach (var t1 in tarjetas.Where(x => x.OrigenId == origenId && x.Estado != "CANCELADO")
                                           .OrderBy(x => x.Salida))
                {
                    foreach (var t2 in tarjetas.Where(x => x.OrigenId == t1.DestinoId
                                                        && x.DestinoId == destinoId
                                                        && x.Estado != "CANCELADO")
                                               .OrderBy(x => x.Salida))
                    {
                        if (t2.Salida >= t1.Llegada.AddMinutes(MinutosConexion))
                            opciones.Add(Evaluar(new List<TarjetaVuelo> { t1, t2 }, pasajeros));
                    }
                }
            }
            return opciones;
        }

        // Decide si una opción de viaje se puede seleccionar (reglas de negocio)
        private OpcionViaje Evaluar(List<TarjetaVuelo> tramos, int pasajeros)
        {
            var op = new OpcionViaje
            {
                Tramos = tramos,
                Vendible = true,
                Disponibles = tramos.Min(t => t.Disponibles)
            };

            foreach (var t in tramos)
            {
                if (t.Estado == "CANCELADO")
                    op.Mensaje = "Vuelo cancelado.";
                else if (!t.TieneAeronave)
                    op.Mensaje = "Sin aeronave asignada: sus asientos no pueden reservarse.";   // RNP-01
                else if (t.Estado != "PROGRAMADO" && t.Estado != "CONFIRMADO")
                    op.Mensaje = "El vuelo no admite reservas en su estado actual.";
                else if (t.Salida <= DateTime.Now)
                    op.Mensaje = "El vuelo ya salió.";
                else if (t.Disponibles <= 0)
                    op.Mensaje = "Sin asientos disponibles.";
                else if (t.Disponibles < pasajeros)
                    op.Mensaje = "Solo quedan " + t.Disponibles + " asientos: no alcanzan para " + pasajeros + " pasajeros.";

                if (op.Mensaje != null)
                {
                    op.Vendible = false;
                    break;
                }
            }

            if (op.Vendible && op.Disponibles <= 3)
                op.Aviso = "Quedan " + op.Disponibles + " asientos disponibles.";

            return op;
        }

        // Arma la tarjeta de un vuelo: ruta, horas, aeronave, capacidad y asientos libres
        private TarjetaVuelo Armar(Vuelo v, Dictionary<int, int> ocupados)
        {
            Frecuencia f = frecuencias[v.FrecuenciaId];
            Ruta r = rutas[f.RutaId];
            Aeropuerto o = aeropuertos[r.AeropuertoOrigenId];
            Aeropuerto d = aeropuertos[r.AeropuertoDestinoId];

            var t = new TarjetaVuelo
            {
                VueloId = v.VueloId,
                Codigo = "VIP" + v.VueloId.ToString("00000"),
                OrigenId = o.AeropuertoId,
                DestinoId = d.AeropuertoId,
                Origen = o.CodigoIATA,
                Destino = d.CodigoIATA,
                CiudadOrigen = o.Localidad,
                CiudadDestino = d.Localidad,
                Salida = v.FechaOperacion.Date + f.HoraSalida,
                Llegada = v.FechaOperacion.Date + f.HoraLlegada,
                Estado = v.EstadoVueloCodigo
            };

            // La capacidad sale del modelo de la aeronave asignada (máximo 7)
            if (v.AeronaveId != null && aeronaves.ContainsKey(v.AeronaveId.Value))
            {
                Aeronave a = aeronaves[v.AeronaveId.Value];
                ModeloAeronave m = modelos[a.ModeloAeronaveId];
                t.TieneAeronave = true;
                t.Matricula = a.Matricula;
                t.Modelo = m.Fabricante + " " + m.Nombre;
                t.Capacidad = m.CapacidadAsientos;
            }

            t.Ocupados = ocupados.ContainsKey(v.VueloId) ? ocupados[v.VueloId] : 0;
            t.Disponibles = t.Capacidad - t.Ocupados;
            return t;
        }

        // Asientos que ocupan inventario por vuelo (reservados + ocupados): VueloId -> cantidad
        private Dictionary<int, int> OcupadosPorVuelo(List<int> vueloIds)
        {
            return db.ReservaPasajeroVuelo
                .Where(a => vueloIds.Contains(a.VueloId)
                         && (a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA"))
                .GroupBy(a => a.VueloId)
                .Select(g => new { Id = g.Key, N = g.Count() })
                .ToList().ToDictionary(x => x.Id, x => x.N);
        }

        // Tabla "Días de operación" de la pantalla 5 (sale de las tablas Ruta y Frecuencia)
        private List<DiaRuta> DiasDeOperacion()
        {
            CargarCatalogos();
            var lista = new List<DiaRuta>();
            foreach (var r in rutas.Values.Where(x => x.Activa).OrderBy(x => x.RutaId))
            {
                var dias = frecuencias.Values.Where(f => f.Activa && f.RutaId == r.RutaId)
                                             .Select(f => (int)f.DiaSemana);
                lista.Add(new DiaRuta
                {
                    Ruta = aeropuertos[r.AeropuertoOrigenId].Localidad + " – " + aeropuertos[r.AeropuertoDestinoId].Localidad,
                    Dias = DiasTexto(dias)
                });
            }
            return lista;
        }

        // Convierte [2,4,6] en "Martes, Jueves, Sábado"
        private string DiasTexto(IEnumerable<int> dias)
        {
            return string.Join(", ", dias.Distinct().OrderBy(d => d).Select(d => Dias[d]));
        }

        // Franja horaria de una hora de salida
        private string Franja(DateTime salida)
        {
            if (salida.Hour < 12) return "Mañana";
            if (salida.Hour < 18) return "Tarde";
            return "Noche";
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