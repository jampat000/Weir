## Summary

- 

## Validation

CI runs what the change touches and `ci-passed` gives the verdict. Tick what you ran locally, or mark it not affected:

- [ ] Server: `dotnet build apps/server/Weir.slnx -warnaserror` and `dotnet test apps/server/Weir.slnx`
- [ ] Web: `npm run lint`, `npm run build` and `npm run test` in `apps/web`
- [ ] Contract suite: `dotnet test apps/server/tests/Weir.Contract.Tests --filter "Area=<area>"` (see its `README.md`)
- [ ] Tray: `dotnet test apps/tray/Weir.Tray.slnx`
- [ ] E2E smoke: `WEIR_E2E=1 dotnet test apps/server/tests/Weir.E2E.Tests` (see `CONTRIBUTING.md`)
- [ ] Docker or Windows package smoke, if packaging changed

## Release impact

- [ ] User-facing change documented
- [ ] Migration or config change considered
- [ ] Security/privacy impact considered
