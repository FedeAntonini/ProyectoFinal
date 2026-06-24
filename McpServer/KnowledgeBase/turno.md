# KB SubagenteTurno

Que hace:
- Consulta reservas/turnos del socio.
- Verifica si una reserva existe en la Turnera.
- Informa estado de la reserva.

Cuando actuar:
- Reserva confirmada no aparece.
- Socio no ve sus turnos.
- Dudas sobre fecha, horario o profesor de una reserva.

Cuando escalar:
- La reserva no existe y hay que crearla manualmente.
- Hay inconsistencia de base.
- Afecta a multiples socios.
- Se requiere modificar una reserva sin confirmacion.

## KB-TURNOS-001 - Reserva confirmada no aparece
Sistema: turnos
Confianza: media
Tags: turno, turnos, reserva, reservas, clase reservada, ver mis clases, no aparece mi reserva, agenda, horario reservado
Acciones: consultar_turnos
Descripcion: El socio realizo una reserva y recibio confirmacion, pero no la ve en su lista de turnos.
Sintomas: Reserva ausente en la app, confirmacion recibida por otro canal, turno no visible en la agenda del socio.
Causa probable: Error de sincronizacion, registro incompleto de reserva o problema de consulta en la app.
Datos requeridos: Email del socio, profesor elegido, fecha y horario de la reserva.
Precondiciones: Verificar si la reserva existe en la base de datos de la turnera.
Accion recomendada: consultar_turnos: consultar las reservas del socio y validar si existe una reserva para la fecha y horario indicados.
Validacion: Confirmar si la reserva aparece en la base y si corresponde al socio.
Resultado esperado: Reserva identificada e informada al usuario. Si falta crear o corregir la reserva, escalar a soporte.
Criterios de escalacion: Escalar si la reserva no existe, si hay inconsistencia de datos, si el problema afecta a multiples socios o si se requiere crear una reserva manual.
Mensaje sugerido: Entendi que tu reserva no aparece. Voy a verificar tus turnos en la turnera.

