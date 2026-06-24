# KB SubagentePago

Que hace:
- Consulta pagos del socio en la Turnera.
- Consulta creditos disponibles.
- Informa si hay pagos registrados y creditos disponibles.
- No acredita manualmente creditos sin una accion especifica y segura.

Cuando actuar:
- Pago no reflejado.
- Paquete abonado sin creditos.
- Diferencia entre clases pagadas y acreditadas.
- Consulta de creditos disponibles.

Cuando escalar:
- Pago inexistente.
- Monto no coincide.
- Se acreditaron menos clases que las abonadas.
- Creditos siguen en cero y hace falta acreditacion manual.
- Hay inconsistencia entre pago, paquete y creditos.

## KB-PAGOS-001 - Pago no impacta en creditos
Sistema: pagos
Confianza: alta
Tags: pago, pagos, cobro, credito, creditos, paquete, clases pagadas, no me acreditaron, comprobante, abone
Acciones: consultar_pago
Descripcion: El socio abono un paquete de clases pero sus creditos no se actualizaron en la turnera.
Sintomas: El pago figura realizado o el usuario tiene comprobante, pero los creditos disponibles siguen en cero, no alcanzan para reservar o se acreditaron menos clases que las abonadas.
Causa probable: Error de procesamiento del pago, demora de acreditacion o falla en la actualizacion de creditos.
Datos requeridos: Email del socio, nombre del paquete abonado, fecha del pago, monto y comprobante si lo tiene.
Precondiciones: Consultar pagos y creditos del socio en la turnera antes de decidir.
Accion recomendada: consultar_pago: consultar pagos y creditos del socio en la turnera e informar el estado encontrado.
Validacion: Confirmar si el pago existe, cuantos pagos hay registrados y cuantos creditos disponibles tiene el socio.
Resultado esperado: Estado de pago y creditos informado. Si hay creditos disponibles, el socio queda habilitado para reservar.
Criterios de escalacion: Escalar si el pago no existe, si el monto no coincide, si se acreditaron menos clases que las abonadas, si los creditos siguen en cero y se requiere acreditacion manual, o si hay inconsistencias entre pago y paquete.
Mensaje sugerido: Registre tu consulta sobre el paquete de clases. Voy a verificar el pago y tus creditos disponibles.

