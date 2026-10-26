using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // CONTROLADOR DE CUENTAS: login, registro y cierre de sesión.
    // Sus vistas están en Views/Account (Login.cshtml y Register.cshtml).
    // Los datos de usuarios y roles vienen de la base AerolineaVIP_Identity.
    [Authorize]
    public class AccountController : Controller
    {
        private ApplicationSignInManager _signInManager;
        private ApplicationUserManager _userManager;

        public AccountController() { }

        // SignInManager: inicia sesión. UserManager: crea usuarios y asigna roles.
        public ApplicationSignInManager SignInManager
        {
            get { return _signInManager ?? HttpContext.GetOwinContext().Get<ApplicationSignInManager>(); }
            private set { _signInManager = value; }
        }

        public ApplicationUserManager UserManager
        {
            get { return _userManager ?? HttpContext.GetOwinContext().GetUserManager<ApplicationUserManager>(); }
            private set { _userManager = value; }
        }

        // LOGIN (GET): muestra el formulario
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // LOGIN (POST): valida usuario y contraseña
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model, string returnUrl)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                // model.Email contiene el NOMBRE DE USUARIO
                var result = await SignInManager.PasswordSignInAsync(
                    model.Email, model.Password, model.RememberMe, shouldLockout: false);

                if (result == SignInStatus.Success)
                    return RedirectToLocal(returnUrl);

                ModelState.AddModelError("", "Usuario o contraseña incorrectos.");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Ocurrió un error al iniciar sesión: " + ex.Message);
            }
            return View(model);
        }

        // REGISTRO (GET): muestra el formulario con la lista de roles
        [AllowAnonymous]
        public ActionResult Register()
        {
            CargarRoles();
            return View();
        }

        // REGISTRO (POST): crea el usuario y le asigna el rol
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(RegisterViewModel model)
        {
            try
            {
                // Validación en el servidor: solo se permite el rol Cliente
                if (model.Rol != "Cliente")
                    ModelState.AddModelError("Rol", "Rol no permitido.");

                if (ModelState.IsValid)
                {
                    var user = new ApplicationUser { UserName = model.UserName, Email = model.Email };
                    var result = await UserManager.CreateAsync(user, model.Password);

                    if (result.Succeeded)
                    {
                        // El rol se asigna fijo aquí, no se toma del formulario
                        await UserManager.AddToRoleAsync(user.Id, "Cliente");
                        await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                        return RedirectToAction("Index", "Home");
                    }
                    AddErrors(result);
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Ocurrió un error al registrar: " + ex.Message);
            }

            CargarRoles();
            return View(model);
        }

        // CERRAR SESIÓN
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LogOff()
        {
            AuthenticationManager.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
            return RedirectToAction("Index", "Home");
        }

        // Llena la lista desplegable: solo aparece "Cliente"
        private void CargarRoles()
        {
            ViewBag.Roles = new SelectList(new List<string> { "Cliente" });
        }

        private IAuthenticationManager AuthenticationManager
        {
            get { return HttpContext.GetOwinContext().Authentication; }
        }

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("", error);
        }

        private ActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_userManager != null) { _userManager.Dispose(); _userManager = null; }
                if (_signInManager != null) { _signInManager.Dispose(); _signInManager = null; }
            }
            base.Dispose(disposing);
        }

        // ---- Necesario para ManageController (inicio de sesión externo) ----
        private const string XsrfKey = "XsrfId";

        internal class ChallengeResult : HttpUnauthorizedResult
        {
            public ChallengeResult(string provider, string redirectUri)
                : this(provider, redirectUri, null)
            {
            }

            public ChallengeResult(string provider, string redirectUri, string userId)
            {
                LoginProvider = provider;
                RedirectUri = redirectUri;
                UserId = userId;
            }

            public string LoginProvider { get; set; }
            public string RedirectUri { get; set; }
            public string UserId { get; set; }

            public override void ExecuteResult(ControllerContext context)
            {
                var properties = new AuthenticationProperties { RedirectUri = RedirectUri };
                if (UserId != null)
                {
                    properties.Dictionary[XsrfKey] = UserId;
                }
                context.HttpContext.GetOwinContext().Authentication.Challenge(properties, LoginProvider);
            }
        }
    }
}