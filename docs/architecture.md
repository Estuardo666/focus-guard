# Architecture

```text
Avalonia Desktop
      |
Versioned Named Pipes
      |
Windows Service (authoritative state)
   |                 |
Process adapter      Native messaging host
                     |
                 Browser extension
```

Domain rules remain platform-independent.

The desktop UI never terminates processes or decides whether a session expired. The service owns policy activation, expiration, and enforcement state.

During safe development, desktop and service use simulator implementations.
