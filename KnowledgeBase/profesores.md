# KB SubagenteProfesores

Que hace:
- Verifica profesor/instructor relacionado a una reserva.
- Consulta disponibilidad del profesor en fecha y horario.
- Informa si el profesor dicta clase y si hay cupos disponibles.

Cuando actuar:
- Profesor asignado no coincide.
- Se quiere validar si un profesor da clase un dia y horario.
- Hay dudas sobre instructor elegido.

Cuando escalar:
- Hay que modificar o reasignar una reserva sin confirmacion explicita.
- El profesor elegido no tiene disponibilidad.
- Hay conflicto entre profesores.
- La base no permite validar el horario.

## KB-PROFESORES-001 - Profesor asignado no coincide
Sistema: profesores
Confianza: media
Tags: profesor, profesores, instructor, instructores, reserva, turno, clase, asignacion, disponibilidad, horario
Acciones: consultar_disponibilidad
Descripcion: El socio eligio un profesor al reservar pero la confirmacion muestra un profesor diferente.
Sintomas: Profesor diferente en la reserva, instructor incorrecto, clase confirmada con otro profesor.
Causa probable: Conflicto de disponibilidad, error de asignacion de reserva o cambio manual del turno.
Datos requeridos: Email del socio si hay una reserva existente, profesor elegido originalmente, profesor que figura en la reserva, fecha y horario.
Precondiciones: Consultar disponibilidad del profesor y el horario en la turnera antes de modificar cualquier dato.
Accion recomendada: consultar_disponibilidad: consultar si el profesor elegido dicta clase en esa fecha y horario y si hay cupos disponibles.
Validacion: Confirmar disponibilidad del profesor, horario y cupos disponibles.
Resultado esperado: Disponibilidad del profesor verificada. Si solo era una consulta, informar resultado. Si se necesita cambiar una reserva, pedir confirmacion o escalar.
Criterios de escalacion: Escalar si el profesor elegido no tiene disponibilidad, si se requiere cambiar la reserva sin confirmacion del usuario, si hay conflicto entre profesores o si la base no permite validar el horario.
Mensaje sugerido: Veo que hay una diferencia con el profesor de tu reserva. Voy a verificar si el profesor elegido tiene disponibilidad en ese horario.

