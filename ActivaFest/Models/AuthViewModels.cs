using System.ComponentModel.DataAnnotations;

namespace ActivaFest.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Ingresa tu correo.")]
        [EmailAddress(ErrorMessage = "Correo no válido.")]
        [Display(Name = "Correo electrónico")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Ingresa tu contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = "";

        [Display(Name = "Recordarme")]
        public bool Recordarme { get; set; }

        public string? ReturnUrl { get; set; }
    }

    public class RegistroViewModel
    {
        [Required(ErrorMessage = "Ingresa tu nombre.")]
        [StringLength(100)]
        [Display(Name = "Nombre completo")]
        public string NombreCompleto { get; set; } = "";

        [Required(ErrorMessage = "Ingresa tu correo.")]
        [EmailAddress(ErrorMessage = "Correo no válido.")]
        [Display(Name = "Correo electrónico")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Ingresa una contraseña.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Confirma tu contraseña.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmarPassword { get; set; } = "";

        [Required]
        [Display(Name = "Tipo de cuenta")]
        public string Rol { get; set; } = Roles.Asistente;
    }

    public class OlvideContrasenaViewModel
    {
        [Required(ErrorMessage = "Ingresa tu correo.")]
        [EmailAddress(ErrorMessage = "Correo no válido.")]
        [Display(Name = "Correo electrónico")]
        public string Email { get; set; } = "";
    }

    public class RestablecerContrasenaViewModel
    {
        [Required]
        public string Email { get; set; } = "";

        [Required]
        public string Token { get; set; } = "";

        [Required(ErrorMessage = "Ingresa una contraseña.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva contraseña")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Confirma tu contraseña.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmarPassword { get; set; } = "";
    }
}
