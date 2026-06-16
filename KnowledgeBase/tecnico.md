# KB Escalacion Tecnica

Que hace:
- Detecta errores tecnicos o de interfaz en la app de la turnera.
- Escala al equipo de soporte nivel 2 para revision tecnica.
- No intenta resolver errores de codigo o fallas del sistema de forma automatica.

Cuando actuar:
- Boton que no funciona o no responde.
- Pantalla con error o mensaje de falla.
- Accion que no se puede completar por error del sistema.
- Problema tecnico que no es de acceso, pago ni reserva.

Cuando escalar:
- Siempre. Los errores tecnicos requieren revision del equipo de desarrollo.

## KB-TECNICO-001 - Error tecnico o de interfaz en la app
Sistema: tecnico
Confianza: alta
Tags: boton, funciona, falla, error, pantalla, cancelar, cancelacion, interfaz, tecnico, sistema, click, responde, carga, bug, problema tecnico, no puedo, no me deja, intento, intente
Acciones: escalar_ticket
Descripcion: El socio no puede realizar una accion en la app de la turnera por un error tecnico o de interfaz.
Sintomas: El boton no funciona, no me deja cancelar, la pantalla da error, la app no responde, el sistema falla, no puedo hacer click, intente cancelar pero no pude, error al intentar realizar una accion en la app.
Causa probable: Error tecnico en la interfaz o en el servidor de la turnera que requiere intervencion del equipo de desarrollo.
Datos requeridos: Email del socio y descripcion del error o pantalla donde ocurre el fallo.
Precondiciones: Confirmar que el problema no es de acceso (login) ni de pago.
Accion recomendada: escalar_ticket: escalar el incidente al equipo de soporte nivel 2 para revision tecnica.
Validacion: Confirmar con el socio que el problema persiste y registrar los pasos para reproducirlo.
Resultado esperado: El equipo tecnico revisa y resuelve el fallo de interfaz o del sistema.
Criterios de escalacion: Escalar siempre que el error sea tecnico o de interfaz ya que requiere intervencion del equipo de desarrollo.
Mensaje sugerido: Detectamos un problema tecnico con la aplicacion. Lo estamos escalando al equipo de soporte nivel 2 para que lo revisen a la brevedad.
