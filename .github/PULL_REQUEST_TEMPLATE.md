## Summary

- 

## Validation

CI runs what the change touches and `ci-passed` gives the verdict. Tick what you ran locally, or mark it not affected:

- [ ] Server: `dotnet build apps/server/Weir.slnx -warnaserror` and `dotnet test apps/server/Weir.slnx`
- [ ] Web: `npm run lint`, `npm run build` and `npm run test` in `apps/web`
- [ ] Contract suite: `python -m pytest tests/contract -q` (see `tests/contract/README.md`)
- [ ] Tray: `dotnet test apps/tray/Weir.Tray.slnx`
- [ ] E2E smoke: `python -m pytest tests/e2e/weir -q` (see `CONTRIBUTING.md`)
- [ ] Docker or Windows package smoke, if packaging changed

## Release impact

- [ ] User-facing change documented
- [ ] Migration or config change considered
- [ ] Security/privacy impact considered
