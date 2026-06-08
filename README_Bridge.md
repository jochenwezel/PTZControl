# PTZControlBridge status

`PTZControlBridge` is an experimental C++/CLI bridge prototype. It is kept in
the source tree for reference, but it is not the current backend for
`PTZControlConsole`.

Current console architecture:

- `PTZControlConsole` references `PTZControl.Uvc` directly.
- Windows camera control is implemented through the console backend abstraction.
- Linux preview support is implemented through the same console backend
  abstraction.
- The current command syntax is documented in [docs/syntax.md](docs/syntax.md)
  and generated help files under [docs/generated](docs/generated).

Bridge limitations in the current source tree:

- `PTZControlBridge` still references a Debug build output of `PTZControl.Uvc`.
- Logitech XU methods in the bridge are placeholders.
- The bridge is not used by the release packaging flow.
- The bridge documentation must not be used as CLI syntax reference.

If the bridge becomes relevant again, update this document together with the
project references, release packaging, and generated CLI documentation.
