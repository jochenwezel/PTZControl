# Repository guidance for agents

- When command syntax is changed or extended, update the documentation in
  `docs/` and, when relevant, `README.md` in the same change.
- Keep CLI help generated from the CommandLineParser verb/option metadata. Do
  not introduce a second manually maintained public verb list for user-facing
  help output.
- After changing CLI verbs, options, help text, or sample output, run
  `PTZControlConsole docs --output docs/generated` and verify with
  `PTZControlConsole docs --output docs/generated --check`.
- Keep simulated/example command output in the documentation up to date whenever
  the corresponding command output changes.
- Guided camera test scripts should prefer long options for readability in test
  logs, even when short options exist.
- Use `--pan`/`--tilt` for move command documentation and guided tests. The
  short aliases are `-x` and `-y`.
- Unknown command-line parameters must fail loudly. Do not silently ignore
  parser errors or selector options that are not valid for a verb.
- Always create GitHub issues in English.
