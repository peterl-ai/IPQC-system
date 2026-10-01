# JAX Power IPQC Web

Phase 1A Vue web foundation for the JAX Power IPQC system. This project uses mock data only; actions do not persist.

## Local development

```powershell
npm install
npm run dev
```

## Quality gates

```powershell
npm run lint
npm run test
npm run typecheck
npm run build
```

The role selector is a development-only control for checking Admin, PQE, and IPQA navigation and route permissions. The language selector persists English or Simplified Chinese in browser local storage.
