# KB SubagenteSocios

Que hace:
- Consulta si un socio existe por email.
- Verifica datos basicos de cuenta.
- No modifica datos personales sin escalacion.

Cuando actuar:
- Socio no sabe si esta registrado.
- Email no encontrado.
- Dudas sobre perfil o rol.

Cuando escalar:
- Cambiar datos personales.
- Unir usuarios duplicados.
- Crear una cuenta.
- Resolver email que no coincide.

## KB-SOCIOS-001 - Socio no registrado o datos incorrectos
Sistema: socios
Confianza: media
Tags: socio, socios, usuario, usuarios, email, perfil, registro, datos
Acciones: consultar_socio
Descripcion: El socio no aparece registrado o sus datos de perfil no coinciden con lo esperado.
Sintomas: Email no encontrado, perfil con datos incorrectos, usuario duplicado o cuenta sin rol de socio.
Causa probable: Registro incompleto, email mal escrito, usuario duplicado o migracion parcial de datos.
Datos requeridos: Email del socio y dato que se quiere validar.
Precondiciones: Consultar el socio por email antes de modificar datos.
Accion recomendada: consultar_socio: consultar si el socio existe y devolver estado basico de la cuenta.
Validacion: Confirmar existencia del socio y datos principales.
Resultado esperado: Socio identificado o motivo de no identificacion informado.
Criterios de escalacion: Escalar si se requiere modificar datos personales, unir usuarios duplicados, crear una cuenta o resolver un email que no coincide.
Mensaje sugerido: Voy a verificar si tu usuario existe en la turnera con el email informado.

