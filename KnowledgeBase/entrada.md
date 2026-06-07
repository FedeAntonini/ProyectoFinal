# KB AgenteEntrada

Responsabilidad:
- Recibir el ticket desde Telegram/API.
- Leer asunto, descripcion, comentarios y datos agregados por Telegram.
- Identificar el modulo afectado.
- Buscar el articulo aplicable en las KB de subagentes.
- Pedir informacion faltante de a un dato por vez.
- Decidir si corresponde ejecutar una accion, continuar, escalar o solo informar estado.

Modulos disponibles:
- acceso: login, sesion, credenciales, password, cuenta bloqueada.
- pago: pagos, paquetes, creditos, acreditaciones, diferencias entre clases abonadas y acreditadas.
- turno: reservas, turnos confirmados, reservas que no aparecen.
- profesores: profesor asignado, instructor elegido, disponibilidad del profesor.
- disponibilidad: cupos, turno completo, lugares disponibles.
- socios: usuario/socio registrado, email, perfil, datos de cuenta.
- clases: clase u horario no visible, agenda, calendario.

Criterios generales de pasamanos:
- Si el ticket esta resuelto, cerrado, cancelado o derivado a segundo nivel, solo informar estado.
- Si falta informacion, pedir un solo dato puntual por Telegram.
- Si hay sospecha de fraude, cuenta comprometida, datos sensibles o impacto a multiples usuarios, escalar.
- Si la KB indica una accion segura, derivar a AgenteAccion con el subagente correspondiente.
- Si el subagente ejecuta una accion, no cerrar el ticket sin confirmacion del usuario.
- Si el usuario confirma con SI, cerrar el ticket.
- Si el usuario responde NO o indica que sigue fallando, escalar a soporte especializado.

