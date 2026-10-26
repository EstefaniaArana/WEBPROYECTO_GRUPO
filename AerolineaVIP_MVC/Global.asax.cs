using System;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            // Al iniciar el sitio se crean los roles y usuarios iniciales
            InicializarIdentity();
        }

        // Crea roles y usuarios solo si no existen (se puede ejecutar muchas veces)
        private void InicializarIdentity()
        {
            try
            {
                var contexto = new ApplicationDbContext();
                var roles = new RoleManager<IdentityRole>(new RoleStore<IdentityRole>(contexto));
                var usuarios = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(contexto));

                // 1) Roles
                string[] nombresRoles = { "SuperUsuario", "Administrador", "Cliente" };
                foreach (string nombre in nombresRoles)
                {
                    if (!roles.RoleExists(nombre))
                        roles.Create(new IdentityRole(nombre));
                }

                // 2) Usuarios iniciales y su rol
                CrearUsuario(usuarios, "superadmin", "superadmin@aerolineavip.com", "Super123*", "SuperUsuario");
                CrearUsuario(usuarios, "admin", "admin@aerolineavip.com", "Admin123*", "Administrador");
                CrearUsuario(usuarios, "cliente1", "cliente1@aerolineavip.com", "Cliente123*", "Cliente");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al inicializar Identity: " + ex.Message);
            }
        }

        private void CrearUsuario(UserManager<ApplicationUser> manager, string nombre,
                                  string correo, string clave, string rol)
        {
            try
            {
                var usuario = manager.FindByName(nombre);

                if (usuario == null)
                {
                    usuario = new ApplicationUser { UserName = nombre, Email = correo };
                    var resultado = manager.Create(usuario, clave);

                    if (!resultado.Succeeded)
                    {
                        System.Diagnostics.Debug.WriteLine("No se creó " + nombre + ": " + string.Join(", ", resultado.Errors));
                        return;
                    }
                }

                if (!manager.IsInRole(usuario.Id, rol))
                    manager.AddToRole(usuario.Id, rol);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error con el usuario " + nombre + ": " + ex.Message);
            }
        }
    }
}