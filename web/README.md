# JAX Power IPQC Web

Vue web application for the JAX Power IPQC system. Phase 1B adds the complete Patrol Standards frontend workflow using in-memory mock data; browser refreshes reset all changes.

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
