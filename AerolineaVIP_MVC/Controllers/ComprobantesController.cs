using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // Una fila de la lista de comprobantes
    public class FilaComprobante
    {
        public int ComprobanteId { get; set; }
        public string Numero { get; set; }
        public string Tipo { get; set; }
        public DateTime Fecha { get; set; }
        public string CodigoReserva { get; set; }
        public decimal? Importe { get; set; }
        public string Moneda { get; set; }
        public string Estado { get; set; }
    }

    // Un concepto del comprobante: un pasajero en un tramo
    public class LineaComprobante
    {
        public string Pasajero { get; set; }
        public string Documento { get; set; }
        public string Vuelo { get; set; }
        public string Asiento { get; set; }
        public decimal Importe { get; set; }
    }

    // Todos los datos que se muestran en la vista previa y en el PDF
    public class ComprobanteVista
    {
        public int ComprobanteId { get; set; }
        public string Numero { get; set; }
        public string Tipo { get; set; }
        public DateTime FechaEmision { get; set; }
        public string Estado { get; set; }
        public string EmisorNombre { get; set; }
        public string EmisorIdentificacion { get; set; }
        public string EmisorDireccion { get; set; }
        public string Leyenda { get; set; }
        public string CodigoReserva { get; set; }
        public string Cliente { get; set; }
        public decimal Total { get; set; }
        public string Moneda { get; set; }
        public string EstadoCompra { get; set; }
        public string Pago { get; set; }             // "Visa **** 1111 · SIM-RV00000001-02"
        public List<LineaComprobante> Lineas { get; set; }

        public ComprobanteVista() { Lineas = new List<LineaComprobante>(); }
    }

    // CONTROLADOR DE COMPROBANTES (pantalla 16).
    // Sus vistas están en Views/Comprobantes (Index y Ver).
    // Los datos vienen de la base AerolineaVIP: Comprobante, Compra, Reserva, Pago y DatosEmisor.
    // El nombre del cliente se busca en la base AerolineaVIP_Identity.
    // Son comprobantes ACADÉMICOS de una operación simulada: no son facturas tributarias.
    // Un cliente solo ve los de SUS reservas; el personal administrativo ve todos.
    [Authorize]
    public class ComprobantesController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // Nombre corto de cada tipo de comprobante
        private static readonly Dictionary<string, string> NombresTipo = new Dictionary<string, string>
        {
            { "CONFIRMACION", "Confirmación" },
            { "FACTURA", "Comprobante de venta académico" },
            { "NOTA_CREDITO", "Nota de crédito académica" }
        };

        // ---------- LISTA + FILTROS: fecha de emisión desde/hasta y tipo ----------
        public ActionResult Index(DateTime? desde, DateTime? hasta, string tipo)
        {
            ViewBag.Desde = desde != null ? desde.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Hasta = hasta != null ? hasta.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Tipos = new SelectList(NombresTipo.Select(x => new { Codigo = x.Key, Texto = x.Value }).ToList(),
                                           "Codigo", "Texto", tipo);
            ViewBag.TieneAlguno = false;
            try
            {
                // Validación del rango (el servidor repite la del cliente)
                bool rangoValido = !(desde != null && hasta != null && hasta.Value.Date < desde.Value.Date);
                if (!rangoValido)
                    ViewBag.ErrorFechas = "La fecha «Hasta» no puede ser anterior a «Desde».";

                // 1) Comprobantes de las reservas permitidas
                var reservaIds = ReservasPermitidas();
                var compras = db.Compra.Where(c => reservaIds.Contains(c.ReservaId)).ToList();
                var compraIds = compras.Select(c => c.CompraId).ToList();

                var todos = db.Comprobante.Where(cb => compraIds.Contains(cb.CompraId));
                ViewBag.TieneAlguno = todos.Any();

                // 2) Filtros
                var consulta = todos;
                if (!string.IsNullOrEmpty(tipo))
                    consulta = consulta.Where(cb => cb.TipoComprobanteCodigo == tipo);
                if (rangoValido)
                {
                    if (desde != null)
                    {
                        DateTime d = desde.Value.Date;
                        consulta = consulta.Where(cb => cb.FechaEmision >= d);
                    }
                    if (hasta != null)
                    {
                        DateTime h = hasta.Value.Date.AddDays(1);
                        consulta = consulta.Where(cb => cb.FechaEmision < h);
                    }
                }
                var lista = consulta.OrderByDescending(cb => cb.FechaEmision).ToList();

                // 3) Filas
                var comprasPorId = compras.ToDictionary(c => c.CompraId);
                var codigos = db.Reserva.Where(r => reservaIds.Contains(r.ReservaId)).ToList()
                                .ToDictionary(r => r.ReservaId, r => r.CodigoReserva);

                var filas = lista.Select(cb => new FilaComprobante
                {
                    ComprobanteId = cb.ComprobanteId,
                    Numero = cb.NumeroComprobante,
                    Tipo = NombresTipo.ContainsKey(cb.TipoComprobanteCodigo) ? NombresTipo[cb.TipoComprobanteCodigo] : cb.TipoComprobanteCodigo,
                    Fecha = cb.FechaEmision,
                    CodigoReserva = codigos[comprasPorId[cb.CompraId].ReservaId],
                    Importe = cb.TipoComprobanteCodigo == "CONFIRMACION" ? (decimal?)null : comprasPorId[cb.CompraId].Importe,
                    Moneda = comprasPorId[cb.CompraId].MonedaCodigo,
                    Estado = AyudaReservas.Bonito(cb.EstadoComprobanteCodigo)
                }).ToList();

                return View(filas);
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudieron cargar los comprobantes: " + AyudaReservas.Mensaje(ex);
                return View(new List<FilaComprobante>());
            }
        }

        // ---------- VISTA PREVIA ----------
        public ActionResult Ver(int? id)
        {
            try
            {
                string error;
                ComprobanteVista vista = CargarVista(id, out error);
                if (vista == null)
                {
                    TempData["Error"] = error;
                    return RedirectToAction("Index");
                }
                return View(vista);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo mostrar el comprobante: " + AyudaReservas.Mensaje(ex);
                return RedirectToAction("Index");
            }
        }

        // ---------- DESCARGAR PDF ----------
        public ActionResult Descargar(int? id)
        {
            try
            {
                string error;
                ComprobanteVista vista = CargarVista(id, out error);
                if (vista == null)
                {
                    TempData["Error"] = error;
                    return RedirectToAction("Index");
                }

                byte[] pdf = CrearPdf(vista);
                return File(pdf, "application/pdf", vista.Numero + ".pdf");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo generar el PDF: " + AyudaReservas.Mensaje(ex);
                return RedirectToAction("Index");
            }
        }

        // ---------- Métodos de ayuda ----------

        private bool EsPersonal()
        {
            return User.IsInRole("Administrador") || User.IsInRole("SuperUsuario");
        }

        // Ids de las reservas que el usuario puede ver: las suyas, o todas si es personal administrativo
        private List<int> ReservasPermitidas()
        {
            if (EsPersonal())
                return db.Reserva.Select(r => r.ReservaId).ToList();

            string usuario = User.Identity.GetUserId();
            return db.Reserva.Where(r => r.UsuarioId == usuario).Select(r => r.ReservaId).ToList();
        }

        // Reúne todos los datos del comprobante. Devuelve null (con el motivo) si no existe o no es suyo.
        private ComprobanteVista CargarVista(int? id, out string error)
        {
            error = null;
            if (id == null)
            {
                error = "No se indicó el comprobante.";
                return null;
            }

            Comprobante cb = db.Comprobante.Find(id);
            if (cb == null)
            {
                error = "No se encontró el comprobante.";
                return null;
            }

            Compra compra = db.Compra.Find(cb.CompraId);
            Reserva reserva = db.Reserva.Find(compra.ReservaId);
            if (reserva.UsuarioId != User.Identity.GetUserId() && !EsPersonal())
            {
                error = "No se encontró el comprobante.";   // mensaje neutro: no revela que existe
                return null;
            }

            DatosEmisor emisor = db.DatosEmisor.FirstOrDefault();

            var vista = new ComprobanteVista
            {
                ComprobanteId = cb.ComprobanteId,
                Numero = cb.NumeroComprobante,
                Tipo = NombresTipo.ContainsKey(cb.TipoComprobanteCodigo) ? NombresTipo[cb.TipoComprobanteCodigo] : cb.TipoComprobanteCodigo,
                FechaEmision = cb.FechaEmision,
                Estado = AyudaReservas.Bonito(cb.EstadoComprobanteCodigo),
                EmisorNombre = emisor != null ? emisor.NombreComercial : "Aerolínea VIP (proyecto académico)",
                EmisorIdentificacion = emisor != null ? emisor.Identificacion : "",
                EmisorDireccion = emisor != null ? emisor.Direccion : "",
                Leyenda = emisor != null ? emisor.LeyendaComprobante
                          : "Comprobante académico de una operación simulada. No constituye factura tributaria ni documento autorizado.",
                CodigoReserva = reserva.CodigoReserva,
                Cliente = NombreCliente(reserva.UsuarioId),
                Total = compra.Importe,
                Moneda = compra.MonedaCodigo,
                EstadoCompra = AyudaReservas.Bonito(compra.EstadoCompraCodigo)
            };

            // Pago simulado: el aprobado, si no el reversado, si no el último
            var pagos = db.Pago.Where(p => p.CompraId == compra.CompraId).ToList();
            Pago pago = pagos.FirstOrDefault(p => p.EstadoPagoCodigo == "APROBADO")
                     ?? pagos.FirstOrDefault(p => p.EstadoPagoCodigo == "REVERSADO")
                     ?? pagos.OrderByDescending(p => p.FechaPago).FirstOrDefault();
            if (pago != null)
            {
                MarcaTarjeta marca = db.MarcaTarjeta.Find(pago.MarcaTarjetaCodigo);
                vista.Pago = (marca != null ? marca.Nombre : pago.MarcaTarjetaCodigo) + " **** " + pago.UltimosCuatroDigitos
                           + " · " + pago.CodigoOperacion + " · " + AyudaReservas.Bonito(pago.EstadoPagoCodigo);
            }
            else
                vista.Pago = "Sin pago registrado";

            // Conceptos: un renglón por pasajero y tramo
            decimal total;
            foreach (var f in AyudaReservas.Filas(db, reserva.ReservaId, out total))
            {
                vista.Lineas.Add(new LineaComprobante
                {
                    Pasajero = f.Pasajero,
                    Documento = "",
                    Vuelo = f.Vuelo,
                    Asiento = f.Asiento,
                    Importe = f.Importe
                });
            }
            return vista;
        }

        // Busca el nombre del cliente en la base de Identity (UsuarioId -> UserName)
        private string NombreCliente(string usuarioId)
        {
            try
            {
                using (var identidad = new ApplicationDbContext())
                {
                    var u = identidad.Users.FirstOrDefault(x => x.Id == usuarioId);
                    return u != null ? u.UserName + " (" + u.Email + ")" : "Usuario de demostración";
                }
            }
            catch (Exception)
            {
                return "No disponible";
            }
        }

        // Quita caracteres que el PDF básico no dibuja bien (la flecha)
        private string T(string texto)
        {
            return (texto ?? "").Replace("→", "-");
        }

        // Genera el PDF con la librería PdfSharp
        private byte[] CrearPdf(ComprobanteVista c)
        {
            var doc = new PdfDocument();
            doc.Info.Title = c.Numero;

            PdfPage pagina = doc.AddPage();
            pagina.Size = PageSize.A4;
            XGraphics g = XGraphics.FromPdfPage(pagina);

            XFont fTitulo = new XFont("Arial", 16, XFontStyle.Bold);
            XFont fNegrita = new XFont("Arial", 10, XFontStyle.Bold);
            XFont fNormal = new XFont("Arial", 10, XFontStyle.Regular);
            XFont fTabla = new XFont("Arial", 8, XFontStyle.Regular);
            XFont fTablaNeg = new XFont("Arial", 8, XFontStyle.Bold);

            double izq = 40;
            double ancho = pagina.Width.Point - 80;
            double alto = pagina.Height.Point;
            double y = 50;

            // Encabezado: emisor
            g.DrawString(T(c.EmisorNombre), fTitulo, XBrushes.Black, izq, y);
            y += 16;
            g.DrawString("Identificación: " + T(c.EmisorIdentificacion), fNormal, XBrushes.Black, izq, y);
            y += 13;
            g.DrawString("Dirección: " + T(c.EmisorDireccion), fNormal, XBrushes.Black, izq, y);
            y += 22;

            // Datos del comprobante
            g.DrawString(T(c.Tipo) + " N.º " + c.Numero, fNegrita, XBrushes.Black, izq, y);
            y += 15;
            g.DrawString("Fecha de emisión: " + c.FechaEmision.ToString("dd/MM/yyyy HH:mm") + "     Estado: " + c.Estado, fNormal, XBrushes.Black, izq, y);
            y += 13;
            g.DrawString("Reserva: " + c.CodigoReserva + "     Compra: " + c.EstadoCompra, fNormal, XBrushes.Black, izq, y);
            y += 13;
            g.DrawString("Cliente: " + T(c.Cliente), fNormal, XBrushes.Black, izq, y);
            y += 13;
            g.DrawString("Pago simulado: " + T(c.Pago), fNormal, XBrushes.Black, izq, y);
            y += 20;

            // Tabla de conceptos
            g.DrawLine(XPens.Black, izq, y, izq + ancho, y);
            y += 12;
            g.DrawString("Pasajero", fTablaNeg, XBrushes.Black, izq, y);
            g.DrawString("Vuelo", fTablaNeg, XBrushes.Black, izq + 170, y);
            g.DrawString("Asiento", fTablaNeg, XBrushes.Black, izq + 380, y);
            g.DrawString("Importe", fTablaNeg, XBrushes.Black, izq + 440, y);
            y += 4;
            g.DrawLine(XPens.Black, izq, y, izq + ancho, y);
            y += 12;

            foreach (var l in c.Lineas)
            {
                // Si no hay espacio, se pasa a una hoja nueva
                if (y > alto - 120)
                {
                    g.Dispose();
                    pagina = doc.AddPage();
                    pagina.Size = PageSize.A4;
                    g = XGraphics.FromPdfPage(pagina);
                    y = 50;
                }
                string pasajero = T(l.Pasajero);
                if (pasajero.Length > 34) pasajero = pasajero.Substring(0, 34);
                g.DrawString(pasajero, fTabla, XBrushes.Black, izq, y);
                g.DrawString(T(l.Vuelo), fTabla, XBrushes.Black, izq + 170, y);
                g.DrawString(l.Asiento, fTabla, XBrushes.Black, izq + 380, y);
                g.DrawString(l.Importe.ToString("N2"), fTabla, XBrushes.Black, izq + 440, y);
                y += 13;
            }

            g.DrawLine(XPens.Black, izq, y, izq + ancho, y);
            y += 16;
            g.DrawString("TOTAL: " + c.Total.ToString("N2") + " " + c.Moneda, fNegrita, XBrushes.Black, izq + 340, y);
            y += 30;

            // Leyenda académica (con salto de línea automático)
            var formato = new XTextFormatter(g);
            formato.DrawString(T(c.Leyenda), fTabla, XBrushes.Black, new XRect(izq, y, ancho, 40));

            g.Dispose();
            using (var ms = new MemoryStream())
            {
                doc.Save(ms, false);
                return ms.ToArray();
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