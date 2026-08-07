"use strict";

/*
  Cliente de demostración para usuarios interceptados (HU-048 / T-110 y T-111).

  Recorre el flujo que el Capítulo IV describe: inicio de sesión, petición interceptada,
  desafío de segundo factor fuera de banda, verificación y reintento de la petición original.

  Vive en el mismo origen que la pasarela a propósito. El token de refresco viaja en una
  cookie HttpOnly con SameSite=Strict y Path=/auth/refresh, que un origen distinto no podría
  enviar; servir este cliente aparte obligaría a relajar esa cookie, que es justamente la
  defensa que deja el refresco fuera del alcance de un XSS (SRS §3.6).
*/

const $ = (id) => document.getElementById(id);

const estado = {
  token: null,
  usuario: null,
  desafio: null,   // { challengeId, peticion } mientras haya uno pendiente
};

// ── Presentación ─────────────────────────────────────────────────────────────

function mostrarVeredicto(clase, titulo, texto) {
  $("seccion-resultado").hidden = false;
  $("veredicto").className = "veredicto " + clase;
  $("veredicto-titulo").textContent = titulo;
  $("veredicto-texto").textContent = texto;
}

function mostrarCuerpo(valor) {
  $("respuesta").textContent =
    typeof valor === "string" ? valor : JSON.stringify(valor, null, 2);
}

async function leerCuerpo(respuesta) {
  const texto = await respuesta.text();
  if (!texto) return null;
  try { return JSON.parse(texto); } catch { return texto; }
}

function ocupado(boton, activo) { boton.disabled = activo; }

// ── 1 · Inicio de sesión ─────────────────────────────────────────────────────

$("btn-login").addEventListener("click", async () => {
  const boton = $("btn-login");
  ocupado(boton, true);
  $("estado-login").textContent = "";

  try {
    const respuesta = await fetch("/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "same-origin",   // para que el navegador acepte la cookie del refresco
      body: JSON.stringify({ username: $("usuario").value, password: $("clave").value }),
    });

    const cuerpo = await leerCuerpo(respuesta);

    if (respuesta.status === 423) {
      $("estado-login").textContent = "Cuenta bloqueada temporalmente por intentos fallidos.";
      return;
    }
    if (!respuesta.ok || !cuerpo || !cuerpo.accessToken) {
      $("estado-login").textContent = "Credenciales inválidas.";
      return;
    }

    estado.token = cuerpo.accessToken;
    estado.usuario = $("usuario").value;

    $("sesion-usuario").textContent = estado.usuario;
    $("sesion-exp").textContent = "";
    $("seccion-sesion").hidden = false;
    $("seccion-peticion").hidden = false;
    $("estado-login").textContent = "Sesión iniciada.";
  } catch {
    $("estado-login").textContent = "No se pudo contactar con la pasarela.";
  } finally {
    ocupado(boton, false);
  }
});

// ── 2 · Petición a un servicio protegido ─────────────────────────────────────

function construirPeticion() {
  const cabeceras = { "Authorization": "Bearer " + estado.token };

  const ip = $("ip").value;
  if (ip) cabeceras["X-Forwarded-For"] = ip;

  // La huella del dispositivo es un resumen de User-Agent, Accept-Language y
  // Accept-Encoding. De los tres, Accept-Language es el único que el navegador permite
  // fijar desde fetch, así que cambiarlo basta para que la petición llegue con una huella
  // distinta y el sistema la trate como un dispositivo que no reconoce.
  if ($("dispositivo-desconocido").checked) {
    cabeceras["Accept-Language"] = "qq-ZZ";
  }

  return { ruta: $("servicio").value, cabeceras };
}

/**
 * Envía la petición y traduce la respuesta de la pasarela al veredicto que la produjo.
 * Se guarda la petición original entera porque, si el veredicto es de desafío, hay que
 * reintentarla tal cual una vez verificado el segundo factor.
 */
async function ejecutar(peticion) {
  const respuesta = await fetch(peticion.ruta, {
    method: "GET",
    headers: peticion.cabeceras,
    credentials: "same-origin",
  });
  const cuerpo = await leerCuerpo(respuesta);

  if (respuesta.status === 401 && cuerpo && cuerpo.errorCode === "MFA_REQUIRED") {
    estado.desafio = { challengeId: cuerpo.challengeId, peticion };
    $("seccion-desafio").hidden = false;
    $("otp").value = "";
    $("otp").focus();
    mostrarVeredicto("challenge", "CHALLENGE",
      "Se requiere verificación adicional antes de conceder el acceso. La petición no se " +
      "reenvió al servicio de destino.");
    mostrarCuerpo(cuerpo);
    return;
  }

  $("seccion-desafio").hidden = true;
  estado.desafio = null;

  if (respuesta.ok) {
    mostrarVeredicto("allow", "ALLOW",
      "El riesgo quedó por debajo del umbral: la petición se reenvió al servicio de destino " +
      "y esta es su respuesta.");
  } else if (respuesta.status === 403) {
    mostrarVeredicto("block", "BLOCK",
      "La pasarela denegó el acceso. El motivo detallado queda en el registro de auditoría " +
      "y no se expone al cliente.");
  } else if (respuesta.status === 401) {
    mostrarVeredicto("block", "NO AUTORIZADO",
      "El token no es válido o ha expirado. Renueve la sesión e inténtelo de nuevo.");
  } else if (respuesta.status === 423) {
    mostrarVeredicto("block", "CUENTA BLOQUEADA",
      "La cuenta quedó bloqueada tras agotar los intentos permitidos.");
  } else if (respuesta.status === 429) {
    mostrarVeredicto("block", "LÍMITE DE TASA",
      "El limitador por IP cortó la petición antes de que llegara al motor de riesgo.");
  } else {
    mostrarVeredicto("neutro", "HTTP " + respuesta.status,
      "Respuesta inesperada de la pasarela.");
  }

  mostrarCuerpo(cuerpo ?? "(sin cuerpo)");
}

$("btn-peticion").addEventListener("click", async () => {
  const boton = $("btn-peticion");
  ocupado(boton, true);
  $("estado-peticion").textContent = "Evaluando…";
  try {
    await ejecutar(construirPeticion());
    $("estado-peticion").textContent = "";
  } catch {
    $("estado-peticion").textContent = "No se pudo contactar con la pasarela.";
  } finally {
    ocupado(boton, false);
  }
});

// ── 3 · Verificación del desafío y reanudación ───────────────────────────────

$("btn-verificar").addEventListener("click", async () => {
  if (!estado.desafio) return;

  const boton = $("btn-verificar");
  ocupado(boton, true);
  $("estado-desafio").textContent = "";

  try {
    const respuesta = await fetch("/auth/challenge/verify", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "same-origin",
      body: JSON.stringify({
        challengeId: estado.desafio.challengeId,
        otp: $("otp").value.trim(),
      }),
    });

    const cuerpo = await leerCuerpo(respuesta);

    if (respuesta.status === 423) {
      mostrarVeredicto("block", "CUENTA BLOQUEADA",
        "Se agotaron los intentos de verificación permitidos y la cuenta quedó bloqueada.");
      mostrarCuerpo(cuerpo ?? "(sin cuerpo)");
      $("seccion-desafio").hidden = true;
      estado.desafio = null;
      return;
    }

    if (!respuesta.ok) {
      const codigo = cuerpo && cuerpo.errorCode;
      $("estado-desafio").textContent = codigo === "MFA_EXPIRED"
        ? "El desafío expiró. Vuelva a enviar la petición."
        : "Código incorrecto. Inténtelo de nuevo.";
      return;
    }

    // Verificado. El step-up queda vigente en Redis durante una ventana breve y la petición
    // original se reintenta tal cual. El riesgo no baja: se autoriza continuar pese a él, de
    // forma acotada en el tiempo y auditada (SRS §9.9).
    $("estado-desafio").textContent = "Verificado. Reintentando la petición original…";
    await ejecutar(estado.desafio.peticion);
  } catch {
    $("estado-desafio").textContent = "No se pudo contactar con la pasarela.";
  } finally {
    ocupado(boton, false);
  }
});

// ── Sesión ───────────────────────────────────────────────────────────────────

$("btn-refresh").addEventListener("click", async () => {
  const boton = $("btn-refresh");
  ocupado(boton, true);
  try {
    const respuesta = await fetch("/auth/refresh", { method: "POST", credentials: "same-origin" });
    const cuerpo = await leerCuerpo(respuesta);

    if (respuesta.ok && cuerpo && cuerpo.accessToken) {
      estado.token = cuerpo.accessToken;
      $("sesion-exp").textContent = "· token renovado";
    } else {
      $("sesion-exp").textContent = "· no se pudo renovar";
    }
  } catch {
    $("sesion-exp").textContent = "· no se pudo renovar";
  } finally {
    ocupado(boton, false);
  }
});

$("btn-logout").addEventListener("click", async () => {
  const boton = $("btn-logout");
  ocupado(boton, true);
  try {
    await fetch("/auth/logout", { method: "POST", credentials: "same-origin" });
  } catch {
    // Cerrar la sesión en el cliente no debe depender de que la llamada llegue a su destino.
  } finally {
    estado.token = null;
    estado.usuario = null;
    estado.desafio = null;
    $("seccion-sesion").hidden = true;
    $("seccion-peticion").hidden = true;
    $("seccion-desafio").hidden = true;
    $("seccion-resultado").hidden = true;
    $("estado-login").textContent = "Sesión cerrada.";
    ocupado(boton, false);
  }
});
