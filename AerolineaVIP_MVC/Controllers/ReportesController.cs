using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // Un reporte ya armado: título, columnas y filas de texto
    public class ReporteVista
    {
        public string Titulo { get; set; }
        public List<string> Columnas { get; set; }
        public List<List<string>> Filas { get; set; }
        public DateTime Corte { get; set; }
        public string Resumen { get; set; }

        public ReporteVista()
        {
            Columnas = new List<string>();
            Filas = new List<List<string>>();
            Corte = DateTime.Now;
        }
    }

    // CONTROLADOR DE REPORTES (pantalla 21): consultas tabulares, SIN gráficos ni dashboard.
    // Su vista está en Views/Reportes (Index).
    // Los datos vienen de la base AerolineaVIP: Vuelo, Reserva, Compra, Pago y sus catálogos.
    // Tres consultas: ocupación por vuelo, reservas por estado y compras y pagos simulados.
    // No hay exportación: la función no está aprobada para esta entrega.
    [Authorize(Roles = "SuperUsuario,Administrador")]
    public class ReportesController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // CONSULTAR: todos los filtros llegan por GET
        public ActionResult Index(string consulta, DateTime? desde, DateTime? hasta, int? rutaId, int? aeronaveId, string estado)
        {
            try
            {
                CargarListas(consulta, desde, hasta, rutaId, aeronaveId, estado);

                // Primera vez: solo se muestra el formulario
                if (string.IsNullOrEmpty(consulta) && desde == null && hasta == null)
                    return View((ReporteVista)null);

                string error = Validar(consulta, desde, hasta);
                if (error != null)
                {
                    ViewBag.Error = error;
                    return View((ReporteVista)null);
                }

                ReporteVista reporte;
                if (consulta == "ocupacion")
                    reporte = Ocupacion(desde.Value, hasta.Value, rutaId, aeronaveId, estado);
                else if (consulta == "reservas")
                    reporte = ReservasPorEstado(desde.Value, hasta.Value, estado);
                else
                    reporte = ComprasYPagos(desde.Value, hasta.Value, estado);

                return View(reporte);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo ejecutar la consulta: " + AyudaReservas.Mensaje(ex);
                return View((ReporteVista)null);
            }
        }

        // Validaciones del servidor (el cliente repite las mismas)
        private string Validar(string consulta, DateTime? desde, DateTime? hasta)
        {
            if (consulta != "ocupacion" && consulta != "reservas" && consulta != "compras")
                return "Seleccione una consulta.";
            if (desde == null || hasta == null)
                return "Indique la fecha «Desde» y la fecha «Hasta».";
            if (hasta.Value.Date < desde.Value.Date)
                return "La fecha «Hasta» no puede ser anterior a «Desde».";
            if ((hasta.Value.Date - desde.Value.Date).TotalDays > 365)
                return "El rango no puede superar 366 días.";
            return null;
        }

        // ---------- CONSULTA 1: OCUPACIÓN POR VUELO ----------
        // Disponibles = Capacidad - Ocupados (asientos reservados u ocupados)
        private ReporteVista Ocupacion(DateTime desde, DateTime hasta, int? rutaId, int? aeronaveId, string estado)
        {
            DateTime d = desde.Date;
            DateTime h = hasta.Date;

            var consulta = db.Vuelo.Where(v => v.FechaOperacion >= d && v.FechaOperacion <= h);
            if (aeronaveId != null)
                consulta = consulta.Where(v => v.AeronaveId == aeronaveId);
            if (!string.IsNullOrEmpty(estado))
                consulta = consulta.Where(v => v.EstadoVueloCodigo == estado);
            var lista = consulta.ToList();

            var frecuencias = db.Frecuencia.ToList().ToDictionary(f => f.FrecuenciaId);
            var rutas = db.Ruta.ToList().ToDictionary(r => r.RutaId);
            var codigos = db.Aeropuerto.ToList().ToDictionary(a => a.AeropuertoId, a => a.CodigoIATA);
            var aeronaves = db.Aeronave.ToList().ToDictionary(a => a.AeronaveId);
            var modelos = db.ModeloAeronave.ToList().ToDictionary(m => m.ModeloAeronaveId);

            if (rutaId != null)
                lista = lista.Where(v => frecuencias[v.FrecuenciaId].RutaId == rutaId).ToList();

            var ids = lista.Select(v => v.VueloId).ToList();
            var ocupados = db.ReservaPasajeroVuelo
                .Where(a => ids.Contains(a.VueloId) && (a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA"))
                .GroupBy(a => a.VueloId).Select(g => new { Id = g.Key, N = g.Count() })
                .ToList().ToDictionary(x => x.Id, x => x.N);

            var vista = new ReporteVista { Titulo = "Ocupación por vuelo" };
            vista.Columnas.AddRange(new[] { "Vuelo", "Salida", "Ruta", "Aeronave", "Capacidad", "Ocupados", "Disponibles", "Estado" });

            int totalCapacidad = 0, totalOcupados = 0;
            foreach (var v in lista.OrderBy(x => x.FechaOperacion).ThenBy(x => frecuencias[x.FrecuenciaId].HoraSalida))
            {
                Frecuencia f = frecuencias[v.FrecuenciaId];
                Ruta r = rutas[f.RutaId];

                int capacidad = 0;
                string matricula = "Sin asignar";
                if (v.AeronaveId != null)
                {
                    Aeronave a = aeronaves[v.AeronaveId.Value];
                    matricula = a.Matricula;
                    capacidad = modelos[a.ModeloAeronaveId].CapacidadAsientos;
                }
                int ocup = ocupados.ContainsKey(v.VueloId) ? ocupados[v.VueloId] : 0;

                vista.Filas.Add(new List<string>
                {
                    "VIP" + v.VueloId.ToString("00000"),
                    v.FechaOperacion.ToString("dd/MM/yyyy") + " " + f.HoraSalida.ToString(@"hh\:mm"),
                    codigos[r.AeropuertoOrigenId] + " → " + codigos[r.AeropuertoDestinoId],
                    matricula,
                    capacidad.ToString(),
                    ocup.ToString(),
                    (capacidad - ocup).ToString(),
                    AyudaReservas.Bonito(v.EstadoVueloCodigo)
                });
                totalCapacidad += capacidad;
                totalOcupados += ocup;
            }

            vista.Resumen = "Vuelos: " + vista.Filas.Count + " · Asientos ocupados: " + totalOcupados + " de " + totalCapacidad;
            return vista;
        }

        // ---------- CONSULTA 2: RESERVAS POR ESTADO (por fecha de creación) ----------
        private ReporteVista ReservasPorEstado(DateTime desde, DateTime hasta, string estado)
        {
            DateTime d = desde.Date;
            DateTime h = hasta.Date.AddDays(1);

            var consulta = db.Reserva.Where(r => r.FechaCreacion >= d && r.FechaCreacion < h);
            if (!string.IsNullOrEmpty(estado))
                consulta = consulta.Where(r => r.EstadoReservaCodigo == estado);
            var lista = consulta.OrderByDescending(r => r.FechaCreacion).ToList();

            var ids = lista.Select(r => r.ReservaId).ToList();
            var detalle = db.ReservaPasajeroVuelo.Where(a => ids.Contains(a.ReservaId))
                .GroupBy(a => a.ReservaId)
                .Select(g => new { Id = g.Key, Asientos = g.Count(), Total = g.Sum(x => x.ImporteTramo) })
                .ToList().ToDictionary(x => x.Id);

            var vista = new ReporteVista { Titulo = "Reservas por estado" };
            vista.Columnas.AddRange(new[] { "Reserva", "Creada", "Estado", "Asientos", "Total (USD)" });

            foreach (var r in lista)
            {
                bool tiene = detalle.ContainsKey(r.ReservaId);
                vista.Filas.Add(new List<string>
                {
                    r.CodigoReserva,
                    r.FechaCreacion.ToString("dd/MM/yyyy HH:mm"),
                    AyudaReservas.Bonito(r.EstadoReservaCodigo),
                    tiene ? detalle[r.ReservaId].Asientos.ToString() : "0",
                    tiene ? detalle[r.ReservaId].Total.ToString("N2") : "0,00"
                });
            }

            vista.Resumen = "Reservas: " + lista.Count + " · "
                + string.Join(" · ", lista.GroupBy(r => r.EstadoReservaCodigo)
                                          .Select(g => AyudaReservas.Bonito(g.Key) + ": " + g.Count()));
            return vista;
        }

        // ---------- CONSULTA 3: COMPRAS Y PAGOS SIMULADOS (por fecha de compra) ----------
        private ReporteVista ComprasYPagos(DateTime desde, DateTime hasta, string estado)
        {
            DateTime d = desde.Date;
            DateTime h = hasta.Date.AddDays(1);

            var consulta = db.Compra.Where(c => c.FechaCompra >= d && c.FechaCompra < h);
            if (!string.IsNullOrEmpty(estado))
                consulta = consulta.Where(c => c.EstadoCompraCodigo == estado);
            var lista = consulta.OrderByDescending(c => c.FechaCompra).ToList();

            var compraIds = lista.Select(c => c.CompraId).ToList();
            var reservaIds = lista.Select(c => c.ReservaId).Distinct().ToList();
            var codigos = db.Reserva.Where(r => reservaIds.Contains(r.ReservaId)).ToList()
                            .ToDictionary(r => r.ReservaId, r => r.CodigoReserva);
            var pagos = db.Pago.Where(p => compraIds.Contains(p.CompraId)).ToList();

            var vista = new ReporteVista { Titulo = "Compras y pagos simulados" };
            vista.Columnas.AddRange(new[] { "Compra", "Fecha", "Reserva", "Importe", "Estado de la compra", "Último pago", "Tarjeta", "Código de operación" });

            decimal totalPagado = 0;
            foreach (var c in lista)
            {
                Pago ultimo = pagos.Where(p => p.CompraId == c.CompraId).OrderByDescending(p => p.PagoId).FirstOrDefault();
                vista.Filas.Add(new List<string>
                {
                    c.CompraId.ToString(),
                    c.FechaCompra.ToString("dd/MM/yyyy HH:mm"),
                    codigos.ContainsKey(c.ReservaId) ? codigos[c.ReservaId] : "",
                    c.Importe.ToString("N2") + " " + c.MonedaCodigo,
                    AyudaReservas.Bonito(c.EstadoCompraCodigo),
                    ultimo != null ? AyudaReservas.Bonito(ultimo.EstadoPagoCodigo) : "Sin pago",
                    ultimo != null ? ultimo.MarcaTarjetaCodigo + " **** " + ultimo.UltimosCuatroDigitos : "",
                    ultimo != null ? ultimo.CodigoOperacion : ""
                });
                if (c.EstadoCompraCodigo == "PAGADA")
                    totalPagado += c.Importe;
            }

            vista.Resumen = "Compras: " + lista.Count + " · Total de compras pagadas (simuladas): USD " + totalPagado.ToString("N2");
            return vista;
        }

        // Listas desplegables del formulario
        private void CargarListas(string consulta, DateTime? desde, DateTime? hasta, int? rutaId, int? aeronaveId, string estado)
        {
            var codigos = db.Aeropuerto.ToList().ToDictionary(a => a.AeropuertoId, a => a.CodigoIATA);
            var rutas = db.Ruta.ToList()
                .Select(r => new { r.RutaId, Texto = codigos[r.AeropuertoOrigenId] + " → " + codigos[r.AeropuertoDestinoId] })
                .OrderBy(x => x.Texto).ToList();

            ViewBag.Consulta = consulta ?? "";
            ViewBag.Desde = desde != null ? desde.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Hasta = hasta != null ? hasta.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Estado = estado ?? "";
            ViewBag.Rutas = new SelectList(rutas, "RutaId", "Texto", rutaId);
            ViewBag.Aeronaves = new SelectList(db.Aeronave.OrderBy(a => a.Matricula).ToList(), "AeronaveId", "Matricula", aeronaveId);

            // El estado depende de la consulta elegida: el navegador cambia la lista con estos datos
            ViewBag.EstadosPorConsulta = new Dictionary<string, object>
            {
                { "ocupacion", db.EstadoVuelo.ToList().Select(e => new { codigo = e.Codigo, texto = e.Descripcion }).ToList() },
                { "reservas", db.EstadoReserva.ToList().Select(e => new { codigo = e.Codigo, texto = e.Descripcion }).ToList() },
                { "compras", db.EstadoCompra.ToList().Select(e => new { codigo = e.Codigo, texto = e.Descripcion }).ToList() }
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}