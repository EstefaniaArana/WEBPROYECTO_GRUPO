using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;
using AerolineaVIP_MVC.Models;

namespace AerolineaVIP_MVC.Controllers
{
    // CONTROLADOR DE CUENTAS: login, registro, recuperación de acceso y cierre de sesión.
    // Sus vistas están en Views/Account (Login, Register, ForgotPassword y ResetPassword).
    // Los datos de usuarios y roles vienen de la base AerolineaVIP_Identity.
    // Nombres y apellidos se guardan como "claims" del usuario (tabla AspNetUserClaims),
    // así no hace falta modificar la tabla AspNetUsers.
    [Authorize]
    public class AccountController : Controller
    {
        private ApplicationSignInManager _signInManager;
        private ApplicationUserManager _userManager;

        public AccountController() { }

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

        // ---------- PANTALLA 2: LOGIN ----------
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model, string returnUrl)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                // model.Email contiene el nombre de usuario O el correo
                string usuario = model.Email.Trim();
                if (usuario.Contains("@"))
                {
                    var porCorreo = await UserManager.FindByEmailAsync(usuario);
                    if (porCorreo != null)
                        usuario = porCorreo.UserName;
                }

                var result = await SignInManager.PasswordSignInAsync(usuario, model.Password, model.RememberMe, shouldLockout: false);
                if (result == SignInStatus.Success)
                    return RedirectToLocal(returnUrl);

                // Mensaje genérico: no revela si la cuenta existe
                ModelState.AddModelError("", "Usuario o contraseña incorrectos.");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Ocurrió un error al iniciar sesión: " + ex.Message);
            }
            return View(model);
        }

        // ---------- PANTALLA 3: REGISTRO ----------
        // Un visitante SIEMPRE queda como Cliente. Solo el SuperUsuario puede crear Administradores.
        [AllowAnonymous]
        public ActionResult Register()
        {
            CargarRoles();
            return View(new RegisterViewModel { Rol = "Cliente" });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(RegisterViewModel model, bool? acepta)
        {
            try
            {
                bool esSuper = EsSuperUsuario();

                // Validación de seguridad en el servidor (no se confía en el formulario)
                if (!esSuper)
                {
                    model.Rol = "Cliente";
                    ModelState.Remove("Rol");
                }
                else if (model.Rol != "Cliente" && model.Rol != "Administrador")
                {
                    ModelState.AddModelError("Rol", "Rol no permitido.");
                }

                if (acepta != true)
                    ModelState.AddModelError("", "Debe aceptar el aviso de privacidad y las condiciones.");

                if (ModelState.IsValid)
                {
                    var user = new ApplicationUser { UserName = model.UserName.Trim(), Email = model.Email.Trim() };
                    var result = await UserManager.CreateAsync(user, model.Password);

                    if (result.Succeeded)
                    {
                        var rolResultado = await UserManager.AddToRoleAsync(user.Id, model.Rol);
                        if (!rolResultado.Succeeded)
                        {
                            // Si no se pudo asignar el rol, no queda un usuario sin rol
                            await UserManager.DeleteAsync(user);
                            AddErrors(rolResultado);
                        }
                        else
                        {
                            await UserManager.AddClaimAsync(user.Id, new Claim("Nombres", model.Nombres.Trim()));
                            await UserManager.AddClaimAsync(user.Id, new Claim("Apellidos", model.Apellidos.Trim()));

                            if (esSuper)
                            {
                                TempData["Ok"] = "Usuario " + user.UserName + " creado con el rol " + model.Rol + ".";
                                return RedirectToAction("Index", "Usuarios");
                            }

                            await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                            return RedirectToAction("Index", "Home");
                        }
                    }
                    else
                    {
                        AddErrors(result);   // usuario o correo repetido, clave débil, etc.
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Ocurrió un error al registrar: " + ex.Message);
            }

            CargarRoles();
            return View(model);
        }

        // ---------- PANTALLA 4: RECUPERAR ACCESO ----------
        [AllowAnonymous]
        public ActionResult ForgotPassword()
        {
            return View();
        }

        // Siempre se muestra el MISMO mensaje, exista o no la cuenta.
        // ENVÍO SIMULADO (decisión del grupo): no se envía correo real; el enlace de recuperación
        // se escribe en la ventana "Salida > Depurar" de Visual Studio.
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                // Control de solicitudes repetidas: una por minuto por sesión
                DateTime? ultima = Session["UltimaRecuperacion"] as DateTime?;
                if (ultima != null && DateTime.Now < ultima.Value.AddMinutes(1))
                {
                    ModelState.AddModelError("", "Ya se envió una solicitud recientemente. Intente más tarde.");
                    return View(model);
                }
                Session["UltimaRecuperacion"] = DateTime.Now;

                // Se busca por nombre de usuario o por correo
                string dato = model.Identificador.Trim();
                var user = await UserManager.FindByNameAsync(dato) ?? await UserManager.FindByEmailAsync(dato);

                if (user != null)
                {
                    try
                    {
                        string codigo = await UserManager.GeneratePasswordResetTokenAsync(user.Id);
                        string enlace = Url.Action("ResetPassword", "Account",
                                                   new { userId = user.Id, code = codigo }, Request.Url.Scheme);
                        System.Diagnostics.Debug.WriteLine("[CORREO SIMULADO] Recuperación para " + user.UserName + ": " + enlace);
                    }
                    catch (Exception)
                    {
                        // Si falla, igual se muestra el mensaje neutro (no se revela nada)
                    }
                }

                ViewBag.Enviado = true;
                ModelState.Clear();
                return View(new ForgotPasswordViewModel());
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Ocurrió un error: " + ex.Message);
                return View(model);
            }
        }

        // Pantalla para escribir la contraseña nueva (se llega con el enlace de recuperación)
        [AllowAnonymous]
        public ActionResult ResetPassword(string userId, string code)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
                return RedirectToAction("ForgotPassword");

            return View(new ResetPasswordViewModel { UserId = userId, Code = code });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                var result = await UserManager.ResetPasswordAsync(model.UserId, model.Code, model.Password);
                if (result.Succeeded)
                {
                    ViewBag.Listo = true;
                    return View(new ResetPasswordViewModel());
                }

                // Mensaje general: el enlace pudo vencer o la clave no cumple los requisitos
                foreach (var error in result.Errors)
                    ModelState.AddModelError("", error);
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "El enlace de recuperación no es válido o venció. Solicite uno nuevo.");
            }
            return View(model);
        }

        // ---------- CERRAR SESIÓN ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LogOff()
        {
            AuthenticationManager.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
            return RedirectToAction("Index", "Home");
        }

        // ---------- Métodos de ayuda ----------

        private bool EsSuperUsuario()
        {
            return User.Identity.IsAuthenticated && User.IsInRole("SuperUsuario");
        }

        // Lista de roles del formulario: el visitante solo ve "Cliente"
        private void CargarRoles()
        {
            bool esSuper = EsSuperUsuario();
            ViewBag.EsSuper = esSuper;
            var roles = esSuper ? new List<string> { "Cliente", "Administrador" } : new List<string> { "Cliente" };
            ViewBag.Roles = new SelectList(roles);
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
            public ChallengeResult(string provider, string redirectUri) : this(provider, redirectUri, null) { }

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
                    properties.Dictionary[XsrfKey] = UserId;
                context.HttpContext.GetOwinContext().Authentication.Challenge(properties, LoginProvider);
            }
        }
    }
}