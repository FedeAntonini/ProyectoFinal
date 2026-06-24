# KB SubagenteDisponibilidad

Que hace:
- Consulta cupos ocupados y disponibles para profesor, fecha y horario.
- Informa disponibilidad real.
- No modifica capacidad ni cupos.

Cuando actuar:
- Turno figura completo.
- Socio quiere saber si hay lugares.
- Hay dudas sobre cupos reales.

Cuando escalar:
- El turno esta realmente completo.
- Hay inconsistencia de cupos.
- Se requiere cambiar capacidad del turno.
- El problema afecta a multiples horarios.

## KB-DISPONIBILIDAD-001 - Turno figura completo
Sistema: disponibilidad
Confianza: media
Tags: disponibilidad, cupo, cupos, completo, sin lugares, no puedo reservar, clase llena, no hay lugar
Acciones: consultar_disponibilidad
Descripcion: La turnera muestra un turno como completo aunque deberia tener lugares disponibles.
Sintomas: Turno sin cupos visibles, clase bloqueada, el socio no puede reservar aunque cree que hay disponibilidad.
Causa probable: Conteo de reservas desactualizado, cancelacion no procesada o limite de cupos alcanzado.
Datos requeridos: Nombre del profesor, fecha, horario y clase reportada como completa.
Precondiciones: Consultar cupos y reservas reales del turno antes de modificar disponibilidad.
Accion recomendada: consultar_disponibilidad: consultar cupos y cantidad real de reservas del turno.
Validacion: Confirmar cupos totales, reservas activas y disponibilidad real.
Resultado esperado: Disponibilidad informada. Si hay inconsistencia, escalar para correccion manual.
Criterios de escalacion: Escalar si el turno esta realmente completo, si hay inconsistencia en cupos, si afecta a multiples horarios o si se requiere cambiar la capacidad del turno.
Mensaje sugerido: Entendi que el turno figura completo. Voy a verificar la disponibilidad real.

