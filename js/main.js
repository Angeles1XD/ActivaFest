import { mostrarEventos, buscarEventos } from "../controllers/eventosController.js";

const input = document.getElementById("buscar");
const boton = document.getElementById("btnBuscar");

mostrarEventos();

boton.addEventListener("click", () => {
  const texto = input.value;
  const filtrados = buscarEventos(texto);
  mostrarEventos(filtrados);
});