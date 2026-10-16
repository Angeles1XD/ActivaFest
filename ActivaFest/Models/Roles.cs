namespace ActivaFest.Models
{
    // Perfiles definidos en el alcance del proyecto (RF01)
    public static class Roles
    {
        public const string Asistente = "Asistente";
        public const string Organizador = "Organizador";
        public const string ControlAcceso = "ControlAcceso";
        public const string Administrador = "Administrador";

        public static readonly string[] Todos = { Asistente, Organizador, ControlAcceso, Administrador };

        // Roles que un usuario puede elegir al registrarse por su cuenta
        public static readonly string[] Registrables = { Asistente, Organizador };

        // Nombre que ve la gente. Internamente se mantiene "Asistente" (RF01 del documento),
        // pero en pantalla se muestra "Usuario" para no confundirlo con el personal del evento.
        public static string NombreVisible(string rol) => rol switch
        {
            Asistente => "Usuario",
            ControlAcceso => "Control de acceso",
            _ => rol
        };
    }
}
