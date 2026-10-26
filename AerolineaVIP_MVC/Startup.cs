using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(AerolineaVIP_MVC.Startup))]
namespace AerolineaVIP_MVC
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
