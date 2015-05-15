using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(GuestBook_WebRole.Startup))]
namespace GuestBook_WebRole
{
    public partial class Startup {
        public void Configuration(IAppBuilder app) {
            ConfigureAuth(app);
        }
    }
}
