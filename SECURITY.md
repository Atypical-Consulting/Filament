# Security Policy

## Reporting a vulnerability

Please **do not** open a public issue for a security vulnerability.

Report it privately through GitHub Security Advisories:
<https://github.com/Atypical-Consulting/Filament/security/advisories/new>

You can expect an acknowledgement within a few days, and an assessment of whether the report is
accepted along with an expected fix timeline.

## Scope

Filament is a **build-time compiler**: it reads `.razor` source you control and emits JavaScript.
It has no server, no network surface, and no runtime privilege boundary. The security-relevant
surface is therefore narrower than most projects, and worth stating explicitly.

**In scope:**

- The generator emitting JavaScript that escapes the semantics of its input in a way an attacker
  could steer — most importantly, **any path where interpolated content reaches the DOM as markup
  rather than as text**. The runtime sets text through `textContent` and attributes through
  `setAttribute` precisely so that component state cannot become executable markup; a hole in that
  is a real vulnerability, not just a bug.
- Path traversal or arbitrary file write in the generator, the SDK targets, or the bench harness's
  static server.
- Dependency vulnerabilities that reach the emitted output or a consumer's build.

**Out of scope:**

- The `Microsoft.AspNetCore.Razor.Language` 6.0.36 dependency being out of support. This is a
  known, documented, deliberate pin — see [docs/adr/0001-eol-razor-mitigation.md](docs/adr/0001-eol-razor-mitigation.md)
  for the mitigation and migration map. It is a build-time parser fed source the developer already
  controls. Reports that only restate the EOL status will be closed as duplicates of that ADR;
  a demonstrated exploit through it is very much in scope.
- Anything requiring the attacker to already control the `.razor` source being compiled. Compiling
  source is equivalent to running it, in Filament as in any compiler.

## Supported versions

Filament is a research project under active development and is explicitly **not yet a shipping
framework**. Only `main` receives fixes; there are no maintained release branches.
