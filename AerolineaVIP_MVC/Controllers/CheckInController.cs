using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // Una fila de la tabla de pasajeros del check-in
    public class FilaCheckIn
    {
        public int AsignacionId { get; set; }
        public string Pasajero { get; set; }
        public string Vuelo { get; set; }
        public string Asiento { get; set; }
        public string Elegibilidad { get; set; }
        public bool PuedeMarcar { get; set; }
        public bool Realizado { get; set; }
    }

    // CONTROLADOR DE CHECK-IN (pantalla 15).
    // Su vista está en Views/CheckIn (Index).
    // Los datos vienen de la base AerolineaVIP: Reserva, ReservaPasajeroVuelo, Pasajero y CheckIn.
    // El check-in se registra por cada asiento (ReservaPasajeroVuelo). Solo se permite para
    // asientos CONFIRMADOS (compra pagada): el trigger trg_CheckIn_Validar también lo exige.
    [Authorize]
    public class CheckInController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // SUPUESTOS (DP-12 pendiente de aprobación): ventana del check-in.
        // Para probar hoy con vuelos de la próxima semana, cambie HorasApertura a 720.
        private const int HorasApertura = 24;    // abre 24 horas antes de la salida
        private const int MinutosCierre = 60;    // cierra 60 minutos antes de la salida

        // ---------- PANTALLA 15 (GET): localizar la reserva y ver los pasajeros ----------
        public ActionResult Index(string codigo, string documento)
        {
            ViewBag.Buscado = false;
            ViewBag.Codigo = codigo;
            ViewBag.Documento = documento;
            ViewBag.Ventana = "El check-in abre " + HorasApertura + " horas antes de la salida y cierra "
                            + MinutosCierre + " minutos antes (supuesto de implementación, DP-12).";
            try
            {
                // Sin datos: solo se muestra el formulario
                if (string.IsNullOrWhiteSpace(codigo) && string.IsNullOrWhiteSpace(documento))
                    return View(new List<FilaCheckIn>());

                codigo = (codigo ?? "").Trim().ToUpper();
                documento = (documento ?? "").Trim();
                ViewBag.Codigo = codigo;
                ViewBag.Documento = documento;

                // Validaciones del servidor (se repiten las del navegador)
                bool valido = true;
                if (!Regex.IsMatch(codigo, @"^RV\d{8}$"))
                {
                    ViewBag.ErrorCodigo = "El código debe tener el formato RV00000000.";
                    valido = false;
                }
                if (documento.Length < 5 || documento.Length > 20)
                {
                    ViewBag.ErrorDocumento = "El documento debe tener entre 5 y 20 caracteres.";
                    valido = false;
                }
                if (!valido)
                    return View(new List<FilaCheckIn>());

                Reserva reserva = BuscarReserva(codigo, documento);
                if (reserva == null)
                {
                    ViewBag.Mensaje = "No se encontró una reserva con esos datos.";   // mensaje neutro
                    return View(new List<FilaCheckIn>());
                }

                ViewBag.Buscado = true;
                ViewBag.CodigoReserva = reserva.CodigoReserva;
                return View(ConstruirFilas(reserva));
            }
            catch (Exception ex)
            {
                ViewBag.Error = "No se pudo consultar el check-in: " + AyudaReservas.Mensaje(ex);
                return View(new List<FilaCheckIn>());
            }
        }

        // ---------- CONFIRMAR CHECK-IN (POST) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Confirmar(string codigo, string documento, int[] asignaciones)
        {
            try
            {
                codigo = (codigo ?? "").Trim().ToUpper();
                documento = (documento ?? "").Trim();

                if (asignaciones == null || asignaciones.Length == 0)
                {
                    TempData["Error"] = "Seleccione al menos un pasajero.";
                    return RedirectToAction("Index", new { codigo = codigo, documento = documento });
                }

                Reserva reserva = BuscarReserva(codigo, documento);
                if (reserva == null)
                {
                    TempData["Error"] = "No se encontró una reserva con esos datos.";
                    return RedirectToAction("Index");
                }

                // Se vuelve a calcular la elegibilidad: el servidor no confía en el navegador
                var filas = ConstruirFilas(reserva);
                var elegidos = asignaciones.Distinct().ToList();
                foreach (int id in elegidos)
                {
                    var fila = filas.FirstOrDefault(f => f.AsignacionId == id);
                    if (fila == null || !fila.PuedeMarcar)
                    {
                        TempData["Error"] = "Uno de los pasajeros seleccionados no es elegible para el check-in.";
                        return RedirectToAction("Index", new { codigo = codigo, documento = documento });
                    }
                }

                foreach (int id in elegidos)
                {
                    db.CheckIn.Add(new CheckIn
                    {
                        AsignacionId = id,
                        FechaRegistro = DateTime.Now,
                        EstadoCheckInCodigo = "COMPLETADO"
                    });
                }
                db.SaveChanges();                          // INSERT (el trigger valida el asiento confirmado)

                TempData["Ok"] = "Check-in completado para " + elegidos.Count + " pasajero(s) en la reserva " + reserva.CodigoReserva + ".";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo registrar el check-in: " + AyudaReservas.Mensaje(ex);
            }
            return RedirectToAction("Index", new { codigo = codigo, documento = documento });
        }

        // ---------- Métodos de ayuda ----------

        // Busca la reserva por código y comprueba que sea del usuario (o del personal administrativo)
        // y que el documento escrito corresponda a un pasajero de esa reserva.
        private Reserva BuscarReserva(string codigo, string documento)
        {
            Reserva reserva = db.Reserva.FirstOrDefault(r => r.CodigoReserva == codigo);
            if (reserva == null)
                return null;

            bool esPersonal = User.IsInRole("Administrador") || User.IsInRole("SuperUsuario");
            if (reserva.UsuarioId != User.Identity.GetUserId() && !esPersonal)
                return null;

            int reservaId = reserva.ReservaId;
            bool pertenece = (from a in db.ReservaPasajeroVuelo
                              join p in db.Pasajero on a.PasajeroId equals p.PasajeroId
                              where a.ReservaId == reservaId && p.NumeroDocumento == documento
                              select a.AsignacionId).Any();

            return pertenece ? reserva : null;
        }

        // Una fila por cada asiento de la reserva, con su elegibilidad
        private List<FilaCheckIn> ConstruirFilas(Reserva reserva)
        {
            var filas = new List<FilaCheckIn>();
            int reservaId = reserva.ReservaId;

            var asignaciones = db.ReservaPasajeroVuelo.Where(a => a.ReservaId == reservaId)
                                 .OrderBy(a => a.VueloId).ThenBy(a => a.NumeroAsiento).ToList();
            var ids = asignaciones.Select(a => a.AsignacionId).ToList();
            var hechos = db.CheckIn.Where(c => ids.Contains(c.AsignacionId) && c.EstadoCheckInCodigo == "COMPLETADO").ToList();

            foreach (var a in asignaciones)
            {
                Pasajero p = db.Pasajero.Find(a.PasajeroId);
                Vuelo v = db.Vuelo.Find(a.VueloId);
                CheckIn hecho = hechos.FirstOrDefault(c => c.AsignacionId == a.AsignacionId);

                bool puede;
                string texto = Elegibilidad(a, v, hecho, out puede);

                filas.Add(new FilaCheckIn
                {
                    AsignacionId = a.AsignacionId,
                    Pasajero = p != null ? p.Apellidos + " " + p.Nombres : "",
                    Vuelo = AyudaReservas.TextoVuelo(db, a.VueloId),
                    Asiento = AyudaReservas.CodigoAsiento(db, a.VueloId, a.NumeroAsiento),
                    Elegibilidad = texto,
                    PuedeMarcar = puede,
                    Realizado = hecho != null
                });
            }
            return filas;
        }

        // Decide si un asiento puede hacer check-in y explica el motivo con texto
        private string Elegibilidad(ReservaPasajeroVuelo a, Vuelo v, CheckIn hecho, out bool puede)
        {
            puede = false;

            if (hecho != null)
                return "Check-in realizado el " + hecho.FechaRegistro.ToString("dd/MM/yyyy HH:mm");
            if (a.EstadoAsignacionCodigo == "PENDIENTE")
                return "No disponible: el check-in solo es para reservas pagadas";
            if (a.EstadoAsignacionCodigo != "CONFIRMADA")
                return "No disponible: asiento cancelado o liberado";
            if (v.EstadoVueloCodigo == "CANCELADO")
                return "No disponible: vuelo cancelado";
            if (v.EstadoVueloCodigo == "COMPLETADO")
                return "No disponible: vuelo realizado";

            DateTime salida = AyudaReservas.SalidaVuelo(db, v.VueloId);
            DateTime abre = salida.AddHours(-HorasApertura);
            DateTime cierra = salida.AddMinutes(-MinutosCierre);
            DateTime ahora = DateTime.Now;

            if (ahora < abre)
                return "Aún no habilitado (abre el " + abre.ToString("dd/MM/yyyy HH:mm") + ")";
            if (ahora >= cierra)
                return "Cerrado (cerró el " + cierra.ToString("dd/MM/yyyy HH:mm") + ")";

            puede = true;
            return "Elegible";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}