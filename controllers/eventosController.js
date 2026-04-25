import { eventos } from "../models/evento.js";

export function mostrarEventos(lista = eventos) {
  const contenedor = document.getElementById("eventos");
  contenedor.innerHTML = "";

  lista.forEach(e => {
    contenedor.innerHTML += `
      <div class="card" onclick="verDetalle(${e.id})">
        <h3>${e.nombre}</h3>
        <p>S/ ${e.precio}</p>
      </div>
    `;
  });
}

export function buscarEventos(texto) {
  return eventos.filter(e =>
    e.nombre.toLowerCase().includes(texto.toLowerCase())
  );
}

// 👇 necesario para usar onclick en HTML
window.verDetalle = function(id) {
  localStorage.setItem("eventoId", id);
  window.location.href = "views/detalle.html";
};