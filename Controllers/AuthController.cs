using Microsoft.AspNetCore.Mvc;

namespace ActivaFest.Controllers
{
    public class AuthController : Controller
    {
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(string usuario, string password)
        {
            if (usuario == "admin" && password == "123")
            {
                HttpContext.Session.SetString("usuario", usuario);
                return RedirectToAction("Index", "Eventos");
            }

            ViewBag.Error = "Credenciales incorrectas";
            return View();
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}