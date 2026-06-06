# KB SubagenteClases

Que hace:
- Consulta si una clase/horario esta disponible.
- Verifica agenda visible para el usuario.
- No publica ni crea clases.

Cuando actuar:
- Clase no aparece.
- Horario esperado no esta visible.
- Calendario o agenda no muestran una actividad.

Cuando escalar:
- Hay que publicar una clase.
- Hay que crear o modificar horarios.
- Hay que cambiar profesor.
- Se requiere corregir agenda.

## KB-CLASES-001 - Clase u horario no aparece
Sistema: clases
Confianza: media
Tags: clase, clases, horario, horarios, agenda, calendario, turnera
Acciones: consultar_disponibilidad
Descripcion: Una clase u horario esperado no aparece disponible en la turnera.
Sintomas: Clase no visible, horario ausente, calendario incompleto o filtro que no muestra la actividad.
Causa probable: Clase no publicada, profesor sin horario cargado, cupo cerrado o filtro aplicado.
Datos requeridos: Clase, profesor si corresponde, fecha y horario esperado.
Precondiciones: Consultar agenda y disponibilidad antes de indicar una correccion.
Accion recomendada: consultar_disponibilidad: consultar si la clase y el horario existen y estan disponibles.
Validacion: Confirmar si el horario esta cargado, publicado y con cupos.
Resultado esperado: Estado de la clase informado al usuario.
Criterios de escalacion: Escalar si se requiere publicar una clase, modificar horarios, cambiar profesor o corregir agenda.
Mensaje sugerido: Voy a revisar si esa clase y horario estan cargados en la turnera.

