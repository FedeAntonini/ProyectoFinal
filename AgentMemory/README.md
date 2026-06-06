# AgentMemory

Memoria reciente de casos por modulo/subagente.

La memoria no reemplaza a la KB. Sirve para comparar casos parecidos y mejorar la clasificacion del modulo. Antes de ejecutar una accion, el agente sigue validando contra la KB Markdown.

Estructura generada automaticamente:

```txt
AgentMemory/
  acceso/
    resueltos/
    no_resueltos/
  pagos/
    resueltos/
    no_resueltos/
  turnos/
    resueltos/
    no_resueltos/
```

Cada carpeta mantiene como maximo 30 casos. Cuando se supera el limite, se eliminan los JSON mas viejos.

Se puede cambiar la ubicacion con:

```txt
AGENTAI_MEMORY_PATH=C:\ruta\a\AgentMemory
```
