using System.Data.Entity;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;

namespace AerolineaVIP_MVC.Models
{
    // Usuario de Identity (tabla AspNetUsers)
    public class ApplicationUser : IdentityUser
    {
        public async Task<ClaimsIdentity> GenerateUserIdentityAsync(UserManager<ApplicationUser> manager)
        {
            var userIdentity = await manager.CreateIdentityAsync(this, DefaultAuthenticationTypes.ApplicationCookie);
            return userIdentity;
        }
    }

    // Conexión a la base AerolineaVIP_Identity (cadena DefaultConnection)
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        // No deja que Entity Framework modifique las tablas creadas por el script SQL
        static ApplicationDbContext()
        {
            Database.SetInitializer<ApplicationDbContext>(null);
        }

        public ApplicationDbContext() : base("DefaultConnection", throwIfV1Schema: false)
        {
        }

        public static ApplicationDbContext Create()
        {
            return new ApplicationDbContext();
        }
    }
}