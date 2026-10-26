using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    public class ItinerarioController : Controller
    {
        private AerolineaVIPEntities db = new AerolineaVIPEntities();

        // GET: Itinerario
        public ActionResult Index()
        {
            var itinerario = db.Itinerario.Include(i => i.EstadoItinerario);
            return View(itinerario.ToList());
        }

        // GET: Itinerario/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Itinerario itinerario = db.Itinerario.Find(id);
            if (itinerario == null)
            {
                return HttpNotFound();
            }
            return View(itinerario);
        }

        // GET: Itinerario/Create
        public ActionResult Create()
        {
            ViewBag.EstadoItinerarioCodigo = new SelectList(db.EstadoItinerario, "Codigo", "Descripcion");
            return View();
        }

        // POST: Itinerario/Create
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "ItinerarioId,Referencia,Nombre,EstadoItinerarioCodigo")] Itinerario itinerario)
        {
            if (ModelState.IsValid)
            {
                db.Itinerario.Add(itinerario);
                db.SaveChanges();
                return RedirectToAction("Index");
            }

            ViewBag.EstadoItinerarioCodigo = new SelectList(db.EstadoItinerario, "Codigo", "Descripcion", itinerario.EstadoItinerarioCodigo);
            return View(itinerario);
        }

        // GET: Itinerario/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Itinerario itinerario = db.Itinerario.Find(id);
            if (itinerario == null)
            {
                return HttpNotFound();
            }
            ViewBag.EstadoItinerarioCodigo = new SelectList(db.EstadoItinerario, "Codigo", "Descripcion", itinerario.EstadoItinerarioCodigo);
            return View(itinerario);
        }

        // POST: Itinerario/Edit/5
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "ItinerarioId,Referencia,Nombre,EstadoItinerarioCodigo")] Itinerario itinerario)
        {
            if (ModelState.IsValid)
            {
                db.Entry(itinerario).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            ViewBag.EstadoItinerarioCodigo = new SelectList(db.EstadoItinerario, "Codigo", "Descripcion", itinerario.EstadoItinerarioCodigo);
            return View(itinerario);
        }

        // GET: Itinerario/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Itinerario itinerario = db.Itinerario.Find(id);
            if (itinerario == null)
            {
                return HttpNotFound();
            }
            return View(itinerario);
        }

        // POST: Itinerario/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Itinerario itinerario = db.Itinerario.Find(id);
            db.Itinerario.Remove(itinerario);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
