using System;
using System.Collections.Generic;
using System.Linq;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // AYUDA PARA RESERVAS: métodos compartidos por varios controladores.
    // Los datos vienen de la base AerolineaVIP (cadena AerolineaVIPEntities).
    public static class AyudaReservas
    {
        // Mensaje real del error (el de SQL Server está en la excepción interna)
        public static string Mensaje(Exception ex)
        {
            while (ex.InnerException != null)
                ex = ex.InnerException;
            return ex.Message;
        }

        // "PENDIENTE" -> "Pendiente"
        public static string Bonito(string codigo)
        {
            if (string.IsNullOrEmpty(codigo)) return "";
            return char.ToUpper(codigo[0]) + codigo.Substring(1).ToLower().Replace("_", " ");
        }

        // Fecha y hora de salida de un vuelo
        public static DateTime SalidaVuelo(AerolineaVIPEntities db, int vueloId)
        {
            Vuelo v = db.Vuelo.Find(vueloId);
            Frecuencia f = db.Frecuencia.Find(v.FrecuenciaId);
            return v.FechaOperacion.Date + f.HoraSalida;
        }

        // Texto de un vuelo: "08/11/2026 09:00 · UIO → GYE"
        public static string TextoVuelo(AerolineaVIPEntities db, int vueloId)
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
        public static string CodigoAsiento(AerolineaVIPEntities db, int vueloId, int numero)
        {
            Vuelo v = db.Vuelo.Find(vueloId);
            if (v == null || v.AeronaveId == null) return numero.ToString();

            Aeronave a = db.Aeronave.Find(v.AeronaveId);
            if (a == null) return numero.ToString();

            Asiento s = db.Asiento.FirstOrDefault(x => x.ModeloAeronaveId == a.ModeloAeronaveId
                                                    && x.NumeroAsiento == numero);
            return s != null ? s.Codigo : numero.ToString();
        }

        // Filas de asientos de una reserva (pasajero, vuelo, asiento, importe, estado) y su total
        public static List<AsignacionFila> Filas(AerolineaVIPEntities db, int reservaId, out decimal total)
        {
            total = 0;
            var filas = new List<AsignacionFila>();
            var asignaciones = db.ReservaPasajeroVuelo.Where(a => a.ReservaId == reservaId)
                                 .OrderBy(a => a.VueloId).ThenBy(a => a.NumeroAsiento).ToList();

            foreach (var a in asignaciones)
            {
                Pasajero p = db.Pasajero.Find(a.PasajeroId);
                filas.Add(new AsignacionFila
                {
                    Pasajero = p != null ? p.Apellidos + " " + p.Nombres : "",
                    Vuelo = TextoVuelo(db, a.VueloId),
                    Asiento = CodigoAsiento(db, a.VueloId, a.NumeroAsiento),
                    Importe = a.ImporteTramo,
                    Estado = a.EstadoAsignacionCodigo
                });
                total += a.ImporteTramo;
            }
            return filas;
        }

        // EXPIRACIÓN (DP-07, supuesto de implementación): las reservas PENDIENTES cuya
        // fecha de expiración ya pasó se marcan EXPIRADA y sus asientos se liberan (vuelven a verde).
        // Se llama al consultar disponibilidad, reservas o pagos. Usa su propio contexto
        // para que un error aquí nunca afecte a la pantalla que lo llamó.
        public static void LiberarVencidas()
        {
            try
            {
                using (var db = new AerolineaVIPEntities())
                {
                    DateTime ahora = DateTime.Now;
                    var vencidas = db.Reserva.Where(r => r.EstadoReservaCodigo == "PENDIENTE"
                                                      && r.FechaExpiracion != null
                                                      && r.FechaExpiracion < ahora).ToList();
                    if (vencidas.Count == 0)
                        return;

                    foreach (var r in vencidas)
                    {
                        int id = r.ReservaId;
                        r.EstadoReservaCodigo = "EXPIRADA";

                        foreach (var a in db.ReservaPasajeroVuelo.Where(x => x.ReservaId == id
                                                                          && x.EstadoAsignacionCodigo == "PENDIENTE").ToList())
                            a.EstadoAsignacionCodigo = "LIBERADA";

                        foreach (var c in db.Compra.Where(x => x.ReservaId == id
                                                            && x.EstadoCompraCodigo == "PENDIENTE").ToList())
                            c.EstadoCompraCodigo = "ANULADA";
                    }
                    db.SaveChanges();
                }
            }
            catch (Exception)
            {
                // Si falla, no se interrumpe la pantalla: se reintentará en la próxima consulta
            }
        }
    }
}