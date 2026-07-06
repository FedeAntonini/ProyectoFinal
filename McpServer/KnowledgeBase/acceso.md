# KB SubagenteAcceso

Que hace:
- Resetea acceso de socios en la Turnera.
- Genera una password temporal cuando la KB lo permite.
- Informa el resultado al usuario.
- No cierra tickets; el cierre queda a cargo de Telegram/API con confirmacion del usuario.

Cuando actuar:
- Problemas de login.
- Credenciales invalidas.
- Password incorrecto.
- Sesion expirada.
- Cuenta bloqueada sin senales de compromiso.

Cuando escalar:
- Cuenta inexistente.
- Sospecha de cuenta comprometida.
- Email no coincide con el solicitante.
- Error persiste luego del reseteo.

## KB-ACCESO-001 - Socio no puede iniciar sesion
Sistema: acceso
Confianza: alta
Tags: acceso, login, sesion, credenciales, password, contrasena, iniciar sesion, no puedo entrar, no me deja ingresar, cuenta bloqueada, error de login
Acciones: AgenteAccionPago
Descripcion: El socio no puede iniciar sesion en la app de la turnera o recibe error de credenciales invalidas.
Sintomas: Error de credenciales, password incorrecto, pantalla de login no avanza, sesion expirada o acceso bloqueado.
Causa probable: Password incorrecto, sesion expirada, cuenta bloqueada o credenciales desactualizadas.
Datos requeridos: Email del socio registrado en la turnera y descripcion del error que aparece en pantalla.
Precondiciones: Verificar que el socio exista en la turnera y que no haya senales de cuenta comprometida o acceso indebido.
Accion recomendada: resetear_acceso: resetear el acceso del socio en la turnera, generar una password temporal y comunicarla al usuario.
Validacion: Confirmar con el usuario que puede iniciar sesion con la password temporal.
Resultado esperado: Password temporal generada, acceso restaurado y socio notificado.
Criterios de escalacion: Escalar si la cuenta no existe en la turnera, si hay sospecha de cuenta comprometida, si el email no coincide con el solicitante o si el error persiste tras el reseteo.
Mensaje sugerido: Entendi tu problema para iniciar sesion. Voy a resetear tu acceso y generarte una password temporal.

