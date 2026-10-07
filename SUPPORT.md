# Support

Use GitHub Issues for reproducible bugs, feature requests, documentation gaps, and release packaging problems.

Do not open public issues for unpatched security vulnerabilities. Use the process in `SECURITY.md` instead.

When opening an issue, include:

- Weir version or commit (shown in System › About)
- install type: Windows installer, Docker, or local development
- operating system and browser
- exact steps to reproduce
- expected result
- actual result
- relevant logs from System › Logs, with secrets and private paths removed

## Windows LAN access

If Weir opens locally but another device on your LAN cannot reach it, allow
`WeirServer.exe` (located under `%LocalAppData%\Weir\current\server\`) through Windows Firewall for your current network profile. If you chose **Devices on my network** before
Weir's rule covered every network profile, choose it again (or use **Try again** on System › About) so Windows asks
once more and replaces the older rule.

