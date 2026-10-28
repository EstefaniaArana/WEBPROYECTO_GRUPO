using System.ComponentModel.DataAnnotations;

namespace AerolineaVIP_MVC.Models
{
    public class LoginViewModel
    {
        // Aquí se escribe el NOMBRE DE USUARIO o el CORREO (se deja el nombre "Email" para no tocar la vista)
        [Required(ErrorMessage = "Ingrese su nombre de usuario o correo.")]
        [StringLength(256)]
        [Display(Name = "Nombre de usuario o correo")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Ingrese su contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; }

        [Display(Name = "Recordarme")]
        public bool RememberMe { get; set; }
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Los nombres son obligatorios.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Los nombres deben tener entre 2 y 100 caracteres.")]
        [RegularExpression(@"^[A-Za-zÁÉÍÓÚÜÑáéíóúüñ ]+$", ErrorMessage = "Los nombres solo admiten letras y espacios.")]
        [Display(Name = "Nombres")]
        public string Nombres { get; set; }

        [Required(ErrorMessage = "Los apellidos son obligatorios.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Los apellidos deben tener entre 2 y 100 caracteres.")]
        [RegularExpression(@"^[A-Za-zÁÉÍÓÚÜÑáéíóúüñ ]+$", ErrorMessage = "Los apellidos solo admiten letras y espacios.")]
        [Display(Name = "Apellidos")]
        public string Apellidos { get; set; }

        [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
        [StringLength(50, MinimumLength = 4, ErrorMessage = "El usuario debe tener entre 4 y 50 caracteres.")]
        [RegularExpression(@"^[A-Za-z0-9._]+$", ErrorMessage = "El usuario solo admite letras, números, punto y guion bajo.")]
        [Display(Name = "Nombre de usuario")]
        public string UserName { get; set; }

        [Required(ErrorMessage = "El correo es obligatorio.")]
        [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
        [StringLength(256)]
        [Display(Name = "Correo electrónico")]
        public string Email { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        [Compare("Password", ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; }

        [Required(ErrorMessage = "Seleccione un rol.")]
        [Display(Name = "Rol")]
        public string Rol { get; set; }
    }

    // Pantalla 4: el usuario escribe su nombre de usuario o su correo
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "Ingrese su correo o nombre de usuario.")]
        [StringLength(256, ErrorMessage = "Máximo 256 caracteres.")]
        [Display(Name = "Correo o nombre de usuario")]
        public string Identificador { get; set; }
    }

    // Fijar una contraseña nueva con el enlace de recuperación
    public class ResetPasswordViewModel
    {
        public string UserId { get; set; }
        public string Code { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva contraseña")]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        [Compare("Password", ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; }
    }
}