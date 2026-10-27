using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // Tarjeta de destino para la página de inicio
    public class DestinoTarjeta
    {
        public string Nombre { get; set; }
        public string Dias { get; set; }
    }

    // CONTROLADOR DE INICIO (pantalla 1): página pública, no pide sesión.
    // Su vista está en Views/Home/Index.cshtml.
    // Los datos vienen de la base AerolineaVIP: Aeropuerto, Ruta y Frecuencia.
    public class HomeController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        private static readonly string[] Dias =
            { "", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo" };

        // INICIO: prepara la lista de origen y las tarjetas de destinos
        public ActionResult Index()
        {
            try
            {
                // Lista desplegable de origen (Quito seleccionado por defecto)
                var aeropuertos = db.Aeropuerto.Where(a => a.Activo).OrderBy(a => a.Localidad).ToList()
                    .Select(a => new { a.CodigoIATA, Texto = a.Localidad + " (" + a.CodigoIATA + ")" }).ToList();

                ViewBag.Origenes = new SelectList(aeropuertos, "CodigoIATA", "Texto", "UIO");
                ViewBag.Destinos = ArmarDestinos();
            }
            catch (Exception ex)
            {
                // Si la base falla, la página igual se muestra con un aviso
                ViewBag.Error = "No se pudieron cargar los datos de vuelos.";
                System.Diagnostics.Debug.WriteLine("Home/Index: " + ex.Message);
                ViewBag.Origenes = new SelectList(new List<string>());
                ViewBag.Destinos = new List<DestinoTarjeta>();
            }

            ViewBag.Hoy = DateTime.Today.ToString("yyyy-MM-dd");
            return View();
        }

        // DESTINOS (JSON): lo llama jQuery cuando cambia el origen.
        // Devuelve solo los destinos que tienen una ruta activa desde ese origen.
        public JsonResult Destinos(string origen)
        {
            try
            {
                Aeropuerto aeropuerto = db.Aeropuerto.FirstOrDefault(a => a.CodigoIATA == origen);
                if (aeropuerto == null)
                    return Json(new List<object>(), JsonRequestBehavior.AllowGet);

                var ids = db.Ruta.Where(r => r.Activa && r.AeropuertoOrigenId == aeropuerto.AeropuertoId)
                                 .Select(r => r.AeropuertoDestinoId).ToList();

                var destinos = db.Aeropuerto.Where(a => a.Activo && ids.Contains(a.AeropuertoId))
                    .OrderBy(a => a.Localidad).ToList()
                    .Select(a => new { codigo = a.CodigoIATA, texto = a.Localidad + " (" + a.CodigoIATA + ")" })
                    .ToList();

                return Json(destinos, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        // Arma las tarjetas: rutas desde Quito + Galápagos (con escala en Guayaquil)
        private List<DestinoTarjeta> ArmarDestinos()
        {
            var aeropuertos = db.Aeropuerto.ToList().ToDictionary(a => a.AeropuertoId);
            var rutas = db.Ruta.Where(r => r.Activa).ToList();
            var frecuencias = db.Frecuencia.Where(f => f.Activa).ToList();
            var tarjetas = new List<DestinoTarjeta>();

            // 1) Rutas que salen de Quito
            Aeropuerto quito = aeropuertos.Values.FirstOrDefault(a => a.CodigoIATA == "UIO");
            if (quito != null)
            {
                foreach (var r in rutas.Where(x => x.AeropuertoOrigenId == quito.AeropuertoId))
                {
                    tarjetas.Add(new DestinoTarjeta
                    {
                        Nombre = aeropuertos[r.AeropuertoDestinoId].Localidad,
                        Dias = DiasTexto(frecuencias.Where(f => f.RutaId == r.RutaId).Select(f => (int)f.DiaSemana))
                    });
                }
            }

            // 2) Galápagos: se llega desde Guayaquil (escala)
            Aeropuerto gps = aeropuertos.Values.FirstOrDefault(a => a.CodigoIATA == "GPS");
            if (gps != null)
            {
                Ruta rutaGps = rutas.FirstOrDefault(r => r.AeropuertoDestinoId == gps.AeropuertoId);
                if (rutaGps != null)
                {
                    tarjetas.Add(new DestinoTarjeta
                    {
                        Nombre = gps.Localidad + " (escala en Guayaquil)",
                        Dias = DiasTexto(frecuencias.Where(f => f.RutaId == rutaGps.RutaId).Select(f => (int)f.DiaSemana))
                    });
                }
            }
            return tarjetas;
        }

        // Convierte [2,4,6] en "Martes, Jueves, Sábado"
        private string DiasTexto(IEnumerable<int> dias)
        {
            return string.Join(", ", dias.Distinct().OrderBy(d => d).Select(d => Dias[d]));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }
}