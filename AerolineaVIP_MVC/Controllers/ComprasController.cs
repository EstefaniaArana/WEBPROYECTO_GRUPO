using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // Datos del formulario de pago. Se leen del navegador, pero NUNCA se guardan completos.
    public class PagoForm
    {
        public string MetodoPagoCodigo { get; set; }
        public string MarcaTarjetaCodigo { get; set; }
        public string NumeroTarjeta { get; set; }
        public string Titular { get; set; }
        public string Vencimiento { get; set; }
        public string CodigoSeguridad { get; set; }
    }

    // Marca de tarjeta para dibujar los botones de la vista
    public class MarcaVista
    {
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public int LongitudNumero { get; set; }
        public int LongitudCodigo { get; set; }
        public string RutaLogo { get; set; }
    }

    // CONTROLADOR DE COMPRAS Y PAGOS SIMULADOS (pantallas 11 y 12).
    // Sus vistas están en Views/Compras (Pagar y Confirmacion).
    // Los datos vienen de la base AerolineaVIP: Reserva, Compra, Pago, MetodoPago, MarcaTarjeta y Comprobante.
    // PAGO SIMULADO: no hay pasarela ni cobros reales. Solo se guardan la marca, los últimos
    // 4 dígitos, el titular y un código de operación "SIM-...". El número completo, el
    // vencimiento y el código de seguridad se usan para validar y se descartan.
    [Authorize]
    public class ComprasController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // ---------- PANTALLA 11: PAGAR (GET) ----------
        public ActionResult Pagar(int? id)
        {
            try
            {
                AyudaReservas.LiberarVencidas();

                string error;
                Reserva reserva = BuscarReservaPagable(id, out error);
                if (reserva == null)
                {
                    TempData["Error"] = error;
                    return RedirectToAction("MisReservas", "Reservas");
                }

                PrepararVista(reserva);
                return View(new PagoForm { MetodoPagoCodigo = "TARJETA_CREDITO" });
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo abrir el pago: " + AyudaReservas.Mensaje(ex);
                return RedirectToAction("MisReservas", "Reservas");
            }
        }

        // ---------- PANTALLA 11: PAGAR (POST) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Pagar(int id, PagoForm datos)
        {
            Reserva reserva = null;
            try
            {
                AyudaReservas.LiberarVencidas();

                string error;
                reserva = BuscarReservaPagable(id, out error);
                if (reserva == null)
                {
                    TempData["Error"] = error;
                    return RedirectToAction("MisReservas", "Reservas");
                }

                // 1) Validaciones del servidor (se repiten las del navegador)
                MarcaTarjeta marca = db.MarcaTarjeta.FirstOrDefault(m => m.Codigo == datos.MarcaTarjetaCodigo && m.Activa);
                var errores = ValidarPago(datos, marca);
                if (errores.Count > 0)
                    return VolverAlFormulario(reserva, datos, errores);

                // 2) El importe lo calcula el servidor (no se acepta el que envía el navegador)
                decimal importe = db.ReservaPasajeroVuelo
                    .Where(a => a.ReservaId == id && a.EstadoAsignacionCodigo == "PENDIENTE")
                    .Sum(a => (decimal?)a.ImporteTramo) ?? 0;
                if (importe <= 0)
                    return VolverAlFormulario(reserva, datos, new List<string> { "La reserva no tiene asientos pendientes de pago." });

                // 3) Regla de la simulación: número terminado en 0000 = rechazado; otro = aprobado
                string numero = (datos.NumeroTarjeta ?? "").Replace(" ", "");
                bool aprobado = !numero.EndsWith("0000");

                // 4) Todo se guarda en una sola transacción
                using (var tx = db.Database.BeginTransaction())
                {
                    // Compra pendiente de esta reserva (si ya existe por un intento anterior, se reutiliza)
                    Compra compra = db.Compra.FirstOrDefault(c => c.ReservaId == id && c.EstadoCompraCodigo == "PENDIENTE");
                    if (compra == null)
                    {
                        compra = new Compra
                        {
                            ReservaId = id,
                            FechaCompra = DateTime.Now,
                            Importe = importe,
                            MonedaCodigo = "USD",
                            EstadoCompraCodigo = "PENDIENTE",
                            ClaveIdempotencia = Guid.NewGuid()
                        };
                        db.Compra.Add(compra);
                        db.SaveChanges();
                    }

                    // Número del intento: SIM-RV00000013-01, -02...
                    var compraIds = db.Compra.Where(c => c.ReservaId == id).Select(c => c.CompraId).ToList();
                    int intento = db.Pago.Count(p => compraIds.Contains(p.CompraId)) + 1;

                    var pago = new Pago
                    {
                        CompraId = compra.CompraId,
                        FechaPago = DateTime.Now,
                        Importe = importe,
                        EstadoPagoCodigo = aprobado ? "APROBADO" : "RECHAZADO",
                        MetodoPagoCodigo = datos.MetodoPagoCodigo,
                        MarcaTarjetaCodigo = marca.Codigo,
                        UltimosCuatroDigitos = numero.Substring(numero.Length - 4),
                        NombreTitular = datos.Titular.Trim().ToUpper(),
                        CodigoOperacion = "SIM-" + reserva.CodigoReserva + "-" + intento.ToString("00"),
                        EsSimulado = true,
                        MensajeResultado = aprobado ? "Pago simulado aprobado." : "Pago simulado rechazado."
                    };
                    db.Pago.Add(pago);

                    if (aprobado)
                    {
                        // La compra queda PAGADA, la reserva CONFIRMADA y los asientos OCUPADOS (rojo)
                        compra.EstadoCompraCodigo = "PAGADA";
                        reserva.EstadoReservaCodigo = "CONFIRMADA";

                        foreach (var a in db.ReservaPasajeroVuelo.Where(x => x.ReservaId == id
                                                                          && x.EstadoAsignacionCodigo == "PENDIENTE").ToList())
                            a.EstadoAsignacionCodigo = "CONFIRMADA";

                        // Comprobantes académicos (sin validez tributaria)
                        db.Comprobante.Add(NuevoComprobante(compra.CompraId, "CONFIRMACION", "CNF-"));
                        db.Comprobante.Add(NuevoComprobante(compra.CompraId, "FACTURA", "ACAD-"));
                    }

                    db.SaveChanges();
                    tx.Commit();
                }

                return RedirectToAction("Confirmacion", new { id = id });
            }
            catch (Exception ex)
            {
                if (reserva != null)
                    return VolverAlFormulario(reserva, datos,
                        new List<string> { "No se pudo registrar el pago: " + AyudaReservas.Mensaje(ex) });

                TempData["Error"] = "No se pudo registrar el pago: " + AyudaReservas.Mensaje(ex);
                return RedirectToAction("MisReservas", "Reservas");
            }
        }

        // ---------- PANTALLA 12: CONFIRMACIÓN DE RESERVA O COMPRA ----------
        // Muestra por separado el estado de la Reserva, la Compra y el Pago simulado.
        public ActionResult Confirmacion(int? id)
        {
            try
            {
                AyudaReservas.LiberarVencidas();

                if (id == null)
                    return RedirectToAction("MisReservas", "Reservas");

                Reserva reserva = db.Reserva.Find(id);
                if (reserva == null)
                {
                    TempData["Error"] = "No se encontró la operación solicitada.";
                    return RedirectToAction("MisReservas", "Reservas");
                }

                // Solo el titular (o el personal administrativo) ve esta operación
                bool esPersonal = User.IsInRole("Administrador") || User.IsInRole("SuperUsuario");
                if (reserva.UsuarioId != User.Identity.GetUserId() && !esPersonal)
                    return RedirectToAction("AccesoDenegado", "Home");

                Compra compra = db.Compra.Where(c => c.ReservaId == reserva.ReservaId)
                                         .OrderByDescending(c => c.CompraId).FirstOrDefault();
                Pago pago = compra == null ? null
                          : db.Pago.Where(p => p.CompraId == compra.CompraId).OrderByDescending(p => p.PagoId).FirstOrDefault();

                // Mensaje principal según el resultado real que quedó guardado
                string tipo, mensaje;
                bool pagada = compra != null && compra.EstadoCompraCodigo == "PAGADA";
                bool rechazado = pago != null && pago.EstadoPagoCodigo == "RECHAZADO" && reserva.EstadoReservaCodigo == "PENDIENTE";

                if (pagada)
                {
                    tipo = "success";
                    mensaje = "Compra " + reserva.CodigoReserva + " pagada. Pago simulado aprobado "
                            + (pago != null ? pago.CodigoOperacion : "") + ".";
                }
                else if (rechazado)
                {
                    tipo = "danger";
                    mensaje = "El pago simulado fue rechazado. La reserva sigue pendiente.";
                }
                else if (reserva.EstadoReservaCodigo == "PENDIENTE")
                {
                    tipo = "info";
                    mensaje = "Reserva " + reserva.CodigoReserva + " registrada. La compra continúa pendiente de pago.";
                }
                else
                {
                    tipo = "secondary";
                    mensaje = "Reserva " + reserva.CodigoReserva + ": " + AyudaReservas.Bonito(reserva.EstadoReservaCodigo).ToLower() + ".";
                }

                decimal total;
                ViewBag.Filas = AyudaReservas.Filas(db, reserva.ReservaId, out total);
                ViewBag.Total = total;
                ViewBag.Tipo = tipo;
                ViewBag.Mensaje = mensaje;
                ViewBag.EstadoReserva = AyudaReservas.Bonito(reserva.EstadoReservaCodigo);
                ViewBag.EstadoCompra = compra != null ? AyudaReservas.Bonito(compra.EstadoCompraCodigo) : "Sin compra";
                ViewBag.EstadoPago = pago != null ? AyudaReservas.Bonito(pago.EstadoPagoCodigo) + " (simulado)" : "Sin pago";
                ViewBag.Metodo = pago != null ? pago.MarcaTarjetaCodigo + " **** " + pago.UltimosCuatroDigitos : "";
                ViewBag.Pagada = pagada;
                ViewBag.PuedePagar = reserva.EstadoReservaCodigo == "PENDIENTE";
                ViewBag.PuedeReintentar = rechazado;

                return View(reserva);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo cargar la operación: " + AyudaReservas.Mensaje(ex);
                return RedirectToAction("MisReservas", "Reservas");
            }
        }

        // ---------- Métodos de ayuda ----------

        // Busca la reserva y comprueba que sea del usuario y que todavía se pueda pagar
        private Reserva BuscarReservaPagable(int? id, out string error)
        {
            error = null;
            if (id == null)
            {
                error = "No se indicó la reserva.";
                return null;
            }

            Reserva r = db.Reserva.Find(id);
            if (r == null || r.UsuarioId != User.Identity.GetUserId())
            {
                error = "No se encontró la reserva solicitada.";   // mensaje neutro
                return null;
            }

            if (r.EstadoReservaCodigo == "CONFIRMADA")
                error = "Esta reserva ya está pagada.";
            else if (r.EstadoReservaCodigo == "EXPIRADA")
                error = "La reserva expiró y sus asientos fueron liberados. Realice una nueva reserva.";
            else if (r.EstadoReservaCodigo == "CANCELADA")
                error = "La reserva está cancelada.";

            return error == null ? r : null;
        }

        // Datos que necesita la vista Pagar
        private void PrepararVista(Reserva reserva)
        {
            decimal total;
            ViewBag.Reserva = reserva;
            ViewBag.Filas = AyudaReservas.Filas(db, reserva.ReservaId, out total);
            ViewBag.Total = total;
            ViewBag.Metodos = db.MetodoPago.Where(m => m.Activo).ToList();
            ViewBag.Marcas = db.MarcaTarjeta.Where(m => m.Activa).ToList()
                .Select(m => new MarcaVista
                {
                    Codigo = m.Codigo,
                    Nombre = m.Nombre,
                    LongitudNumero = m.LongitudNumero,
                    LongitudCodigo = m.LongitudCodigoSeguridad,
                    RutaLogo = m.RutaLogo
                }).ToList();
        }

        // Si hay errores se vuelve al formulario. Por seguridad se vacían el número y el código.
        private ActionResult VolverAlFormulario(Reserva reserva, PagoForm datos, List<string> errores)
        {
            datos.NumeroTarjeta = null;
            datos.CodigoSeguridad = null;
            datos.Vencimiento = null;
            ViewBag.Errores = errores;
            PrepararVista(reserva);
            return View(datos);
        }

        // Validaciones del pago (solo de FORMATO: no se verifica que la tarjeta exista)
        private List<string> ValidarPago(PagoForm d, MarcaTarjeta marca)
        {
            var errores = new List<string>();

            if (!db.MetodoPago.Any(m => m.Codigo == d.MetodoPagoCodigo && m.Activo))
                errores.Add("Seleccione el método de pago.");

            if (marca == null)
            {
                errores.Add("Seleccione la marca de la tarjeta.");
                return errores;
            }

            string numero = (d.NumeroTarjeta ?? "").Replace(" ", "");
            if (numero.Length == 0 || !numero.All(c => c >= '0' && c <= '9'))
                errores.Add("El número de tarjeta solo admite dígitos.");
            else if (numero.Length != marca.LongitudNumero)
                errores.Add("El número debe tener " + marca.LongitudNumero + " dígitos para " + marca.Nombre + ".");

            string titular = (d.Titular ?? "").Trim();
            if (!Regex.IsMatch(titular, @"^[A-Za-zÁÉÍÓÚÜÑáéíóúüñ ]{2,100}$"))
                errores.Add("El nombre del titular solo admite letras y espacios (2 a 100 caracteres).");

            string venc = (d.Vencimiento ?? "").Trim();
            if (!Regex.IsMatch(venc, @"^(0[1-9]|1[0-2])/\d{2}$"))
                errores.Add("El vencimiento debe tener el formato MM/AA con un mes entre 01 y 12.");
            else
            {
                int mes = int.Parse(venc.Substring(0, 2));
                int anio = 2000 + int.Parse(venc.Substring(3, 2));
                DateTime ultimoDia = new DateTime(anio, mes, 1).AddMonths(1).AddDays(-1);
                if (ultimoDia < DateTime.Today)
                    errores.Add("La tarjeta está vencida.");
            }

            string codigo = (d.CodigoSeguridad ?? "").Trim();
            if (codigo.Length == 0 || !codigo.All(c => c >= '0' && c <= '9'))
                errores.Add("El código de seguridad solo admite dígitos.");
            else if (codigo.Length != marca.LongitudCodigoSeguridad)
                errores.Add("El código de seguridad debe tener " + marca.LongitudCodigoSeguridad + " dígitos para " + marca.Nombre + ".");

            return errores;
        }

        // Crea un comprobante académico con numeración de ejemplo (ACAD-000002, CNF-000002...)
        private Comprobante NuevoComprobante(int compraId, string tipo, string prefijo)
        {
            int n = db.Comprobante.Count(c => c.TipoComprobanteCodigo == tipo) + 1;
            string numero;
            do
            {
                numero = prefijo + n.ToString("000000");
                n++;
            } while (db.Comprobante.Any(c => c.TipoComprobanteCodigo == tipo && c.NumeroComprobante == numero));

            return new Comprobante
            {
                CompraId = compraId,
                TipoComprobanteCodigo = tipo,
                NumeroComprobante = numero,
                FechaEmision = DateTime.Now,
                EstadoComprobanteCodigo = "EMITIDO"
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