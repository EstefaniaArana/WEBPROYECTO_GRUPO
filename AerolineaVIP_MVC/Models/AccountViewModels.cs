using System.ComponentModel.DataAnnotations;

namespace AerolineaVIP_MVC.Models
{
    public class LoginViewModel
    {
        // Aquí se escribe el NOMBRE DE USUARIO (sin [EmailAddress])
        [Required(ErrorMessage = "El usuario es obligatorio")]
        [Display(Name = "Nombre de usuario")]
        public string Email { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; }

        [Display(Name = "¿Recordar cuenta?")]
        public bool RememberMe { get; set; }
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "El nombre de usuario es obligatorio")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Debe tener entre 3 y 50 caracteres")]
        [Display(Name = "Nombre de usuario")]
        public string UserName { get; set; }

        [Required(ErrorMessage = "El correo es obligatorio")]
        [EmailAddress(ErrorMessage = "Correo no válido")]
        [Display(Name = "Correo electrónico")]
        public string Email { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria")]
        [StringLength(100, ErrorMessage = "La {0} debe tener al menos {2} caracteres.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        [Compare("Password", ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; }

        [Required(ErrorMessage = "Seleccione un rol")]
        [Display(Name = "Rol")]
        public string Rol { get; set; }
    }
}